using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using Geomatica.Desktop.Models;
using Geomatica.Desktop.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Esri.ArcGISRuntime.Symbology;

namespace Geomatica.Desktop.ViewModels;

public partial class MapaViewModel
{
    [RelayCommand]
    public async Task ToggleModo3DAsync()
    {
        IsModo3D = !IsModo3D;
        Modo3DTextoIcono = IsModo3D ? "🗺️ 2D" : "🌐 3D";

        if (IsModo3D)
        {
            if (Scene == null)
            {
                SetupScene();
            }

            await EnfocarCamaraModo3DAsync();
            _notifications?.ShowInfo("Visor 3D activado con relieve topográfico. Use clic derecho sostenido para orbitar e inclinar.", "Modo 3D");
        }
        else
        {
            _notifications?.ShowInfo("Visor 2D activado.", "Modo 2D");
        }
    }

    [RelayCommand]
    public async Task InclinarCamara45Async()
    {
        if (_ownerSceneView == null) return;
        try
        {
            if (HasCapa3DActiva && (Capa3DSeleccionada != null || UltimoExtent3D != null))
            {
                await VistaPerspectiva3DAsync();
                return;
            }

            var currentCam = _ownerSceneView.Camera;
            if (currentCam != null)
            {
                var newCam = currentCam.RotateTo(0.0, 45.0, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(newCam, TimeSpan.FromSeconds(0.8));
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task InclinarCamaraCenitalAsync()
    {
        if (_ownerSceneView == null) return;
        try
        {
            if (HasCapa3DActiva && UltimoExtent3D != null)
            {
                await VistaCenital3DAsync();
                return;
            }

            var currentCam = _ownerSceneView.Camera;
            if (currentCam != null)
            {
                var newCam = currentCam.RotateTo(currentCam.Heading, 0.0, currentCam.Roll);
                await _ownerSceneView.SetViewpointCameraAsync(newCam, TimeSpan.FromSeconds(0.8));
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task ResetearCamara3DAsync()
    {
        if (_ownerSceneView == null) return;
        try
        {
            var currentCam = _ownerSceneView.Camera;
            if (currentCam != null)
            {
                var newCam = currentCam.RotateTo(0.0, currentCam.Pitch, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(newCam, TimeSpan.FromSeconds(0.8));
            }
        }
        catch { }
    }

    public static double CalcularRadioMetros(Envelope? extent)
    {
        if (extent == null) return 150.0;

        try
        {
            if (extent.SpatialReference != null && extent.SpatialReference.Wkid == 4326)
            {
                double latCenter = (extent.YMin + extent.YMax) / 2.0;
                double rad = latCenter * Math.PI / 180.0;
                double metersPerDegLon = 111_320.0 * Math.Cos(rad);
                double metersPerDegLat = 111_320.0;

                double dx = extent.Width * metersPerDegLon;
                double dy = extent.Height * metersPerDegLat;
                double dz = extent.HasZ ? extent.Depth : 0.0;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2.0;
                return Math.Max(r, 40.0);
            }
            else
            {
                double dx = extent.Width;
                double dy = extent.Height;
                double dz = extent.HasZ ? extent.Depth : 0.0;
                double r = Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2.0;
                return Math.Max(r, 40.0);
            }
        }
        catch
        {
            return 150.0;
        }
    }

    private async Task AsegurarSceneViewListoAsync(int timeoutMs = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (_ownerSceneView != null && _ownerSceneView.ActualWidth > 50 && _ownerSceneView.ActualHeight > 50)
            {
                // 1. Asegurar que la Scene esté cargada en ArcGIS Runtime (crucial en la primera capa 3D)
                if (_ownerSceneView.Scene != null && _ownerSceneView.Scene.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                {
                    try
                    {
                        await _ownerSceneView.Scene.LoadAsync();
                    }
                    catch { }
                }

                // 2. Asegurar que la superficie de elevación esté cargada
                if (_ownerSceneView.Scene?.BaseSurface != null)
                {
                    if (_ownerSceneView.Scene.BaseSurface.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                    {
                        try
                        {
                            await _ownerSceneView.Scene.BaseSurface.LoadAsync();
                        }
                        catch { }
                    }

                    foreach (var src in _ownerSceneView.Scene.BaseSurface.ElevationSources)
                    {
                        if (src.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                        {
                            try { await src.LoadAsync(); } catch { }
                        }
                    }
                }

                // 3. Si la cámara aún está en el origen mundial o sin posición, anclarla primero en Colombia
                if (_ownerSceneView.Camera == null ||
                    (Math.Abs(_ownerSceneView.Camera.Location.X) < 1.0 && Math.Abs(_ownerSceneView.Camera.Location.Y) < 1.0))
                {
                    _ownerSceneView.SetViewpointCamera(CamColombia3D);
                }

                // 4. Sincronizar GraphicsOverlays si aún no estuvieran en el SceneView
                if (_ownerSceneView.GraphicsOverlays != null)
                {
                    foreach (var capa in Capas3D)
                    {
                        if (capa.OverlayGuia3D != null && !_ownerSceneView.GraphicsOverlays.Contains(capa.OverlayGuia3D))
                            _ownerSceneView.GraphicsOverlays.Add(capa.OverlayGuia3D);
                        if (capa.OverlayPuntos3D != null && !_ownerSceneView.GraphicsOverlays.Contains(capa.OverlayPuntos3D))
                            _ownerSceneView.GraphicsOverlays.Add(capa.OverlayPuntos3D);
                    }
                }

                // 5. Permitir que WPF y el despachador de renderizado completen el ciclo de presentación
                if (Application.Current != null)
                {
                    try
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    }
                    catch { }
                }

                await Task.Delay(150);
                return;
            }
            await Task.Delay(40);
        }
    }

    [RelayCommand]
    public async Task ZoomCapa3DAsync()
    {
        if (Capa3DSeleccionada != null)
        {
            await ZoomACapa3DAsync(Capa3DSeleccionada);
            return;
        }

        if (UltimoExtent3D != null)
        {
            await AsegurarSceneViewListoAsync();
            if (_ownerSceneView == null) return;
            try
            {
                var center = UltimoExtent3D.GetCenter();
                var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                    ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                    : center;

                double groundElev = double.NaN;
                if (_ownerSceneView.Scene?.BaseSurface != null)
                {
                    try
                    {
                        var elev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(wgs84Center);
                        if (!double.IsNaN(elev)) groundElev = elev;
                    }
                    catch { }
                }

                double targetZ = UltimoCentroZ3D + OffsetZ3D;
                if (!double.IsNaN(groundElev) && targetZ < groundElev)
                {
                    targetZ = groundElev + Math.Max(2.0, UltimoCentroZ3D) + OffsetZ3D;
                }
                else if (targetZ <= 0.0)
                {
                    targetZ = 960.0 + OffsetZ3D;
                }

                double radio = UltimoRadioMetros3D > 0 ? UltimoRadioMetros3D : CalcularRadioMetros(UltimoExtent3D);
                double distance = Math.Clamp(radio * 2.5, 50.0, 30_000.0);

                // Perspectiva inclinada a 45° con validación de relieve
                double pitch = 45.0;
                double pitchRad = pitch * Math.PI / 180.0;
                double groundDistSouth = distance * Math.Sin(pitchRad);
                double eyeLat = wgs84Center.Y - (groundDistSouth / 111_320.0);
                double eyeLon = wgs84Center.X;
                double eyeAltitude = targetZ + distance * Math.Cos(pitchRad);

                if (_ownerSceneView.Scene?.BaseSurface != null)
                {
                    try
                    {
                        var eyeTerrainElev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(new MapPoint(eyeLon, eyeLat, SpatialReferences.Wgs84));
                        if (!double.IsNaN(eyeTerrainElev) && eyeAltitude < eyeTerrainElev + 25.0)
                        {
                            double neededAlt = eyeTerrainElev + 35.0;
                            distance = Math.Max(distance, (neededAlt - targetZ) / Math.Cos(pitchRad));
                            distance = Math.Clamp(distance, 50.0, 40_000.0);
                        }
                    }
                    catch { }
                }

                var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
                var camera = new Camera(lookAtTarget, distance, 0.0, pitch, 0.0);

                if (_ownerSceneView.Camera == null ||
                    (Math.Abs(_ownerSceneView.Camera.Location.X) < 1.0 && Math.Abs(_ownerSceneView.Camera.Location.Y) < 1.0))
                {
                    _ownerSceneView.SetViewpointCamera(camera);
                }
                else
                {
                    await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(0.8));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Error en ZoomCapa3DAsync: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public async Task VistaCenital3DAsync()
    {
        var targetItem = Capa3DSeleccionada;
        var extent = targetItem?.ExtentParaZoom ?? UltimoExtent3D;
        if (extent == null) return;
        await AsegurarSceneViewListoAsync();
        if (_ownerSceneView == null) return;
        try
        {
            var center = extent.GetCenter();
            var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                : center;

            double groundElev = double.NaN;
            if (_ownerSceneView.Scene?.BaseSurface != null)
            {
                try
                {
                    var elev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(wgs84Center);
                    if (!double.IsNaN(elev)) groundElev = elev;
                }
                catch { }
            }

            double rawZ = (targetItem?.CentroZ ?? UltimoCentroZ3D) + (targetItem?.OffsetZ3D ?? OffsetZ3D);
            double targetZ = (!double.IsNaN(groundElev) && rawZ < groundElev)
                ? groundElev + 5.0 + (targetItem?.OffsetZ3D ?? OffsetZ3D)
                : (rawZ <= 0.0 ? 960.0 : rawZ);

            double radio = targetItem?.RadioMetros ?? (UltimoRadioMetros3D > 0 ? UltimoRadioMetros3D : CalcularRadioMetros(extent));
            double distance = Math.Clamp(radio * 2.0, 50.0, 25_000.0);

            var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
            var camera = new Camera(lookAtTarget, distance, 0.0, 0.0, 0.0);
            await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(0.9));
        }
        catch { }
    }

    [RelayCommand]
    public async Task VistaPerspectiva3DAsync()
    {
        if (Capa3DSeleccionada != null)
        {
            await ZoomACapa3DAsync(Capa3DSeleccionada);
            return;
        }

        if (UltimoExtent3D != null)
        {
            await ZoomCapa3DAsync();
            return;
        }

        if (_ownerSceneView != null)
        {
            var currentCam = _ownerSceneView.Camera;
            if (currentCam != null)
            {
                var newCam = currentCam.RotateTo(0.0, 45.0, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(newCam, TimeSpan.FromSeconds(0.8));
            }
        }
    }

    public async Task ZoomACapa3DAsync(CapaUsuarioItem item)
    {
        Capa3DSeleccionada = item;
        if (item.ExtentParaZoom == null) return;

        try
        {
            if (!IsModo3D)
            {
                IsModo3D = true;
                Modo3DTextoIcono = "🗺️ 2D";
            }

            if (Scene == null)
            {
                SetupScene();
            }

            // 1. Asegurar que SceneView esté adjunto y con dimensiones válidas en el árbol visual
            await AsegurarSceneViewListoAsync();
            if (_ownerSceneView == null) return;

            var extent = item.ExtentParaZoom;
            var center = extent.GetCenter();
            var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                : center;

            // 2. Determinar radio en metros
            double radio = item.RadioMetros;
            if (radio <= 0.0)
            {
                radio = CalcularRadioMetros(extent);
                item.RadioMetros = radio;
            }

            // 3. Consultar la elevación del terreno en la superficie base 3D
            double groundElev = double.NaN;
            if (_ownerSceneView.Scene?.BaseSurface != null)
            {
                try
                {
                    if (_ownerSceneView.Scene.BaseSurface.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                    {
                        await _ownerSceneView.Scene.BaseSurface.LoadAsync();
                    }
                    var elev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(wgs84Center);
                    if (!double.IsNaN(elev))
                    {
                        groundElev = elev;
                    }
                }
                catch { }
            }

            // 4. Calcular elevación Z objetivo segura (evitar que quede bajo tierra)
            double targetZ = item.CentroZ + item.OffsetZ3D;
            if (!double.IsNaN(groundElev))
            {
                if (targetZ < groundElev)
                {
                    targetZ = groundElev + Math.Max(2.0, item.CentroZ) + item.OffsetZ3D;
                }
            }
            else if (targetZ <= 0.0)
            {
                targetZ = 960.0 + item.OffsetZ3D;
            }

            // 5. Distancia óptima para perspectiva 3D inclinada a 45°
            double distance = Math.Clamp(radio * 2.5, 50.0, 30_000.0);

            // 6. Verificar y evitar colisión de la cámara con relieve elevado al sur
            double pitch = 45.0;
            double pitchRad = pitch * Math.PI / 180.0;
            double groundDistSouth = distance * Math.Sin(pitchRad);
            double eyeLat = wgs84Center.Y - (groundDistSouth / 111_320.0);
            double eyeLon = wgs84Center.X;
            double eyeAltitude = targetZ + distance * Math.Cos(pitchRad);

            if (_ownerSceneView.Scene?.BaseSurface != null)
            {
                try
                {
                    var eyeTerrainElev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(new MapPoint(eyeLon, eyeLat, SpatialReferences.Wgs84));
                    if (!double.IsNaN(eyeTerrainElev) && eyeAltitude < eyeTerrainElev + 25.0)
                    {
                        double neededAlt = eyeTerrainElev + 35.0;
                        distance = Math.Max(distance, (neededAlt - targetZ) / Math.Cos(pitchRad));
                        distance = Math.Clamp(distance, 50.0, 40_000.0);
                    }
                }
                catch { }
            }

            var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
            var camera45 = new Camera(lookAtTarget, distance, 0.0, pitch, 0.0);

            // Si la cámara aún está en el origen mundial o sin posición, aplicar directamente para evitar deriva
            if (_ownerSceneView.Camera == null ||
                (Math.Abs(_ownerSceneView.Camera.Location.X) < 1.0 && Math.Abs(_ownerSceneView.Camera.Location.Y) < 1.0))
            {
                _ownerSceneView.SetViewpointCamera(camera45);
            }
            else
            {
                await _ownerSceneView.SetViewpointCameraAsync(camera45, TimeSpan.FromSeconds(0.8));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Error en ZoomACapa3DAsync: {ex.Message}");
        }
    }

    [RelayCommand]
    public void SubirAltura3D()
    {
        if (Capa3DSeleccionada != null)
        {
            double step = Capa3DSeleccionada.RadioMetros < 25.0 ? 1.0 : 25.0;
            Capa3DSeleccionada.OffsetZ3D += step;
            OffsetZ3D = Capa3DSeleccionada.OffsetZ3D;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    [RelayCommand]
    public void BajarAltura3D()
    {
        if (Capa3DSeleccionada != null)
        {
            double step = Capa3DSeleccionada.RadioMetros < 25.0 ? 1.0 : 25.0;
            Capa3DSeleccionada.OffsetZ3D -= step;
            OffsetZ3D = Capa3DSeleccionada.OffsetZ3D;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    [RelayCommand]
    public void ResetearAltura3D()
    {
        if (Capa3DSeleccionada != null)
        {
            Capa3DSeleccionada.OffsetZ3D = 0.0;
            OffsetZ3D = 0.0;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    [RelayCommand]
    public void AumentarTamanoPuntos3D()
    {
        if (Capa3DSeleccionada != null)
        {
            Capa3DSeleccionada.TamanoPunto3D = Math.Min(14.0, Capa3DSeleccionada.TamanoPunto3D + 1.0);
            TamanoPunto3D = Capa3DSeleccionada.TamanoPunto3D;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    [RelayCommand]
    public void DisminuirTamanoPuntos3D()
    {
        if (Capa3DSeleccionada != null)
        {
            Capa3DSeleccionada.TamanoPunto3D = Math.Max(1.5, Capa3DSeleccionada.TamanoPunto3D - 1.0);
            TamanoPunto3D = Capa3DSeleccionada.TamanoPunto3D;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    private async Task EnfocarCamaraModo3DAsync()
    {
        await AsegurarSceneViewListoAsync();
        if (_ownerSceneView == null) return;

        if (Capa3DSeleccionada != null)
        {
            await ZoomACapa3DAsync(Capa3DSeleccionada);
            return;
        }

        if (UltimoExtent3D != null)
        {
            await ZoomCapa3DAsync();
            return;
        }

        if (LastViewpoint != null)
        {
            MapPoint? targetPt = LastViewpoint.TargetGeometry as MapPoint;
            if (targetPt == null && LastViewpoint.TargetGeometry?.Extent != null)
            {
                targetPt = LastViewpoint.TargetGeometry.Extent.GetCenter();
            }

            if (targetPt != null)
            {
                var wgs84 = (targetPt.SpatialReference != null && targetPt.SpatialReference.Wkid != 4326)
                    ? GeometryEngine.Project(targetPt, SpatialReferences.Wgs84) as MapPoint ?? targetPt
                    : targetPt;

                double groundElev = 0.0;
                if (_ownerSceneView.Scene?.BaseSurface != null)
                {
                    try
                    {
                        var elev = await _ownerSceneView.Scene.BaseSurface.GetElevationAsync(wgs84);
                        if (!double.IsNaN(elev)) groundElev = elev;
                    }
                    catch { }
                }

                double altOffset = LastViewpoint.TargetScale > 0 ? Math.Clamp(LastViewpoint.TargetScale * 0.7, 1000.0, 150_000.0) : 15_000.0;
                double eyeAlt = groundElev + altOffset;
                var cam = new Camera(wgs84.Y, wgs84.X, eyeAlt, 0.0, 45.0, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(cam, TimeSpan.FromSeconds(1.0));
                return;
            }
        }

        // Vista regional inicial de Colombia en 3D
        await _ownerSceneView.SetViewpointCameraAsync(CamColombia3D, TimeSpan.FromSeconds(1.0));
    }

    private async Task CargarSlpk3DAsync(string path)
    {
        try
        {
            if (Scene == null) SetupScene();

            Layer slpkLayer;
            try
            {
                slpkLayer = new PointCloudLayer(new Uri(path));
                await slpkLayer.LoadAsync();
            }
            catch
            {
                slpkLayer = new ArcGISSceneLayer(new Uri(path));
                await slpkLayer.LoadAsync();
            }

            slpkLayer.Name = Path.GetFileNameWithoutExtension(path);
            Scene?.OperationalLayers.Add(slpkLayer);

            // Esperar brevemente a que el runtime calcule el FullExtent si aún no está disponible
            for (int i = 0; i < 10 && slpkLayer.FullExtent == null; i++)
            {
                await Task.Delay(100);
            }

            var extent = slpkLayer.FullExtent;
            double radio = CalcularRadioMetros(extent);
            double centroZ = 0.0;
            if (extent != null && extent.HasZ && !double.IsNaN(extent.ZMin) && (extent.ZMin != 0 || extent.ZMax != 0))
            {
                centroZ = (extent.ZMin + extent.ZMax) / 2.0;
            }
            else if (extent != null && Scene?.BaseSurface != null)
            {
                try
                {
                    var c = extent.GetCenter();
                    var wgs84Center = (c.SpatialReference != null && c.SpatialReference.Wkid != 4326)
                        ? GeometryEngine.Project(c, SpatialReferences.Wgs84) as MapPoint ?? c
                        : c;
                    var elev = await Scene.BaseSurface.GetElevationAsync(wgs84Center);
                    if (!double.IsNaN(elev)) centroZ = elev;
                }
                catch { }
            }

            var itemCapa = new CapaUsuarioItem
            {
                Nombre = Path.GetFileName(path),
                RutaCompleta = path,
                Capa = slpkLayer,
                TipoIcono = "☁️",
                TipoTexto = "Nube de Puntos 3D (SLPK)",
                ExtentParaZoom = extent,
                RadioMetros = radio,
                CentroZ = centroZ,
                InfoDetalle3D = $"Paquete de Escena 3D (.slpk)\nCapa: {slpkLayer.Name}"
            };
            itemCapa.QuitarCommand = new RelayCommand(() =>
            {
                Scene?.OperationalLayers.Remove(slpkLayer);
                CapasAdicionales.Remove(itemCapa);
                Capas3D.Remove(itemCapa);
                itemCapa.Dispose();
                SincronizarEstadoCapas3D();
            });
            itemCapa.ZoomCommand = new AsyncRelayCommand(async () => await ZoomACapa3DAsync(itemCapa));

            CapasAdicionales.Add(itemCapa);
            Capas3D.Add(itemCapa);
            Capa3DSeleccionada = itemCapa;
            SincronizarEstadoCapas3D();

            if (!IsModo3D)
            {
                IsModo3D = true;
                Modo3DTextoIcono = "🗺️ 2D";
            }

            if (extent != null)
            {
                await AsegurarSceneViewListoAsync();
                await ZoomACapa3DAsync(itemCapa);
            }

            _notifications?.ShowSuccess($"Nube de puntos 3D '{Path.GetFileName(path)}' cargada exitosamente.", "Visor 3D");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Error al cargar .slpk en 3D: {path}", ex);
            _notifications?.ShowError($"No se pudo cargar el archivo 3D: {ex.Message}", "Error Visor 3D");
        }
    }

    private (double Lon, double Lat, double Alt)? _anclajeLocalActual3D;

    private async Task<(double Lon, double Lat, double Alt)> ObtenerAnclajeLocal3DAsync()
    {
        if (_anclajeLocalActual3D.HasValue)
        {
            return _anclajeLocalActual3D.Value;
        }

        if (Scene == null)
        {
            SetupScene();
        }

        double lon = -73.1210; // Campus Principal UIS, Bucaramanga
        double lat = 7.1390;
        double alt = 960.0;

        if (Filtros?.SelectedProyecto is FiltrosViewModel.ProyectoItem p && (p.Lon != 0 || p.Lat != 0))
        {
            lon = p.Lon;
            lat = p.Lat;
        }
        else if (ArchivosVM?.ProyectoDetalle?.Proyecto != null && (ArchivosVM.ProyectoDetalle.Proyecto.Lon != 0 || ArchivosVM.ProyectoDetalle.Proyecto.Lat != 0))
        {
            lon = ArchivosVM.ProyectoDetalle.Proyecto.Lon;
            lat = ArchivosVM.ProyectoDetalle.Proyecto.Lat;
        }
        else if (LastViewpoint?.TargetGeometry is MapPoint vpPoint)
        {
            var wgs84Vp = (vpPoint.SpatialReference != null && vpPoint.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(vpPoint, SpatialReferences.Wgs84) as MapPoint ?? vpPoint
                : vpPoint;
            if (wgs84Vp != null && !double.IsNaN(wgs84Vp.X) && !double.IsNaN(wgs84Vp.Y) && wgs84Vp.X >= -180 && wgs84Vp.X <= 180)
            {
                lon = wgs84Vp.X;
                lat = wgs84Vp.Y;
            }
        }

        try
        {
            if (Scene?.BaseSurface != null)
            {
                if (Scene.BaseSurface.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                {
                    try { await Scene.BaseSurface.LoadAsync(); } catch { }
                }
                var testPt = new MapPoint(lon, lat, SpatialReferences.Wgs84);
                var elev = await Scene.BaseSurface.GetElevationAsync(testPt);
                if (!double.IsNaN(elev) && elev > -100.0)
                {
                    alt = elev;
                }
            }
        }
        catch { }

        _anclajeLocalActual3D = (lon, lat, alt);
        return _anclajeLocalActual3D.Value;
    }

    private async Task CargarNubePuntosLas3DAsync(string path)
    {
        IsOperacionEnProgreso = true;
        ProgresoPorcentaje = 0;
        ProgresoTitulo = $"Cargando nube LiDAR: {Path.GetFileName(path)}";
        ProgresoDetalle = "Iniciando procesamiento en segundo plano...";

        IProgress<(int porcentaje, string detalle)> progress = new Progress<(int porcentaje, string detalle)>(p =>
        {
            ProgresoPorcentaje = p.porcentaje;
            ProgresoDetalle = p.detalle;
        });

        try
        {
            _notifications?.ShowInfo($"Procesando nube de puntos LiDAR '{Path.GetFileName(path)}'...", "Cargando 3D");

            (double lon, double lat, double alt)? anclaje = null;
            try
            {
                if (Scene == null) SetupScene();
                anclaje = await ObtenerAnclajeLocal3DAsync();
            }
            catch { }

            // Procesar completamente en segundo plano (lectura, submuestreo, reproyección WGS84 y cálculo de huella)
            var result = await LidarBackgroundWorker.ProcesarNubeLidarAsync(path, anclaje, maxPointsToSample: 75_000, progress);

            if (result.SampledPointsCount == 0)
            {
                _notifications?.ShowWarning("El archivo LiDAR no contiene puntos legibles o requiere descompresión.", "Sin Puntos");
                return;
            }

            if (Scene == null) SetupScene();

            // 1. Crear GraphicsOverlays PROPIOS e independientes para esta capa específica
            var overlayGuia = new GraphicsOverlay
            {
                Id = $"Guia3D_{Guid.NewGuid():N}",
                SceneProperties = { SurfacePlacement = SurfacePlacement.DrapedFlat }
            };
            var overlayPuntos = new GraphicsOverlay
            {
                Id = $"NubePuntos3D_{Guid.NewGuid():N}",
                SceneProperties = { SurfacePlacement = SurfacePlacement.Absolute }
            };

            // 2. Huella y centro WGS84 ya calculados por el trabajador en segundo plano
            var lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(235, 255, 193, 7), 2.5);
            var fillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(40, 255, 193, 7), lineSymbol);
            overlayGuia.Graphics.Add(new Graphic(result.FootprintWgs84, fillSymbol));

            var pinSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.FromArgb(240, 220, 53, 69), 14.0);
            overlayGuia.Graphics.Add(new Graphic(result.CenterWgs84, pinSymbol));

            // 3. Agregar puntos precalculados a la capa gráfica
            var initialGraphics = new List<Graphic>(result.PuntosMuestreadosWgs84.Count);
            foreach (var item in result.PuntosMuestreadosWgs84)
            {
                var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, item.Color, result.TamanoPuntoRecomendado);
                initialGraphics.Add(new Graphic(item.PtWgs84, symbol));
            }
            overlayPuntos.Graphics.AddRange(initialGraphics);

            // Agregar los overlays al SceneView
            if (_ownerSceneView != null && _ownerSceneView.GraphicsOverlays != null)
            {
                _ownerSceneView.GraphicsOverlays.Add(overlayGuia);
                _ownerSceneView.GraphicsOverlays.Add(overlayPuntos);
            }

            var itemCapa = new CapaUsuarioItem
            {
                Nombre = result.NombreArchivo,
                RutaCompleta = path,
                Capa = null,
                TipoIcono = "☁️",
                TipoTexto = "Nube de Puntos (LAS/LAZ)",
                OverlayGuia3D = overlayGuia,
                OverlayPuntos3D = overlayPuntos,
                PuntosMuestreados3D = result.PuntosMuestreadosWgs84,
                CentroZ = result.CentroZWgs84,
                RadioMetros = result.RadioMetros,
                CrsNombre = result.CrsNombre,
                ExtentParaZoom = result.EnvelopeWgs84,
                InfoDetalle3D = result.InfoDetalle3D,
                OffsetZ3D = 0.0,
                TamanoPunto3D = result.TamanoPuntoRecomendado
            };

            itemCapa.QuitarCommand = new RelayCommand(() =>
            {
                if (_ownerSceneView?.GraphicsOverlays != null)
                {
                    _ownerSceneView.GraphicsOverlays.Remove(overlayGuia);
                    _ownerSceneView.GraphicsOverlays.Remove(overlayPuntos);
                }
                CapasAdicionales.Remove(itemCapa);
                Capas3D.Remove(itemCapa);
                itemCapa.Dispose();
                if (Capas3D.Count == 0)
                {
                    _anclajeLocalActual3D = null;
                }
                SincronizarEstadoCapas3D();
            });

            itemCapa.ZoomCommand = new AsyncRelayCommand(async () => await ZoomACapa3DAsync(itemCapa));

            CapasAdicionales.Add(itemCapa);
            Capas3D.Add(itemCapa);
            Capa3DSeleccionada = itemCapa;
            SincronizarEstadoCapas3D();

            if (!IsModo3D)
            {
                IsModo3D = true;
                Modo3DTextoIcono = "🗺️ 2D";
            }

            await AsegurarSceneViewListoAsync();
            await ZoomACapa3DAsync(itemCapa);

            _notifications?.ShowSuccess(
                $"Nube de puntos renderizada: {result.SampledPointsCount:N0} puntos ({result.CrsNombre}). Capas 3D activas: {Capas3D.Count}.",
                "Visor 3D");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Error al leer nube de puntos LAS/LAZ '{path}'", ex);
            _notifications?.ShowError($"Error al cargar archivo LiDAR: {ex.Message}", "Error LAS 3D");
        }
        finally
        {
            IsOperacionEnProgreso = false;
            ProgresoPorcentaje = 0;
            ProgresoTitulo = "";
            ProgresoDetalle = "";
        }
    }
}
