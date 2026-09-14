using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Rasters;
using Geomatica.Data.Repositories;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.UI.Controls;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.UI;
using Geomatica.Desktop.Models;
using Geomatica.Desktop.Services;

namespace Geomatica.Desktop.ViewModels
{
    public partial class CapaUsuarioItem : ObservableObject
    {
        public string Nombre { get; set; } = "";
        public string RutaCompleta { get; set; } = "";
        public string TipoIcono { get; set; } = "🗺️";
        public string TipoTexto { get; set; } = "Capa Ráster";
        public Layer? Capa { get; set; }
        public Envelope? ExtentParaZoom { get; set; }

        // Elementos y propiedades 3D propios de esta capa (para nubes LAS/LAZ y SLPK)
        public GraphicsOverlay? OverlayPuntos3D { get; set; }
        public GraphicsOverlay? OverlayGuia3D { get; set; }
        public List<(MapPoint PtWgs84, System.Drawing.Color Color)>? PuntosMuestreados3D { get; set; }
        public double CentroZ { get; set; }
        public double RadioMetros { get; set; } = 150.0;
        public string CrsNombre { get; set; } = "";
        public string InfoDetalle3D { get; set; } = "";

        [ObservableProperty] private double offsetZ3D = 0.0;
        [ObservableProperty] private double tamanoPunto3D = 4.5;

        [ObservableProperty]
        private bool isVisible = true;

        public Action<bool>? OnVisibilityChangedAction { get; set; }
        public Action<double>? OnOpacityChangedAction { get; set; }

        partial void OnIsVisibleChanged(bool value)
        {
            if (Capa != null)
            {
                Capa.IsVisible = value;
            }
            if (OverlayPuntos3D != null)
            {
                OverlayPuntos3D.IsVisible = value;
            }
            if (OverlayGuia3D != null)
            {
                OverlayGuia3D.IsVisible = value;
            }
            OnVisibilityChangedAction?.Invoke(value);
        }

        [ObservableProperty]
        private double opacidad = 1.0;

        partial void OnOpacidadChanged(double value)
        {
            if (Capa != null)
            {
                Capa.Opacity = value;
            }
            if (OverlayPuntos3D != null)
            {
                OverlayPuntos3D.Opacity = value;
            }
            if (OverlayGuia3D != null)
            {
                OverlayGuia3D.Opacity = value;
            }
            OnOpacityChangedAction?.Invoke(value);
        }

        public void ReconstruirPuntos()
        {
            if (OverlayPuntos3D == null || PuntosMuestreados3D == null || PuntosMuestreados3D.Count == 0) return;

            OverlayPuntos3D.Graphics.Clear();
            var graphics = new List<Graphic>(PuntosMuestreados3D.Count);
            double tamano = TamanoPunto3D;
            double offset = OffsetZ3D;

            foreach (var item in PuntosMuestreados3D)
            {
                var basePt = item.PtWgs84;
                var ptConOffset = (offset != 0.0)
                    ? new MapPoint(basePt.X, basePt.Y, basePt.Z + offset, SpatialReferences.Wgs84)
                    : basePt;

                var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, item.Color, tamano);
                graphics.Add(new Graphic(ptConOffset, symbol));
            }

            OverlayPuntos3D.Graphics.AddRange(graphics);
        }

