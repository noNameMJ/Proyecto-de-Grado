using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Rasters;
using Geomatica.Data.Repositories;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.UI.Controls;
using System.Collections.ObjectModel;
using System.IO;
using Geomatica.Desktop.Services;

namespace Geomatica.Desktop.ViewModels
{
    public class CapaUsuarioItem
    {
        public string Nombre { get; set; } = "";
        public Layer? Capa { get; set; }
        public IRelayCommand? QuitarCommand { get; set; }
        public IRelayCommand? ZoomCommand { get; set; }
    }

 public class MapaViewModel : INotifyPropertyChanged
 {
 private readonly IProyectoRepository _proyectos;
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

 public ObservableCollection<CapaUsuarioItem> CapasAdicionales { get; } = new();

 public string? UltimosSidecarsRaster { get; private set; }

 // Comando y evento para Home (MVVM)
 public IRelayCommand HomeCommand { get; }
 public event EventHandler? HomeRequested;
 public event EventHandler<ProyectoDetalleDto>? FichaProyectoSolicitada;

 // Nuevo constructor: recibe los filtros (opción B)
 public MapaViewModel(IProyectoRepository proyectos, IMunicipioRepository municipios, FiltrosViewModel filtros, ArchivosViewModel archivosVM)
 {
 _proyectos = proyectos;
 _municipios = municipios;
 Filtros = filtros;
 ArchivosVM = archivosVM;

 HomeCommand = new RelayCommand(() => HomeRequested?.Invoke(this, EventArgs.Empty));

 ArchivosVM.AbrirEnMapaSolicitado += async (s, path) => await CargarCapaAdicionalAsync(path);
 Filtros.PropertyChanged += Filtros_PropertyChanged;

 SetupMap();
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
            // Maneja bases de datos locales .geodatabase y file geodatabases (.gdb) si el SDK lo soporta como path
            var gdb = await Geodatabase.OpenAsync(path);
            var table = gdb.GeodatabaseFeatureTables.FirstOrDefault();
            if (table != null) layer = new FeatureLayer(table);
        }
        else if (ext == ".slpk")
        {
            // IntegratedMesh, PointCloud or 3D Objects in a Scene Layer
            layer = new ArcGISSceneLayer(new Uri(path));
        }
        else if (ext == ".las" || ext == ".laz" || ext == ".zlas")
        {
            // Nota: Para visualizar archivos de Nube de Puntos directamente en MapView WPF como dataset
            // Dependiendo de la versión de ArcGIS Runtime puede ser PointCloudLayer.
            try 
            {
                layer = new PointCloudLayer(new Uri(path));
            } 
            catch (Exception)
            {
                MessageBox.Show("ArcGIS Runtime requiere un SLPK o dataset compatible para nubes de puntos .las/.laz.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (ext == ".tif" || ext == ".tiff")
        {
            layer = await CrearRasterLayerValidadoAsync(path);
            if (layer == null) return;
        }

        if (layer != null)
        {
            layer.LoadStatusChanged += async (s, e) =>
            {
                RasterDiagnostics.LogArcGisLayerError("Layer.LoadStatusChanged", layer.Name, e.Status.ToString(), layer.LoadError);
                if (e.Status == Esri.ArcGISRuntime.LoadStatus.Loaded && _ownerMapView != null)
                {
                    await ZoomCapaAsync(layer, 20, "carga inicial");
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

            if (layer.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded && _ownerMapView != null)
                await ZoomCapaAsync(layer, 20, "post-add");

            var item = new CapaUsuarioItem { Nombre = Path.GetFileName(path), Capa = layer };
            item.QuitarCommand = new RelayCommand(() => 
            {
                if (item.Capa != null) Map.OperationalLayers.Remove(item.Capa);
                CapasAdicionales.Remove(item);
            });
            
            item.ZoomCommand = new RelayCommand(async () =>
            {
                if (item.Capa != null)
                    await ZoomCapaAsync(item.Capa, 50, "manual");
            });

            Application.Current.Dispatcher.Invoke(() => CapasAdicionales.Add(item));
        }
        else
        {
            MessageBox.Show("El formato de archivo no se puede mostrar en el mapa.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    catch (Exception ex)
    {
        RasterDiagnostics.LogException("Unexpected user layer load error", ex);
        MessageBox.Show($"Error al cargar en mapa: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        RasterDiagnostics.Log($"TIFF selected path={path}; sidecars={string.Join(", ", sidecars.Select(s => Path.GetFileName(s)))}");

        var raster = new Raster(path);
        await raster.LoadAsync();
        if (raster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.FailedToLoad)
        {
            RasterDiagnostics.LogException("Raster.LoadAsync failed", raster.LoadError);
            MessageBox.Show(
                $"ArcGIS Runtime no pudo cargar el TIFF.\n\nRuta: {path}\nDetalle: {ObtenerMensajeErrorArcGis(raster.LoadError)}",
                "Error de Raster",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }

        var rasterInfo = raster.RasterInfo;
        if (rasterInfo == null)
        {
            RasterDiagnostics.Log($"RasterInfo is null path={path}");
            MessageBox.Show("No se pudo leer la información del raster.", "Aviso de Raster", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        var rasterLayer = new RasterLayer(raster);
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

        var validationError = ValidarRasterGeorreferenciado(path, rasterInfo, rasterLayer, sidecars);
        if (validationError != null)
        {
            RasterDiagnostics.Log($"Raster rejected path={path}; reason={validationError.Replace(Environment.NewLine, " | ")}");
            MessageBox.Show(validationError, "Raster TIFF no georreferenciado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        if (fileInfo.Length > 700_000_000)
        {
            MessageBox.Show(
                "El raster se cargó, pero es grande. Si el paneo o zoom se siente lento, usa un GeoTIFF/COG con pirámides internas.",
                "Raster grande",
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

    var sr = rasterLayer.SpatialReference ?? rasterInfo.SpatialReference ?? extent.SpatialReference;
    if (sr == null)
    {
        var sidecarText = sidecars.Count == 0
            ? "No se encontraron archivos auxiliares .tfw/.prj/.aux.xml junto al TIFF."
            : $"Sidecars detectados: {string.Join(", ", sidecars.Select(Path.GetFileName))}. ArcGIS Runtime no los aplicó al raster.";

        return
            "ArcGIS Runtime cargó el TIFF sin sistema de coordenadas. La app no lo agregará al mapa porque su extent queda en coordenadas de pixel y provoca error de renderizado.\n\n" +
            $"Ruta: {path}\n" +
            $"Extent leído: {extent}\n" +
            $"{sidecarText}\n\n" +
            "Corrección del dato: exporta un GeoTIFF con CRS embebido (no solo sidecars). " +
            "Si trabajas con MAGNA Colombia Bogotá TM (EPSG:3116), genera un GeoTIFF que ArcGIS Runtime WPF reconozca; " +
            "en pruebas locales, el GeoTIFF reproyectado a EPSG:4326 sí reportó SpatialReference. Para mosaicos grandes, genera pirámides/overviews internas o publica un ImageServer.";
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
        path + ".aux.xml"
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
    if (error == null) return "Error desconocido.";
    var messages = new List<string>();
    for (var current = error; current != null; current = current.InnerException)
        messages.Add(current.Message);
    return string.Join(" | ", messages);
 }

 private async Task ZoomCapaAsync(Layer layer, double padding, string origen)
 {
    if (_ownerMapView == null)
    {
        MessageBox.Show("La vista de mapa todavía no está lista para centrar la capa.", "Zoom a Capa", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
    }

    var extent = layer.FullExtent;
    if (extent == null || extent.SpatialReference == null || !EsEnvelopeFinito(extent))
    {
        RasterDiagnostics.Log($"Zoom rejected origin={origen}; layer={layer.Name}; extent={extent}");
        MessageBox.Show("La capa no tiene información espacial válida para hacer zoom.", "Zoom a Capa", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        MessageBox.Show($"No se pudo centrar la capa.\n\nDetalle: {ex.Message}", "Zoom a Capa", MessageBoxButton.OK, MessageBoxImage.Warning);
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
 // Ensure the MapView.Map assignment happens on the UI thread and completes before returning.
 try
 {
 if (Application.Current == null)
 {
 // fallback: do the operation directly
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
 // detach previous owner
 if (_ownerMapView != null)
 {
 try { _ownerMapView.Map = null; } catch { }
 }
 _ownerMapView = mv;
 if (_ownerMapView != null && _ownerMapView.Map != Map)
 {
 _ownerMapView.Map = Map;
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
 if (_ownerMapView == mv)
 {
 try
 {
 // Save current viewpoint so zoom/center can be restored later
 try
 {
 var vp = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
 if (vp != null)
 {
 LastViewpoint = vp;
 }
 }
 catch { }

 try { _ownerMapView.Map = null; } catch { }
 _ownerMapView = null;
 }
 catch (Exception ex)
 {
 System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error en DoDetach: {ex}");
 }
 }
 }

 public event PropertyChangedEventHandler? PropertyChanged;
 protected void OnPropertyChanged([CallerMemberName] string name = "") =>
 PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

 private Map? _map;
 public Map? Map { get => _map; set { _map = value; OnPropertyChanged(); } }

 // Guarda el último viewpoint mostrado en el MapView para restaurarlo
 // cuando la vista se vuelva a adjuntar.
 public Viewpoint? LastViewpoint { get; set; }

 private void SetupMap()
 {
 var map = new Map(BasemapStyle.ArcGISTopographic);
 var center = new MapPoint(-74.146592,4.680486, SpatialReferences.Wgs84);
 map.InitialViewpoint = new Viewpoint(center,12_000_000);
 Map = map;
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
 /// Actualiza las capas del mapa con los proyectos filtrados.
 /// Llamar desde MapaView después de obtener los resultados filtrados.
 /// </summary>
 public async Task ActualizarCapasConFiltroAsync(IReadOnlyList<ProyectoDto> proyectosFiltrados)
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

 private async Task<Layer?> CrearCapaProyectosDesdeListaAsync(IReadOnlyList<ProyectoDto> items)
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
  if (p.Lon == 0 && p.Lat == 0) continue;
  var currentOid = oid++;
  _oidToProjectId[currentOid] = p.Id;
  var attrs = new Dictionary<string, object?>
  {
  ["oid"] = currentOid,
  ["id_proyecto"] = p.Id,
  ["titulo"] = p.Titulo,
  ["ruta_archivos"] = p.RutaArchivos
  };
  var geom = new MapPoint(p.Lon, p.Lat, SpatialReferences.Wgs84);
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
 var layer = new FeatureCollectionLayer(collection);
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
  return new FeatureCollectionLayer(collection);
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