        public IRelayCommand? QuitarCommand { get; set; }
        public IRelayCommand? ZoomCommand { get; set; }
    }

    public partial class MapaViewModel : ObservableObject
    {
        private readonly IProyectoRepository _proyectos;
        private readonly BuscarProyectosUseCase _buscarProyectos;
        private readonly IMunicipioRepository _municipios;

        // Referencia opcional a los filtros compartidos
        public FiltrosViewModel Filtros { get; }
        public ArchivosViewModel ArchivosVM { get; }
        private Layer? _layerMunicipios;
        private Layer? _layerProyectos;
        public Layer? LayerProyectos => _layerProyectos;
        private IReadOnlyList<MunicipioGeoJsonDto>? _cachedMunicipios;
        private Dictionary<string, Geometry>? _cachedGeometries;
        private IReadOnlyList<string>? _ultimosCodigosMunicipio;
        private Dictionary<long, int> _oidToProjectId = new();
        private int _updateGeneration;

        // Track the MapView that currently displays this Map to release ownership when re-attaching
        private MapView? _ownerMapView;
        private readonly Dictionary<Layer, Envelope> _rasterExtentsSeguros = new();

        public ObservableCollection<CapaUsuarioItem> CapasAdicionales { get; } = new();
        [ObservableProperty] private bool isPanelCapasVisible;

        public string? UltimosSidecarsRaster { get; private set; }

        // Gestión de Mapas Base (Online y Offline)
        private readonly BasemapService _basemapService = new();
        public ObservableCollection<BasemapOption> MapasBase { get; } = new();
        [ObservableProperty] private BasemapOption? mapaBaseSeleccionado;
        [ObservableProperty] private bool isSelectorMapasBaseVisible;
        [ObservableProperty] private bool isModoOfflineForzado;
        [ObservableProperty] private bool isSinConexionInternet;
        [ObservableProperty] private string avisoSinConexionTexto = "Sin conexión a internet: funcionando en modo offline con el mapa base predeterminado.";

        // Herramientas de Medición SIG
        public GraphicsOverlay OverlayMedicion { get; } = new() { Id = "OverlayMedicion" };
        private readonly List<MapPoint> _puntosMedicion = new();
        [ObservableProperty] private bool isHerramientasMedicionVisible;
        [ObservableProperty] private string modoMedicion = "Ninguno"; // "Ninguno", "Distancia", "Area"
        [ObservableProperty] private string resultadoMedicion = "";
        [ObservableProperty] private string detalleMedicion = "";
        [ObservableProperty] private bool hasResultadoMedicion;

        // Visualización de Coordenadas y Escala en Vivo
        [ObservableProperty] private string coordenadasCursorTexto = "Lat: -- | Lon: --";
        [ObservableProperty] private string escalaMapaTexto = "Escala: 1:--";

        // Visor 3D y Nubes de Puntos
        [ObservableProperty] private bool isModo3D;
        [ObservableProperty] private Scene? scene;
        [ObservableProperty] private string modo3DTextoIcono = "🌐 3D";
        [ObservableProperty] private bool hasCapa3DActiva;
        [ObservableProperty] private bool hasMultiplesCapas3D;
        [ObservableProperty] private string infoCapa3DTexto = "";
        [ObservableProperty] private string tituloCapa3D = "";

        public ObservableCollection<CapaUsuarioItem> Capas3D { get; } = new();
        [ObservableProperty] private CapaUsuarioItem? capa3DSeleccionada;

        partial void OnCapa3DSeleccionadaChanged(CapaUsuarioItem? value)
        {
            if (value != null)
            {
                HasCapa3DActiva = true;
                TituloCapa3D = value.Nombre;
                InfoCapa3DTexto = value.InfoDetalle3D;
                CrsDetectado3D = value.CrsNombre;
                OffsetZ3D = value.OffsetZ3D;
                TamanoPunto3D = value.TamanoPunto3D;
                UltimoExtent3D = value.ExtentParaZoom;
                UltimoCentroZ3D = value.CentroZ;
                UltimoRadioMetros3D = value.RadioMetros;
            }
            else
            {
                HasCapa3DActiva = Capas3D.Count > 0;
                if (!HasCapa3DActiva)
                {
                    TituloCapa3D = "";
                    InfoCapa3DTexto = "";
                    CrsDetectado3D = "";
                    UltimoExtent3D = null;
                }
            }
        }

        public void SincronizarEstadoCapas3D()
        {
            HasMultiplesCapas3D = Capas3D.Count > 1;
            HasCapa3DActiva = Capas3D.Count > 0;
            if (Capa3DSeleccionada == null || !Capas3D.Contains(Capa3DSeleccionada))
            {
                Capa3DSeleccionada = Capas3D.LastOrDefault();
            }
        }

        [ObservableProperty] private string crsDetectado3D = "";
        [ObservableProperty] private double offsetZ3D = 0.0;
        [ObservableProperty] private double tamanoPunto3D = 4.5;
        public double UltimoCentroZ3D { get; private set; } = 0.0;
        public double UltimoRadioMetros3D { get; private set; } = 150.0;

        private SceneView? _ownerSceneView;
        public Envelope? UltimoExtent3D { get; private set; }

        // Comando y evento para Home (MVVM)
        public IRelayCommand HomeCommand { get; }
        public IAsyncRelayCommand RestablecerVistaMapaCommand { get; }
        public event EventHandler? HomeRequested;
        public event EventHandler<ProyectoDetalleDto>? FichaProyectoSolicitada;

        // Inyección de notificaciones
        private readonly Geomatica.Desktop.Services.INotificationService? _notifications;

        public MapaViewModel(
            BuscarProyectosUseCase buscarProyectos, 
            IProyectoRepository proyectos, 
            IMunicipioRepository municipios, 
            FiltrosViewModel filtros, 
            ArchivosViewModel archivosVM,
            Geomatica.Desktop.Services.INotificationService? notifications = null)
        {
            _buscarProyectos = buscarProyectos;
            _proyectos = proyectos;
            _municipios = municipios;
            Filtros = filtros;
            ArchivosVM = archivosVM;
            _notifications = notifications;

            HomeCommand = new RelayCommand(() => HomeRequested?.Invoke(this, EventArgs.Empty));
            RestablecerVistaMapaCommand = new AsyncRelayCommand(RestablecerVistaMapaAsync);

            // Cargar lista de mapas base
            foreach (var b in _basemapService.ObtenerMapasBaseDisponibles())
            {
                MapasBase.Add(b);
            }
            MapaBaseSeleccionado = MapasBase.FirstOrDefault();
            if (MapaBaseSeleccionado != null)
            {
                MapaBaseSeleccionado.IsSeleccionado = true;
            }

            ArchivosVM.AbrirEnMapaSolicitado += async (s, path) => await CargarCapaAdicionalAsync(path);
            Filtros.PropertyChanged += Filtros_PropertyChanged;

            SetupMap();
            SetupScene();

            // Comprobación inicial de conectividad a internet en segundo plano
            _ = Task.Run(() =>
            {
                var conectado = BasemapService.ComprobarConexionInternet();
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    if (!conectado)
                    {
                        IsSinConexionInternet = true;
                        IsModoOfflineForzado = true;
                        ActualizarDisponibilidadMapasBase();
                        var defaultOption = MapasBase.FirstOrDefault(b => b.IsDefaultOffline);
                        if (defaultOption != null)
                        {
                            await AplicarBasemapAsync(defaultOption, true);
                        }
                        _notifications?.ShowWarning(
                            "Se inició la aplicación sin conexión a internet. Los servicios en línea no están disponibles; se utilizará el mapa base Topográfico local predeterminado.", 
                            "Modo Sin Conexión");
                    }
                    else
                    {
                        IsSinConexionInternet = false;
                        ActualizarDisponibilidadMapasBase();
                    }
                });
            });
        }

        private async void Filtros_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FiltrosViewModel.SelectedProyecto))
            {
                if (Filtros.SelectedProyecto != null)
                {
                    await AbrirFichaProyectoAsync(Filtros.SelectedProyecto.Id);
                }
            }
        }

        private async Task CargarCapaAdicionalAsync(string path)
        {
            if (Map == null) return;
            try
            {
                RasterDiagnostics.Log($"Loading user layer path={path}");
                RasterDiagnostics.LogDispatcher("MapaViewModel.CargarCapaAdicionalAsync");
                RasterDiagnostics.LogFile(path);
                Layer? layer = null;
                var ext = Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".shp")
                {
                    var shapefile = await ShapefileFeatureTable.OpenAsync(path);
                    layer = new FeatureLayer(shapefile);
                }
                else if (ext == ".kml" || ext == ".kmz")
                {
                    var dataset = new Esri.ArcGISRuntime.Ogc.KmlDataset(new Uri(path));
                    layer = new KmlLayer(dataset);
                }
                else if (ext == ".geodatabase" || ext == ".gdb")
                {
                    var gdb = await Geodatabase.OpenAsync(path);
                    var table = gdb.GeodatabaseFeatureTables.FirstOrDefault();
                    if (table != null) layer = new FeatureLayer(table);
                }
                else if (ext == ".slpk")
                {
                    await CargarSlpk3DAsync(path);
                    return;
                }
                else if (ext == ".las" || ext == ".laz" || ext == ".zlas")
                {
                    await CargarNubePuntosLas3DAsync(path);
                    return;
                }
                else if (ext == ".tif" || ext == ".tiff")
                {
                    layer = await CrearRasterLayerValidadoAsync(path);
                    if (layer == null) return;
                }
        if (layer != null)
        {
            layer.ShowInLegend = false;
            if (layer is RasterLayer rasterLayer)
            {
                rasterLayer.IsVisible = true;
                rasterLayer.Opacity = 1.0;
            }
            layer.LoadStatusChanged += async (s, e) =>
            {
                RasterDiagnostics.LogArcGisLayerError("Layer.LoadStatusChanged", layer.Name, e.Status.ToString(), layer.LoadError);
                if (e.Status == Esri.ArcGISRuntime.LoadStatus.Loaded && _ownerMapView != null)
                {
                    // Darle un instante al MapView para que active la capa antes de hacer zoom
                    await Task.Delay(250);
                    await ZoomCapaSeguraAsync(layer, 20, "carga inicial");
                }
                else if (e.Status == Esri.ArcGISRuntime.LoadStatus.FailedToLoad) 
                {
                    Application.Current.Dispatcher.Invoke(() => 
                    {
                        var err = layer.LoadError?.Message ?? "Error desconocido.";
                        MessageBox.Show($"La capa falló al renderizarse en el mapa.\nDetalle: {err}", "Error de Capa", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            };

            RasterDiagnostics.Log($"Adding layer to map: name={layer.Name}; type={layer.GetType().FullName}; loadStatus={layer.LoadStatus}; extent={layer.FullExtent}");
            await Application.Current.Dispatcher.InvokeAsync(() => Map.OperationalLayers.Add(layer));

            if (layer.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                await layer.LoadAsync();

            // Esperar a que el MapView active la capa antes de centrar
            if (layer.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded && _ownerMapView != null)
            {
                await Task.Delay(250);
                await ZoomCapaSeguraAsync(layer, 20, "post-add");
            }

            var tipoIcono = ext switch
            {
                ".tif" or ".tiff" => "🗺️",
                ".shp" => "📐",
                ".kml" or ".kmz" => "📍",
                ".geojson" or ".json" => "🌐",
                _ => "📁"
            };
            var tipoTexto = ext switch
            {
                ".tif" or ".tiff" => "Ráster GeoTIFF",
                ".shp" => "Vectorial Shapefile",
                ".kml" or ".kmz" => "Archivo KML/KMZ",
                ".geojson" or ".json" => "GeoJSON",
                _ => "Capa de Datos"
            };

            var item = new CapaUsuarioItem
            {
                Nombre = Path.GetFileName(path),
                RutaCompleta = path,
                TipoIcono = tipoIcono,
                TipoTexto = tipoTexto,
                Capa = layer,
                ExtentParaZoom = _rasterExtentsSeguros.GetValueOrDefault(layer),
                IsVisible = true,
                Opacidad = 1.0
            };
            item.QuitarCommand = new RelayCommand(() => 
            {
                if (item.Capa != null)
                {
                    Map.OperationalLayers.Remove(item.Capa);
                    _rasterExtentsSeguros.Remove(item.Capa);
                }
                CapasAdicionales.Remove(item);
                if (CapasAdicionales.Count == 0)
                {
                    IsPanelCapasVisible = false;
                }
            });
            
            item.ZoomCommand = new RelayCommand(async () =>
            {
                if (item.Capa != null)
                    await ZoomCapaSeguraAsync(item.Capa, 50, "manual");
            });

            Application.Current.Dispatcher.Invoke(() => 
            {
                CapasAdicionales.Add(item);
                IsPanelCapasVisible = true;
                IsSelectorMapasBaseVisible = false;
                IsHerramientasMedicionVisible = false;
                _notifications?.ShowSuccess($"Capa '{item.Nombre}' añadida al mapa.", "Capa Añadida");
            });
        }
        else
        {
            _notifications?.ShowWarning("El formato de archivo no se puede mostrar en el mapa.", "Formato no compatible");
        }
    }
    catch (Exception ex)
    {
        RasterDiagnostics.LogException("Unexpected user layer load error", ex);
        _notifications?.ShowError($"Error al cargar en mapa: {ex.Message}", "Error al Cargar Capa");
    }
 }

    private async Task<Layer?> CrearRasterLayerValidadoAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                MessageBox.Show("El archivo raster no existe en la ruta indicada.", "Aviso de Raster", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            var fileInfo = new FileInfo(path);
            var sidecars = BuscarSidecarsRaster(path);
            UltimosSidecarsRaster = sidecars.Count == 0
                ? null
                : string.Join(", ", sidecars.Select(Path.GetFileName));
            RasterDiagnostics.LogPix4DProduct(path, "orthomosaic", sidecars);
            RasterDiagnostics.Log($"TIFF selected path={path}; sidecars={string.Join(", ", sidecars.Select(s => Path.GetFileName(s)))}");

            // 1. Intentar carga directa del raster desde su ruta original
            // Los GeoTIFFs transparentes de Pix4D con CRS embebido cargan de inmediato sin necesidad de sidecars
            Raster? raster = null;
            string rasterPath = path;
            bool isDirectLoad = true;

            try
            {
                var directRaster = new Raster(path);
                await directRaster.LoadAsync();
                if (directRaster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded && directRaster.RasterInfo?.SpatialReference != null)
                {
                    raster = directRaster;
                    RasterDiagnostics.Log($"Direct Raster load succeeded with SpatialReference={raster.RasterInfo.SpatialReference}");
                }
                else
                {
                    RasterDiagnostics.Log($"Direct Raster load did not obtain SpatialReference (LoadStatus={directRaster.LoadStatus}).");
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.LogException("Direct Raster.LoadAsync exception", ex);
            }

            // 2. Si la carga directa no obtuvo SpatialReference, intentar el fallback con sidecars (.prj + .tfw)
            if (raster == null)
            {
                RasterDiagnostics.Log($"Attempting sidecar resolution for: {path}");
                
                try
                {
                    await GeoTiffSidecarResolver.AsegurarAuxXmlGeorreferenciadoAsync(path);
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.LogException("GeoTIFF aux.xml synchronization failed", ex);
                }

                var cacheRasterPath = GeoTiffSidecarResolver.ObtenerRutaRasterCache(path);
                if (File.Exists(cacheRasterPath))
                {
                    try
                    {
                        var fallbackRaster = new Raster(cacheRasterPath);
                        await fallbackRaster.LoadAsync();
                        if (fallbackRaster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded)
                        {
                            raster = fallbackRaster;
                            rasterPath = cacheRasterPath;
                            isDirectLoad = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        RasterDiagnostics.LogException("Fallback Raster.LoadAsync exception", ex);
                    }
                }
            }

            // Si ambos fallaron, intentar una última carga directa para capturar el error de ArcGIS
            if (raster == null)
            {
                try
                {
                    raster = new Raster(path);
                    await raster.LoadAsync();
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.LogException("Final Raster.LoadAsync exception", ex);
                }
            }

            if (raster == null || raster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.FailedToLoad)
            {
                RasterDiagnostics.LogException("Raster.LoadAsync failed", raster?.LoadError);
                MessageBox.Show(
                    $"No se pudo cargar el ortomosaico.\n\n" +
                    $"Ruta: {path}\n" +
                    $"Detalle: {ObtenerMensajeErrorArcGis(raster?.LoadError)}\n\n" +
                    "Consejo para Pix4D: exporta el ortomosaico como GeoTIFF con el sistema de coordenadas incrustado (por ejemplo EPSG:4326 si trabajas en WGS84, o el CRS proyectado del proyecto). " +
                    "Asegúrate de que el archivo no esté bloqueado por otro proceso y que tenga las pirámides (overviews) generadas.",
                    "Error de ortomosaico",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }

            var rasterInfo = raster.RasterInfo;
            if (rasterInfo == null)
            {
                RasterDiagnostics.Log($"RasterInfo is null path={path}");
                MessageBox.Show(
                    "No se pudo leer la información del ortomosaico.\n\n" +
                    "Verifica que el GeoTIFF generado por Pix4D no esté corrupto y que incluya metadatos de georreferenciación.",
                    "Aviso de ortomosaico",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            // Diagnóstico técnico del ortomosaico
            try
            {
                RasterDiagnostics.LogRasterInfo(
                    path,
                    Path.GetFileName(path),
                    rasterInfo.Extent?.ToString(),
                    rasterInfo.SpatialReference?.ToString());
            }
            catch (Exception ex)
            {
                RasterDiagnostics.LogException("RasterInfo diagnostics failed", ex);
            }

            var rasterLayer = new RasterLayer(raster)
            {
                Name = Path.GetFileName(path),
                IsVisible = true,
                Opacity = 1.0,
                ShowInLegend = false
            };
            rasterLayer.LoadStatusChanged += (s, e) =>
            {
                RasterDiagnostics.LogArcGisLayerError("RasterLayer.LoadStatusChanged", rasterLayer.Name, e.Status.ToString(), rasterLayer.LoadError);
            };

            await rasterLayer.LoadAsync();
            RasterDiagnostics.LogRasterMetadata(
                path,
                fileInfo.Length,
                raster.LoadStatus.ToString(),
                rasterLayer.LoadStatus.ToString(),
                rasterInfo.Extent?.ToString(),
                rasterLayer.FullExtent?.ToString(),
                rasterInfo.SpatialReference?.ToString(),
                rasterLayer.SpatialReference?.ToString(),
                FormatearSpatialReferenceId(rasterInfo.SpatialReference),
                FormatearSpatialReferenceId(rasterLayer.SpatialReference));

            if (rasterLayer.LoadStatus == Esri.ArcGISRuntime.LoadStatus.FailedToLoad)
            {
                RasterDiagnostics.LogException("RasterLayer.LoadAsync failed", rasterLayer.LoadError);
                MessageBox.Show(
                    $"ArcGIS Runtime cargó el TIFF, pero no pudo crear la capa raster.\n\nRuta: {path}\nDetalle: {ObtenerMensajeErrorArcGis(rasterLayer.LoadError)}",
                    "Error de Raster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }

            // Determinar Envelope y SpatialReference válidos
            Envelope? extentSeguro = null;
            SpatialReference? spatialReferenceSegura = null;

            if (isDirectLoad && (rasterLayer.SpatialReference != null || rasterInfo.SpatialReference != null))
            {
                extentSeguro = rasterLayer.FullExtent ?? rasterInfo.Extent;
                spatialReferenceSegura = rasterLayer.SpatialReference ?? rasterInfo.SpatialReference;
            }
            else
            {
                // Fallback con sidecars
                GeoTiffSidecarResolution sidecar;
                try
                {
                    sidecar = GeoTiffSidecarResolver.Resolve(rasterPath, rasterInfo);
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.LogException("GeoTIFF sidecar resolution failed", ex);
                    sidecar = new(null, null, ex.Message);
                }

                if (sidecar.IsValid)
                {
                    extentSeguro = sidecar.Envelope;
                    spatialReferenceSegura = sidecar.SpatialReference;
                }
                else
                {
                    extentSeguro = rasterLayer.FullExtent ?? rasterInfo.Extent;
                    spatialReferenceSegura = rasterLayer.SpatialReference ?? rasterInfo.SpatialReference;
                }
            }

            var validationError = ValidarRasterGeorreferenciado(path, rasterInfo, rasterLayer, sidecars);
            if (validationError != null && (spatialReferenceSegura == null || extentSeguro == null || !EsEnvelopeFinito(extentSeguro)))
            {
                RasterDiagnostics.Log($"Raster rejected path={path}; reason={validationError.Replace(Environment.NewLine, " | ")}");
                MessageBox.Show(
                    $"El TIFF no tiene georreferenciación utilizable.\n\nRuta: {path}\nDetalle: {validationError}\n\nNo se agregará la capa ni se modificará la vista del mapa.",
                    "Raster sin georreferenciación válida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            if (extentSeguro == null || spatialReferenceSegura == null || !EsEnvelopeFinito(extentSeguro))
            {
                MessageBox.Show(
                    "El TIFF no tiene un extent geográfico válido; la carga fue cancelada para proteger la vista del mapa.",
                    "Raster sin georreferenciación válida",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            _rasterExtentsSeguros[rasterLayer] = extentSeguro;

            if (fileInfo.Length > 700_000_000)
            {
                MessageBox.Show(
                    "El ortomosaico se cargó correctamente, pero el archivo es grande. " +
                    "Si el paneo o zoom se siente lento, genera pirámides internas (overviews) en Pix4D o convierte el GeoTIFF a COG (Cloud Optimized GeoTIFF).",
                    "Ortomosaico grande",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return rasterLayer;
        }
        catch (Exception ex)
        {
            RasterDiagnostics.LogException("Exception loading TIFF raster", ex);
            MessageBox.Show(
                $"Error al leer el archivo TIFF.\n\nRuta: {path}\nDetalle: {ex.Message}",
                "Error de Raster",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }
    }

  private static string? ValidarRasterGeorreferenciado(string path, RasterInfo rasterInfo, RasterLayer rasterLayer, IReadOnlyList<string> sidecars)
  {
    var extent = rasterLayer.FullExtent ?? rasterInfo.Extent;
    if (extent == null)
        return $"ArcGIS Runtime cargó el TIFF, pero la capa no reporta un extent espacial.\n\nRuta: {path}";

    if (!EsEnvelopeFinito(extent))
        return $"ArcGIS Runtime cargó el TIFF, pero el extent contiene coordenadas inválidas.\n\nRuta: {path}\nExtent: {extent}";

    var sr = rasterLayer.SpatialReference ?? rasterInfo.SpatialReference;
    if (sr == null)
    {
        var sidecarText = sidecars.Count == 0
            ? "No se encontraron archivos auxiliares .tfw/.prj/.aux.xml junto al TIFF."
            : $"Sidecars detectados: {string.Join(", ", sidecars.Select(Path.GetFileName))}. ArcGIS Runtime no los aplicó al raster.";

        return
            "El ortomosaico de Pix4D no tiene sistema de coordenadas reconocido por ArcGIS Runtime. La app no lo superpondrá al mapa porque su extent queda en coordenadas de pixel.\n\n" +
            $"Ruta: {path}\n" +
            $"Extent leído: {extent}\n" +
            $"{sidecarText}\n\n" +
            "Corrección en Pix4D: en el paso 'Exportar', elige el sistema de coordenadas del proyecto y marca la opción para incrustar el CRS en el GeoTIFF (no solo archivos .tfw/.prj aparte). " +
            "Si usas MAGNA Colombia Bogotá (EPSG:3116), prueba exportar también una copia reproyectada a EPSG:4326; ArcGIS Runtime WPF la reconoce de forma más confiable. " +
            "Para mosaicos grandes, genera pirámides/overviews internas o publica un ImageServer.";
    }

    return null;
  }

  private static bool EsEnvelopeFinito(Envelope extent)
    => double.IsFinite(extent.XMin)
       && double.IsFinite(extent.YMin)
       && double.IsFinite(extent.XMax)
       && double.IsFinite(extent.YMax)
       && extent.XMax > extent.XMin
       && extent.YMax > extent.YMin;

  private static IReadOnlyList<string> BuscarSidecarsRaster(string path)
  {
    var dir = Path.GetDirectoryName(path);
    var name = Path.GetFileNameWithoutExtension(path);
    if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();

    var candidates = new[]
    {
        Path.Combine(dir, name + ".tfw"),
        Path.Combine(dir, name + ".tifw"),
        Path.Combine(dir, name + ".wld"),
        Path.Combine(dir, name + ".prj"),
        path + ".aux.xml",
        path + ".ovr",
        path + ".rrd",
        Path.Combine(dir, name + ".tif.pox"),
        Path.Combine(dir, name + ".tif.points")
    };
    return candidates.Where(File.Exists).ToArray();
  }

  private static string? FormatearSpatialReferenceId(SpatialReference? sr)
  {
     if (sr == null) return null;
     if (sr.Wkid > 0) return $"WKID:{sr.Wkid}";
     return null;
  }

 private static string ObtenerMensajeErrorArcGis(Exception? error)
 {
    var messages = new List<string>();
    for (var current = error; current != null; current = current.InnerException)
        messages.Add(current.Message);
    return string.Join(" | ", messages);
 }

 private async Task ZoomCapaSeguraAsync(Layer layer, double padding, string origen)
 {
    if (_rasterExtentsSeguros.TryGetValue(layer, out var extent))
    {
        await ZoomEnvelopeAsync(extent, padding, origen);
        return;
    }

    await ZoomCapaAsync(layer, padding, origen);
 }

 private async Task ZoomEnvelopeAsync(Envelope extent, double padding, string origen)
 {
    if (_ownerMapView == null || !EsEnvelopeFinito(extent) || extent.SpatialReference == null)
        return;

    try
    {
        var targetSpatialReference = _ownerMapView.SpatialReference ?? Map?.SpatialReference;
        var extentParaVista = targetSpatialReference == null || extent.SpatialReference.Wkid == targetSpatialReference.Wkid
            ? extent
            : GeometryEngine.Project(extent, targetSpatialReference) as Envelope;

        if (extentParaVista == null || !EsEnvelopeFinito(extentParaVista))
            throw new InvalidOperationException("No se pudo proyectar el extent del raster al sistema de referencia del mapa.");

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            RasterDiagnostics.Log($"Zoom sidecar extent origin={origen}; extent={extentParaVista}");
            await _ownerMapView.SetViewpointGeometryAsync(extentParaVista, padding);
        });
    }
    catch (Exception ex)
    {
        RasterDiagnostics.LogException($"Zoom sidecar extent failed origin={origen}", ex);
        MessageBox.Show($"No se pudo centrar el raster georreferenciado.\n\nDetalle: {ex.Message}", "Zoom a Raster", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
 }

 public async Task RestablecerVistaMapaAsync()
 {
    var colombia = new Envelope(-79.0, -4.5, -66.8, 12.5, SpatialReferences.Wgs84);
    await ZoomEnvelopeAsync(colombia, 40, "restablecer Colombia");
 }

 private async Task ZoomCapaAsync(Layer layer, double padding, string origen)
 {
     if (_ownerMapView == null)
     {
         _notifications?.ShowInfo("La vista de mapa todavía no está lista para centrar la capa.", "Zoom a Capa");
         return;
     }

     var extent = layer.FullExtent;
     if (extent == null || extent.SpatialReference == null || !EsEnvelopeFinito(extent))
     {
         RasterDiagnostics.Log($"Zoom rejected origin={origen}; layer={layer.Name}; extent={extent}");
         _notifications?.ShowWarning("La capa no tiene información espacial válida para hacer zoom.", "Zoom a Capa");
         return;
     }

     try
     {
         await Application.Current.Dispatcher.InvokeAsync(async () =>
         {
             RasterDiagnostics.Log($"Zoom layer origin={origen}; layer={layer.Name}; extent={extent}");
             await _ownerMapView.SetViewpointGeometryAsync(extent, padding);
         });
     }
     catch (Exception ex)
     {
         RasterDiagnostics.LogException($"Zoom layer failed origin={origen}; layer={layer.Name}", ex);
         _notifications?.ShowWarning($"No se pudo centrar la capa: {ex.Message}", "Zoom a Capa");
     }
 }

    private bool _isOpeningFicha = false;

    public async Task AbrirFichaProyectoAsync(int idProyecto)
    {
        if (_isOpeningFicha) return;
        _isOpeningFicha = true;
        try
        {
            var detalle = await _proyectos.ObtenerPorIdAsync(idProyecto);
            if (detalle != null)
            {
                if (Filtros != null && Filtros.SelectedProyecto?.Id != detalle.Id)
                {
                    Filtros.SelectedProyecto = new FiltrosViewModel.ProyectoItem(
                        detalle.Id, detalle.Titulo, detalle.Lon, detalle.Lat, detalle.RutaArchivos);
                }
                FichaProyectoSolicitada?.Invoke(this, detalle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error cargando detalle de proyecto {idProyecto}: {ex}");
        }
        finally
        {
            _isOpeningFicha = false;
        }
    }

    // Methods to attach/detach a MapView safely
    public void AttachMapView(MapView mv)
    {
        try
        {
            if (Application.Current == null)
            {
                DoAttach(mv);
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                DoAttach(mv);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => DoAttach(mv));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error en AttachMapView: {ex}");
        }
    }

    private void DoAttach(MapView mv)
    {
        if (_ownerMapView == mv) return;
        if (_ownerMapView != null)
        {
            try { _ownerMapView.Map = null; } catch { }
        }
        _ownerMapView = mv;
        if (_ownerMapView != null)
        {
            if (_ownerMapView.Map != Map)
            {
                _ownerMapView.Map = Map;
            }
            if (_ownerMapView.GraphicsOverlays != null && !_ownerMapView.GraphicsOverlays.Contains(OverlayMedicion))
            {
                _ownerMapView.GraphicsOverlays.Add(OverlayMedicion);
            }
        }
    }

    public void DetachMapView(MapView mv)
    {
        try
        {
            if (Application.Current == null)
            {
                DoDetach(mv);
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                DoDetach(mv);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => DoDetach(mv));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error en DetachMapView: {ex}");
        }
    }

    private void DoDetach(MapView mv)
    {
        if (_ownerMapView == mv && mv != null)
        {
            try
            {
                try
                {
                    var vp = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                    if (vp != null)
                    {
                        LastViewpoint = vp;
                    }
                }
                catch { }

                try 
                { 
                    if (mv.GraphicsOverlays != null && mv.GraphicsOverlays.Contains(OverlayMedicion))
                    {
                        mv.GraphicsOverlays.Remove(OverlayMedicion);
                    }
                    _ownerMapView.Map = null; 
                } 
                catch { }
                _ownerMapView = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error en DoDetach: {ex}");
            }
        }
    }

    // ==========================================
    // SECCIÓN: VISOR 3D Y NUBES DE PUNTOS
    // ==========================================

    private void SetupScene()
    {
        var basemapStyle = MapaBaseSeleccionado?.Style ?? BasemapStyle.ArcGISTopographic;
        var newScene = new Scene(basemapStyle);

        try
        {
            // Superficie de elevación mundial 3D
            var elevationSource = new ArcGISTiledElevationSource(new Uri("https://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer"));
            newScene.BaseSurface.ElevationSources.Add(elevationSource);
            newScene.BaseSurface.IsEnabled = true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"No se pudo inicializar la fuente de elevación 3D: {ex.Message}");
        }

        Scene = newScene;
        if (_ownerSceneView != null)
        {
            _ownerSceneView.Scene = newScene;
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
        }
    }

    public void AttachSceneView(SceneView sv)
    {
        try
        {
            if (Application.Current == null)
            {
                DoAttachScene(sv);
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                DoAttachScene(sv);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => DoAttachScene(sv));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error al adjuntar SceneView", ex);
        }
    }

    private void DoAttachScene(SceneView sv)
    {
        if (_ownerSceneView == sv) return;
        if (_ownerSceneView != null)
        {
            try { _ownerSceneView.Scene = null; } catch { }
        }
        _ownerSceneView = sv;
        if (_ownerSceneView != null)
        {
            if (_ownerSceneView.Scene != Scene)
            {
                _ownerSceneView.Scene = Scene;
            }
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
        }
    }

    public void DetachSceneView(SceneView sv)
    {
        try
        {
            if (Application.Current == null)
            {
                DoDetachScene(sv);
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                DoDetachScene(sv);
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => DoDetachScene(sv));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Error al desadjuntar SceneView", ex);
        }
    }

    private void DoDetachScene(SceneView sv)
    {
        if (_ownerSceneView == sv && sv != null)
        {
            try
            {
                if (sv.GraphicsOverlays != null)
                {
                    foreach (var capa in Capas3D)
                    {
                        if (capa.OverlayGuia3D != null && sv.GraphicsOverlays.Contains(capa.OverlayGuia3D))
                            sv.GraphicsOverlays.Remove(capa.OverlayGuia3D);
                        if (capa.OverlayPuntos3D != null && sv.GraphicsOverlays.Contains(capa.OverlayPuntos3D))
                            sv.GraphicsOverlays.Remove(capa.OverlayPuntos3D);
                    }
                }
                _ownerSceneView.Scene = null;
                _ownerSceneView = null;
            }
            catch { }
        }
    }

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
            if (HasCapa3DActiva && UltimoExtent3D != null)
            {
                await VistaPerspectiva3DAsync();
                return;
            }

            var currentCam = _ownerSceneView.Camera;
            if (currentCam != null)
            {
                var newCam = currentCam.RotateTo(currentCam.Heading, 45.0, currentCam.Roll);
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

    [RelayCommand]
    public async Task ZoomCapa3DAsync()
    {
        if (Capa3DSeleccionada != null)
        {
            await ZoomACapa3DAsync(Capa3DSeleccionada);
            return;
        }

        if (UltimoExtent3D != null && _ownerSceneView != null)
        {
            try
            {
                var center = UltimoExtent3D.GetCenter();
                var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                    ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                    : center;

                double targetZ = UltimoCentroZ3D + OffsetZ3D;
                var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
                double distance = Math.Clamp(UltimoRadioMetros3D * 2.2, 120.0, 15_000.0);

                var camera = new Camera(lookAtTarget, distance, 0.0, 50.0, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(1.2));
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
        if (_ownerSceneView == null || extent == null) return;
        try
        {
            var center = extent.GetCenter();
            var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                : center;

            double targetZ = (targetItem?.CentroZ ?? UltimoCentroZ3D) + (targetItem?.OffsetZ3D ?? OffsetZ3D);
            var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
            double radio = targetItem?.RadioMetros ?? UltimoRadioMetros3D;
            double distance = Math.Clamp(radio * 1.8, 100.0, 12_000.0);

            var camera = new Camera(lookAtTarget, distance, 0.0, 0.0, 0.0);
            await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(0.9));
        }
        catch { }
    }

    [RelayCommand]
    public async Task VistaPerspectiva3DAsync()
    {
        var targetItem = Capa3DSeleccionada;
        var extent = targetItem?.ExtentParaZoom ?? UltimoExtent3D;
        if (_ownerSceneView == null || extent == null) return;
        try
        {
            var center = extent.GetCenter();
            var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                : center;

            double targetZ = (targetItem?.CentroZ ?? UltimoCentroZ3D) + (targetItem?.OffsetZ3D ?? OffsetZ3D);
            var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
            double radio = targetItem?.RadioMetros ?? UltimoRadioMetros3D;
            double distance = Math.Clamp(radio * 2.2, 120.0, 15_000.0);

            var camera = new Camera(lookAtTarget, distance, 320.0, 45.0, 0.0);
            await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(0.9));
        }
        catch { }
    }

    public async Task ZoomACapa3DAsync(CapaUsuarioItem item)
    {
        Capa3DSeleccionada = item;
        if (_ownerSceneView == null || item.ExtentParaZoom == null) return;
        try
        {
            var center = item.ExtentParaZoom.GetCenter();
            var wgs84Center = (center.SpatialReference != null && center.SpatialReference.Wkid != 4326)
                ? GeometryEngine.Project(center, SpatialReferences.Wgs84) as MapPoint ?? center
                : center;

            double targetZ = item.CentroZ + item.OffsetZ3D;
            var lookAtTarget = new MapPoint(wgs84Center.X, wgs84Center.Y, targetZ, SpatialReferences.Wgs84);
            double distance = Math.Clamp(item.RadioMetros * 2.2, 120.0, 15_000.0);

            var camera = new Camera(lookAtTarget, distance, 0.0, 50.0, 0.0);
            await _ownerSceneView.SetViewpointCameraAsync(camera, TimeSpan.FromSeconds(1.2));
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
            Capa3DSeleccionada.OffsetZ3D += 25.0;
            OffsetZ3D = Capa3DSeleccionada.OffsetZ3D;
            Capa3DSeleccionada.ReconstruirPuntos();
        }
    }

    [RelayCommand]
    public void BajarAltura3D()
    {
        if (Capa3DSeleccionada != null)
        {
            Capa3DSeleccionada.OffsetZ3D -= 25.0;
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
            var targetGeo = LastViewpoint.TargetGeometry as MapPoint;
            if (targetGeo != null)
            {
                var wgs84 = (targetGeo.SpatialReference != null && targetGeo.SpatialReference.Wkid != 4326)
                    ? GeometryEngine.Project(targetGeo, SpatialReferences.Wgs84) as MapPoint ?? targetGeo
                    : targetGeo;

                double alt = LastViewpoint.TargetScale > 0 ? Math.Max(2000.0, LastViewpoint.TargetScale * 0.7) : 25_000.0;
                var cam = new Camera(wgs84.Y, wgs84.X, alt, 0.0, 45.0, 0.0);
                await _ownerSceneView.SetViewpointCameraAsync(cam, TimeSpan.FromSeconds(1.0));
                return;
            }
        }

        // Vista regional inicial de Colombia en 3D
        var camColombia = new Camera(4.680486, -74.146592, 1_800_000.0, 0.0, 45.0, 0.0);
        await _ownerSceneView.SetViewpointCameraAsync(camColombia, TimeSpan.FromSeconds(1.0));
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

            var itemCapa = new CapaUsuarioItem
            {
                Nombre = Path.GetFileName(path),
                RutaCompleta = path,
                Capa = slpkLayer,
                TipoIcono = "☁️",
                TipoTexto = "Nube de Puntos 3D (SLPK)",
                ExtentParaZoom = slpkLayer.FullExtent,
                InfoDetalle3D = $"Paquete de Escena 3D (.slpk)\nCapa: {slpkLayer.Name}"
            };
            itemCapa.QuitarCommand = new RelayCommand(() =>
            {
                Scene?.OperationalLayers.Remove(slpkLayer);
                CapasAdicionales.Remove(itemCapa);
                Capas3D.Remove(itemCapa);
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

            if (slpkLayer.FullExtent != null)
            {
                await Task.Delay(200);
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

    private async Task CargarNubePuntosLas3DAsync(string path)
    {
        try
        {
            _notifications?.ShowInfo($"Procesando nube de puntos LiDAR '{Path.GetFileName(path)}'...", "Cargando 3D");

            var cloud = await Task.Run(() => LasFileReader.Read(path, maxPointsToSample: 75_000));

            if (cloud.SampledPointsCount == 0)
            {
                _notifications?.ShowWarning("El archivo LAS no contiene puntos legibles.", "Sin Puntos");
                return;
            }

            if (Scene == null) SetupScene();

            var targetSr = cloud.SpatialReference ?? SpatialReferences.Wgs84;

            // Calcular radio aproximado en metros según sistema de referencia
            double radioMetros = cloud.RadioAproximadoMetros;
            if (targetSr.Wkid == 4326 || (cloud.MinX >= -180 && cloud.MaxX <= 180 && cloud.MinY >= -90 && cloud.MaxY <= 90))
            {
                double latRad = cloud.CentroY * Math.PI / 180.0;
                double dx = (cloud.MaxX - cloud.MinX) * 111_320.0 * Math.Cos(latRad);
                double dy = (cloud.MaxY - cloud.MinY) * 111_320.0;
                radioMetros = Math.Sqrt(dx * dx + dy * dy + cloud.AlturaRango * cloud.AlturaRango) / 2.0;
            }
            radioMetros = Math.Max(80.0, radioMetros);

            var envelopeOriginal = new Envelope(cloud.MinX, cloud.MinY, cloud.MaxX, cloud.MaxY, targetSr);
            var envelopeWgs84 = (targetSr.Wkid == 4326)
                ? envelopeOriginal
                : GeometryEngine.Project(envelopeOriginal, SpatialReferences.Wgs84) as Envelope ?? envelopeOriginal;

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

            // Marco Guía y Centro proyectados sobre el relieve (Draped)
            var polyPoints = new PointCollection(targetSr)
            {
                new MapPoint(cloud.MinX, cloud.MinY, targetSr),
                new MapPoint(cloud.MaxX, cloud.MinY, targetSr),
                new MapPoint(cloud.MaxX, cloud.MaxY, targetSr),
                new MapPoint(cloud.MinX, cloud.MaxY, targetSr),
                new MapPoint(cloud.MinX, cloud.MinY, targetSr)
            };
            var footprintPoly = new Polygon(polyPoints, targetSr);
            var footprintWgs84 = (targetSr.Wkid == 4326)
                ? footprintPoly
                : GeometryEngine.Project(footprintPoly, SpatialReferences.Wgs84) as Polygon ?? footprintPoly;

            var lineSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(235, 255, 193, 7), 2.5);
            var fillSymbol = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid, System.Drawing.Color.FromArgb(40, 255, 193, 7), lineSymbol);
            overlayGuia.Graphics.Add(new Graphic(footprintWgs84, fillSymbol));

            var centerPoint = new MapPoint(cloud.CentroX, cloud.CentroY, targetSr);
            var centerWgs84 = (targetSr.Wkid == 4326)
                ? centerPoint
                : GeometryEngine.Project(centerPoint, SpatialReferences.Wgs84) as MapPoint ?? centerPoint;
            var pinSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Cross, System.Drawing.Color.FromArgb(240, 220, 53, 69), 14.0);
            overlayGuia.Graphics.Add(new Graphic(centerWgs84, pinSymbol));

            // 2. Caché y renderizado de puntos de esta capa
            var cachedList = new List<(MapPoint PtWgs84, System.Drawing.Color Color)>(cloud.SampledPointsCount);
            foreach (var pt in cloud.Points)
            {
                var mapPoint = new MapPoint(pt.X, pt.Y, pt.Z, targetSr);
                var wgs84Point = (targetSr.Wkid == 4326)
                    ? mapPoint
                    : GeometryEngine.Project(mapPoint, SpatialReferences.Wgs84) as MapPoint ?? mapPoint;

                var color = System.Drawing.Color.FromArgb(240, pt.R, pt.G, pt.B);
                cachedList.Add((wgs84Point, color));
            }

            var initialGraphics = new List<Graphic>(cachedList.Count);
            foreach (var item in cachedList)
            {
                var symbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, item.Color, 4.5);
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
                Nombre = Path.GetFileName(path),
                RutaCompleta = path,
                Capa = null,
                TipoIcono = "☁️",
                TipoTexto = "Nube de Puntos (LAS)",
                OverlayGuia3D = overlayGuia,
                OverlayPuntos3D = overlayPuntos,
                PuntosMuestreados3D = cachedList,
                CentroZ = cloud.CentroZ,
                RadioMetros = radioMetros,
                CrsNombre = cloud.CrsNombre,
                ExtentParaZoom = envelopeWgs84,
                InfoDetalle3D = $"Archivo: {cloud.TotalPoints:N0} pts (muestra: {cloud.SampledPointsCount:N0})\n" +
                                $"CRS: {cloud.CrsNombre}\n" +
                                $"Dim: {cloud.AnchoMetros:F0}m × {cloud.LargoMetros:F0}m (R: {cloud.RadioAproximadoMetros:F0}m)\n" +
                                $"Elevación Z: {cloud.MinZ:F1} m a {cloud.MaxZ:F1} m (Δ {cloud.AlturaRango:F1} m)\n" +
                                $"Colores: {(cloud.HasRgbColors ? "RGB Fotogramétrico" : "Rampa Hipsométrica")}",
                OffsetZ3D = 0.0,
                TamanoPunto3D = 4.5
            };

            itemCapa.QuitarCommand = new RelayCommand(() =>
            {
                if (_ownerSceneView?.GraphicsOverlays != null)
                {
                    _ownerSceneView.GraphicsOverlays.Remove(overlayGuia);
                    _ownerSceneView.GraphicsOverlays.Remove(overlayPuntos);
                }
                overlayGuia.Graphics.Clear();
                overlayPuntos.Graphics.Clear();
                CapasAdicionales.Remove(itemCapa);
                Capas3D.Remove(itemCapa);
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

            await Task.Delay(250);
            await ZoomACapa3DAsync(itemCapa);

            _notifications?.ShowSuccess(
                $"Nube de puntos renderizada: {cloud.SampledPointsCount:N0} puntos ({cloud.CrsNombre}). Capas 3D activas: {Capas3D.Count}.",
                "Visor 3D");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Error al leer nube de puntos LAS '{path}'", ex);
            _notifications?.ShowError($"Error al cargar archivo LiDAR: {ex.Message}", "Error LAS 3D");
        }
    }

    [ObservableProperty] private Map? map;

    // Guarda el último viewpoint mostrado en el MapView para restaurarlo
    // cuando la vista se vuelva a adjuntar.
    public Viewpoint? LastViewpoint { get; set; }

    async partial void OnIsModoOfflineForzadoChanged(bool value)
    {
        AppLogger.Info($"[MapaViewModel] OnIsModoOfflineForzadoChanged: {value}");
        ActualizarDisponibilidadMapasBase();

        if (value)
        {
            var defaultOption = MapasBase.FirstOrDefault(b => b.IsDefaultOffline);
            if (defaultOption != null)
            {
                await AplicarBasemapAsync(defaultOption, true);
            }
            _notifications?.ShowInfo("Modo Offline activado: usando mapa base local predeterminado.", "Modo Sin Conexión");
        }
        else
        {
            if (IsSinConexionInternet)
            {
                _notifications?.ShowWarning("No se detectó conexión a internet activa para salir del modo offline.", "Sin Conexión");
                IsModoOfflineForzado = true;
                return;
            }

            if (MapaBaseSeleccionado != null && Map != null)
            {
                await AplicarBasemapAsync(MapaBaseSeleccionado, false);
                _notifications?.ShowInfo("Modo Online activado: usando servicios web de ArcGIS.", "Modo En Línea");
            }
        }
    }

    public void ActualizarDisponibilidadMapasBase()
    {
        bool offlineActivo = IsModoOfflineForzado || IsSinConexionInternet;

        foreach (var b in MapasBase)
        {
            if (b.IsDefaultOffline)
            {
                b.IsHabilitado = true;
                b.EstadoTexto = offlineActivo ? "Offline Activo" : "Offline Predeterminado";
            }
            else
            {
                b.IsHabilitado = !offlineActivo;
                b.EstadoTexto = offlineActivo ? "No disponible sin conexión" : "En línea";
            }
        }

        if (offlineActivo)
        {
            var defaultOffline = MapasBase.FirstOrDefault(b => b.IsDefaultOffline);
            if (defaultOffline != null && MapaBaseSeleccionado != defaultOffline)
            {
                MapaBaseSeleccionado = defaultOffline;
                foreach (var b in MapasBase)
                {
                    b.IsSeleccionado = (b == defaultOffline);
                }
            }
        }
    }

    [RelayCommand]
    public void ToggleSelectorMapasBase()
    {
        IsSelectorMapasBaseVisible = !IsSelectorMapasBaseVisible;
        if (IsSelectorMapasBaseVisible)
        {
            IsHerramientasMedicionVisible = false;
            IsPanelCapasVisible = false;
        }
    }

    [RelayCommand]
    public void TogglePanelCapas()
    {
        IsPanelCapasVisible = !IsPanelCapasVisible;
        if (IsPanelCapasVisible)
        {
            IsSelectorMapasBaseVisible = false;
            IsHerramientasMedicionVisible = false;
        }
    }

    [RelayCommand]
    public void QuitarTodasLasCapas()
    {
        if (CapasAdicionales.Count == 0) return;

        foreach (var item in CapasAdicionales.ToList())
        {
            if (item.Capa != null)
            {
                Map?.OperationalLayers.Remove(item.Capa);
                Scene?.OperationalLayers.Remove(item.Capa);
                _rasterExtentsSeguros.Remove(item.Capa);
            }
            if (_ownerSceneView?.GraphicsOverlays != null)
            {
                if (item.OverlayGuia3D != null) _ownerSceneView.GraphicsOverlays.Remove(item.OverlayGuia3D);
                if (item.OverlayPuntos3D != null) _ownerSceneView.GraphicsOverlays.Remove(item.OverlayPuntos3D);
            }
            item.OverlayGuia3D?.Graphics.Clear();
            item.OverlayPuntos3D?.Graphics.Clear();
        }
        CapasAdicionales.Clear();
        Capas3D.Clear();
        Capa3DSeleccionada = null;
        SincronizarEstadoCapas3D();
        IsPanelCapasVisible = false;
        _notifications?.ShowInfo("Se han quitado todas las capas adicionales del visor.", "Capas");
    }

    [RelayCommand]
    public async Task CambiarMapaBaseAsync(BasemapOption? option)
    {
        if (option == null || Map == null) return;
        if (!option.IsHabilitado)
        {
            _notifications?.ShowWarning($"El mapa '{option.Nombre}' requiere conexión a internet y no está disponible en modo offline.", "Mapa No Disponible");
            return;
        }
        await AplicarBasemapAsync(option, IsModoOfflineForzado || IsSinConexionInternet);
    }

    private async Task AplicarBasemapAsync(BasemapOption option, bool forzarOffline)
    {
        if (Map == null) return;

        try
        {
            var basemap = _basemapService.CrearBasemap(option, forzarOffline);
            MapaBaseSeleccionado = option;
            foreach (var b in MapasBase)
            {
                b.IsSeleccionado = (b == option);
            }
            IsSelectorMapasBaseVisible = false;

            // Cargar el basemap para obtener su SpatialReference
            try
            {
                await basemap.LoadAsync();
            }
            catch (Exception loadEx)
            {
                AppLogger.Warn($"Basemap LoadAsync warning: {loadEx.Message}");
            }

            var basemapLayer = basemap.BaseLayers.FirstOrDefault();
            if (basemapLayer != null && basemapLayer.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
            {
                try { await basemapLayer.LoadAsync(); } catch { }
            }
            var basemapSr = basemapLayer?.SpatialReference;
            AppLogger.Info($"Basemap '{option.Nombre}' SpatialReference: {basemapSr?.Wkid ?? 0}; Map SpatialReference: {Map.SpatialReference?.Wkid ?? 0}");

            // Si los sistemas de referencia difieren (ej. 3857 vs 3116):
            // En ArcGIS Runtime se debe instanciar un nuevo Map para adoptar la nueva proyección
            if (Map.SpatialReference != null && basemapSr != null && Map.SpatialReference.Wkid != basemapSr.Wkid)
            {
                AppLogger.Info($"Cambiando SpatialReference del mapa de {Map.SpatialReference.Wkid} a {basemapSr.Wkid}. Reinstanciando Map.");

                var newMap = new Map(basemap);
                var ops = Map.OperationalLayers.ToList();
                Map.OperationalLayers.Clear();
                foreach (var l in ops)
                {
                    newMap.OperationalLayers.Add(l);
                }

                Map = newMap;
                if (_ownerMapView != null)
                {
                    _ownerMapView.Map = newMap;
                }
            }
            else
            {
                Map.Basemap = basemap;
            }

            if (Scene != null)
            {
                try
                {
                    if (option.Style != null)
                        Scene.Basemap = new Basemap(option.Style.Value);
                    else
                        Scene.Basemap = _basemapService.CrearBasemap(option, forzarOffline || IsSinConexionInternet);
                }
                catch { }
            }

            var modoTexto = (forzarOffline || IsSinConexionInternet)
                ? " (Modo Offline)"
                : "";
            _notifications?.ShowSuccess($"Mapa base cambiado a: {option.Nombre}{modoTexto}", "Mapa Base");

            if (_ownerMapView != null)
            {
                await RestablecerVistaMapaAsync();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Error al aplicar basemap '{option.Nombre}'", ex);
            _notifications?.ShowError($"No se pudo aplicar el mapa base: {ex.Message}", "Error Mapa Base");
        }
    }

    [RelayCommand]
    public void ToggleHerramientasMedicion()
    {
        IsHerramientasMedicionVisible = !IsHerramientasMedicionVisible;
        if (IsHerramientasMedicionVisible)
        {
            IsSelectorMapasBaseVisible = false;
            IsPanelCapasVisible = false;
        }
        else
        {
            ModoMedicion = "Ninguno";
            LimpiarMedicion();
        }
    }

    [RelayCommand]
    public void ActivarMedicionDistancia()
    {
        ModoMedicion = "Distancia";
        LimpiarMedicion();
        _notifications?.ShowInfo("Haga clic en el mapa para trazar puntos y medir distancias.", "Medir Distancia");
    }

    [RelayCommand]
    public void ActivarMedicionArea()
    {
        ModoMedicion = "Area";
        LimpiarMedicion();
        _notifications?.ShowInfo("Haga clic en el mapa para trazar los vértices del polígono y medir el área.", "Medir Área");
    }

    [RelayCommand]
    public void LimpiarMedicion()
    {
        _puntosMedicion.Clear();
        OverlayMedicion.Graphics.Clear();
        ResultadoMedicion = "";
        DetalleMedicion = "";
        HasResultadoMedicion = false;
    }

    [RelayCommand]
    public void CopiarMedicion()
    {
        if (string.IsNullOrWhiteSpace(ResultadoMedicion)) return;
        try
        {
            Clipboard.SetText($"{ResultadoMedicion} ({DetalleMedicion})");
            _notifications?.ShowSuccess("Resultado de medición copiado al portapapeles.", "Copiado");
        }
        catch { }
    }

    public void AgregarPuntoMedicion(MapPoint punto)
    {
        if (ModoMedicion == "Ninguno") return;

        var puntoWgs84 = GeometryEngine.Project(punto, SpatialReferences.Wgs84) as MapPoint ?? punto;
        _puntosMedicion.Add(puntoWgs84);

        OverlayMedicion.Graphics.Clear();

        var puntoSymbol = new SimpleMarkerSymbol(
            SimpleMarkerSymbolStyle.Circle,
            System.Drawing.Color.FromArgb(255, 24, 134, 75),
            8);
        var lineSymbol = new SimpleLineSymbol(
            SimpleLineSymbolStyle.Solid,
            System.Drawing.Color.FromArgb(230, 24, 134, 75),
            3);

        // Dibujar vértices
        foreach (var p in _puntosMedicion)
        {
            OverlayMedicion.Graphics.Add(new Graphic(p, puntoSymbol));
        }

        if (ModoMedicion == "Distancia")
        {
            if (_puntosMedicion.Count >= 2)
            {
                var polyline = new Polyline(_puntosMedicion, SpatialReferences.Wgs84);
                OverlayMedicion.Graphics.Add(new Graphic(polyline, lineSymbol));

                var longitudMetros = GeometryEngine.LengthGeodetic(polyline, LinearUnits.Meters, GeodeticCurveType.Geodesic);
                if (longitudMetros >= 1000)
                {
                    var km = longitudMetros / 1000.0;
                    ResultadoMedicion = $"{km:F2} km";
                    DetalleMedicion = $"{longitudMetros:N0} metros ({_puntosMedicion.Count} puntos)";
                }
                else
                {
                    ResultadoMedicion = $"{longitudMetros:F1} m";
                    DetalleMedicion = $"{_puntosMedicion.Count} puntos marcados";
                }
                HasResultadoMedicion = true;
            }
            else
            {
                ResultadoMedicion = "1 punto marcado";
                DetalleMedicion = "Haga clic en otro punto para calcular la distancia";
                HasResultadoMedicion = true;
            }
        }
        else if (ModoMedicion == "Area")
        {
            if (_puntosMedicion.Count >= 3)
            {
                var polygon = new Polygon(_puntosMedicion, SpatialReferences.Wgs84);
                var fillSymbol = new SimpleFillSymbol(
                    SimpleFillSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(80, 24, 134, 75),
                    lineSymbol);

                OverlayMedicion.Graphics.Add(new Graphic(polygon, fillSymbol));

                var areaM2 = Math.Abs(GeometryEngine.AreaGeodetic(polygon, AreaUnits.SquareMeters, GeodeticCurveType.Geodesic));
                var ha = areaM2 / 10_000.0;
                var km2 = areaM2 / 1_000_000.0;

                if (areaM2 >= 1_000_000)
                {
                    ResultadoMedicion = $"{km2:F2} km²";
                    DetalleMedicion = $"{ha:N1} ha | {areaM2:N0} m² ({_puntosMedicion.Count} vértices)";
                }
                else if (areaM2 >= 10_000)
                {
                    ResultadoMedicion = $"{ha:F2} ha";
                    DetalleMedicion = $"{areaM2:N0} m² ({_puntosMedicion.Count} vértices)";
                }
                else
                {
                    ResultadoMedicion = $"{areaM2:N1} m²";
                    DetalleMedicion = $"{_puntosMedicion.Count} vértices";
                }
                HasResultadoMedicion = true;
            }
            else if (_puntosMedicion.Count == 2)
            {
                var polyline = new Polyline(_puntosMedicion, SpatialReferences.Wgs84);
                OverlayMedicion.Graphics.Add(new Graphic(polyline, lineSymbol));
                ResultadoMedicion = "2 vértices marcados";
                DetalleMedicion = "Agregue al menos 3 vértices para calcular el área";
                HasResultadoMedicion = true;
            }
            else
            {
                ResultadoMedicion = "1 vértice marcado";
                DetalleMedicion = "Agregue al menos 3 vértices para calcular el área";
                HasResultadoMedicion = true;
            }
        }
    }

    public void ActualizarCoordenadasCursor(double lat, double lon)
    {
        var latDir = lat >= 0 ? "N" : "S";
        var lonDir = lon >= 0 ? "E" : "W";
        CoordenadasCursorTexto = $"Lat: {Math.Abs(lat):F5}° {latDir}  |  Lon: {Math.Abs(lon):F5}° {lonDir}";
    }

    public void ActualizarEscala(double escala)
    {
        if (escala > 0)
        {
            EscalaMapaTexto = $"Escala 1:{escala:N0}";
        }
    }

    private void SetupMap()
    {
        var newMap = new Map(BasemapStyle.ArcGISTopographic);
        var center = new MapPoint(-74.146592, 4.680486, SpatialReferences.Wgs84);
        newMap.InitialViewpoint = new Viewpoint(center, 12_000_000);
        Map = newMap;
    }

    // Fallback para crear campos cuando no existen los helpers CreateXxx
    private static Field OID(string name)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeOID\",\"alias\":\"{name}\"}}")!;

    private static Field Int(string name, string? alias = null)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeInteger\",\"alias\":\"{alias ?? name}\"}}")!;

    private static Field Str(string name, int length, string? alias = null)
        => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeString\",\"alias\":\"{alias ?? name}\",\"length\":{length}}}")!;

    /// <summary>
    /// Busca el id de proyecto correspondiente al OID de un feature en la capa de proyectos.
    /// </summary>
    public int? BuscarIdProyectoPorOid(long oid)
        => _oidToProjectId.TryGetValue(oid, out var id) ? id : null;

 /// <summary>
 /// Invalida el caché de municipios y la capa de proyectos para reflejar datos nuevos.
 /// Llamar después de crear/eliminar un proyecto.
 /// </summary>
 public void InvalidarCache()
 {
  _cachedMunicipios = null;
  _cachedGeometries = null;
  _oidToProjectId.Clear();
  _layerProyectos = null;
  _layerMunicipios = null;
  _updateGeneration++;
  Map?.OperationalLayers.Clear();
 }

  /// <summary>
  /// Realiza la búsqueda geográfica y por filtros de proyectos y actualiza las capas del mapa.
  /// </summary>
  public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarConFiltrosGeograficosAsync(CancellationToken ct = default)
  {
      string? dptoCodigo = null;
      string? mpioCodigo = null;
      double? minX = null;
      double? minY = null;
      double? maxX = null;
      double? maxY = null;

      if (Filtros.AreaInteres is FiltrosViewModel.MunicipioItem muni && !string.IsNullOrEmpty(muni.Codigo))
      {
          mpioCodigo = muni.Codigo;
          var extent = await _municipios.ExtentPorMunicipiosAsync(new[] { muni.Codigo });
          if (extent != null)
          {
              minX = extent.West;
              minY = extent.South;
              maxX = extent.East;
              maxY = extent.North;
          }
      }
      else if (Filtros.SelectedDepartamento is FiltrosViewModel.DepartamentoItem dept && !string.IsNullOrEmpty(dept.Codigo))
      {
          dptoCodigo = dept.Codigo;
          var extent = await _municipios.ExtentPorDepartamentoAsync(dept.Codigo);
          if (extent != null)
          {
              minX = extent.West;
              minY = extent.South;
              maxX = extent.East;
              maxY = extent.North;
          }
      }

      return await BuscarYActualizarCapasAsync(
          Filtros.PalabraClave,
          Filtros.Desde,
          Filtros.Hasta,
          dptoCodigo,
          mpioCodigo,
          minX,
          minY,
          maxX,
          maxY,
          ct);
  }

 /// <summary>
 /// Actualiza las capas del mapa con los proyectos filtrados.
 /// Llamar desde MapaView después de obtener los resultados filtrados.
 /// </summary>
 public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarYActualizarCapasAsync(
     string? texto,
     DateTime? desde,
     DateTime? hasta,
     string? dptoCodigo = null,
     string? mpioCodigo = null,
     double? minX = null,
     double? minY = null,
     double? maxX = null,
     double? maxY = null,
     CancellationToken ct = default)
 {
  var proyectos = await _buscarProyectos.EjecutarAsync(texto, desde, hasta, dptoCodigo, mpioCodigo, minX, minY, maxX, maxY, ct);
  await ActualizarCapasConFiltroAsync(proyectos);
  return proyectos;
 }

 public async Task ActualizarCapasConFiltroAsync(IReadOnlyList<ProyectoGeomatico> proyectosFiltrados)
 {
  if (Map == null) return;

  var gen = ++_updateGeneration;

  try
  {
   // Limpiar todas las capas operacionales para evitar capas huérfanas
   Map.OperationalLayers.Clear();
   _layerProyectos = null;
   _layerMunicipios = null;

   // 1. Crear capa de proyectos
   var layerProy = await CrearCapaProyectosDesdeListaAsync(proyectosFiltrados);
   if (gen != _updateGeneration) return;

   // 2. Obtener municipios de los proyectos filtrados
   var ids = proyectosFiltrados.Select(p => p.Id).ToList();
   var codigosMuni = ids.Count > 0
    ? await _proyectos.ObtenerCodigosMunicipioAsync(ids)
    : (IReadOnlyList<string>)Array.Empty<string>();
   if (gen != _updateGeneration) return;
   _ultimosCodigosMunicipio = codigosMuni;

   // 3. Asegurar que el caché de geometrías esté poblado
   if (_cachedMunicipios == null)
   {
    var todosCodigosConProyecto = await _proyectos.ObtenerTodosCodigosMunicipioAsync();
    _cachedMunicipios = todosCodigosConProyecto.Count > 0
     ? await _municipios.PorCodigosGeoJsonAsync(todosCodigosConProyecto)
     : (IReadOnlyList<MunicipioGeoJsonDto>)Array.Empty<MunicipioGeoJsonDto>();
   }
   if (gen != _updateGeneration) return;

   if (_cachedGeometries == null)
   {
    var munis = _cachedMunicipios;
    _cachedGeometries = await Task.Run(() =>
    {
     var dict = new Dictionary<string, Geometry>(munis.Count);
     foreach (var m in munis)
     {
      if (string.IsNullOrEmpty(m.GeoJson)) continue;
      try
      {
       var geom = ParseGeoJson(m.GeoJson);
       if (geom != null) dict[m.Codigo] = geom;
      }
      catch { }
     }
     return dict;
    });
   }
   if (gen != _updateGeneration) return;

   // 4. Crear capa de municipios solo con los del filtro
   var filteredMuni = _cachedMunicipios.Where(m => codigosMuni.Contains(m.Codigo));
   var layerMuni = await CrearCapaMunicipiosFiltradaAsync(filteredMuni);
   if (gen != _updateGeneration) return;

   // 5. Agregar capas al mapa (municipios abajo, proyectos arriba)
   Map.OperationalLayers.Clear();
   if (layerMuni != null)
   {
    Map.OperationalLayers.Add(layerMuni);
    await layerMuni.LoadAsync();
   }
   if (gen != _updateGeneration) return;
   if (layerProy != null)
   {
    Map.OperationalLayers.Add(layerProy);
    await layerProy.LoadAsync();
   }

   _layerMunicipios = layerMuni;
   _layerProyectos = layerProy;
  }
  catch (Exception ex)
  {
   System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error actualizando capas con filtro: {ex}");
  }
 }

 /// <summary>
 /// Devuelve el extent (Envelope) de los municipios que contienen proyectos del último filtro aplicado.
 /// </summary>
 public Envelope? ObtenerExtentMunicipiosFiltrados()
 {
  if (_cachedGeometries == null || _ultimosCodigosMunicipio == null || _ultimosCodigosMunicipio.Count == 0)
   return null;

  double xmin = double.MaxValue, ymin = double.MaxValue;
  double xmax = double.MinValue, ymax = double.MinValue;
  bool any = false;
  foreach (var codigo in _ultimosCodigosMunicipio)
  {
   if (_cachedGeometries.TryGetValue(codigo, out var geom))
   {
    var ext = geom.Extent;
    if (ext != null)
    {
     xmin = Math.Min(xmin, ext.XMin);
     ymin = Math.Min(ymin, ext.YMin);
     xmax = Math.Max(xmax, ext.XMax);
     ymax = Math.Max(ymax, ext.YMax);
     any = true;
    }
   }
  }
  return any ? new Envelope(xmin, ymin, xmax, ymax, SpatialReferences.Wgs84) : null;
 }

 private async Task<Layer?> CrearCapaProyectosDesdeListaAsync(IReadOnlyList<ProyectoGeomatico> items)
 {
 var fields = new List<Field>
 {
  OID("oid"),
  Int("id_proyecto"),
  Str("titulo", 200),
  Str("ruta_archivos", 1024)
 };
 var table = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);

 _oidToProjectId.Clear();
 var features = new List<Feature>();
 int oid = 1;
 foreach (var p in items)
 {
  if (p.Longitud == 0 && p.Latitud == 0) continue;
  var currentOid = oid++;
  _oidToProjectId[currentOid] = p.Id;
  var attrs = new Dictionary<string, object?>
  {
  ["oid"] = currentOid,
  ["id_proyecto"] = p.Id,
  ["titulo"] = p.Titulo,
  ["ruta_archivos"] = string.IsNullOrWhiteSpace(p.RutaArchivos) ? null : p.RutaArchivos
  };
  var geom = new MapPoint(p.Longitud, p.Latitud, SpatialReferences.Wgs84);
  features.Add(table.CreateFeature(attrs, geom));
 }

 if (features.Count == 0) return null;
 await table.AddFeaturesAsync(features);

 var marker = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.OrangeRed, 9)
 {
  Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1.5)
 };
 table.Renderer = new SimpleRenderer(marker);

 var collection = new FeatureCollection(new[] { table });
 var layer = new FeatureCollectionLayer(collection)
 {
  Name = "Proyectos"
 };
 return layer;
 }

 private async Task<Layer?> CrearCapaMunicipiosFiltradaAsync(IEnumerable<MunicipioGeoJsonDto> municipios)
 {
  var fields = new List<Field>
  {
   OID("oid"),
   Str("mpio_cdpmp",5),
   Str("mpio_cnmbr",200)
  };
  var table = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);

  int oid = 1;
  var features = new List<Feature>();
  foreach (var m in municipios)
  {
   if (!_cachedGeometries!.TryGetValue(m.Codigo, out var geom)) continue;

   var attrs = new Dictionary<string, object?>
   {
    ["oid"] = oid++,
    ["mpio_cdpmp"] = m.Codigo,
    ["mpio_cnmbr"] = m.Nombre
   };
   features.Add(table.CreateFeature(attrs, geom));
  }

  if (features.Count == 0) return null;
  await table.AddFeaturesAsync(features);

  table.Renderer = new SimpleRenderer(
   new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
    System.Drawing.Color.FromArgb(40, 33, 150, 243),
    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
     System.Drawing.Color.FromArgb(180, 33, 150, 243), 1.5f)));

  var collection = new FeatureCollection(new[] { table });
  var layer = new FeatureCollectionLayer(collection)
  {
   Name = "Municipios"
  };
  return layer;
 }

 /// <summary>
 /// Parses a GeoJSON geometry string (Polygon/MultiPolygon) into an ArcGIS Geometry.
 /// Geometry.FromJson() expects Esri JSON, not GeoJSON, so we parse coordinates manually.
 /// </summary>
 public static Geometry? ParseGeoJson(string geoJson)
 {
  using var doc = JsonDocument.Parse(geoJson);
  var root = doc.RootElement;
  var type = root.GetProperty("type").GetString();
  var coordinates = root.GetProperty("coordinates");

  if (type is not ("Polygon" or "MultiPolygon")) return null;

  var builder = new PolygonBuilder(SpatialReferences.Wgs84);

  // MultiPolygon: [polygon, polygon, ...] where polygon = [ring, ring, ...]
  // Polygon: [ring, ring, ...] where ring = [[lon, lat], ...]
  if (type == "Polygon")
  {
  	foreach (var ring in coordinates.EnumerateArray())
  	{
  		var numPoints = ring.GetArrayLength();
  		var points = new MapPoint[numPoints];
  		int pointIndex = 0;

  		foreach (var point in ring.EnumerateArray())
  		{
  			points[pointIndex++] = new MapPoint(point[0].GetDouble(), point[1].GetDouble(), SpatialReferences.Wgs84);
  		}
  		builder.AddPart(points);
  	}
  }
  else
  {
  	foreach (var polygon in coordinates.EnumerateArray())
  	{
  		foreach (var ring in polygon.EnumerateArray())
  		{
  			var numPoints = ring.GetArrayLength();
  			var points = new MapPoint[numPoints];
  			int pointIndex = 0;

  			foreach (var point in ring.EnumerateArray())
  			{
  				points[pointIndex++] = new MapPoint(point[0].GetDouble(), point[1].GetDouble(), SpatialReferences.Wgs84);
  			}
  			builder.AddPart(points);
  		}
  	}
  }

  return builder.ToGeometry();
 }
}
}

