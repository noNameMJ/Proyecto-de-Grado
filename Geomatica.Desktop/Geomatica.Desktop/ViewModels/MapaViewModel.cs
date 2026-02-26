using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Data.Repositories;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.UI.Controls;

namespace Geomatica.Desktop.ViewModels
{
 public class MapaViewModel : INotifyPropertyChanged
 {
 private readonly IProyectoRepository _proyectos;
 private readonly IMunicipioRepository _municipios;

 // Referencia opcional a los filtros compartidos
 public FiltrosViewModel Filtros { get; }
 private Layer? _layerMunicipios;
 private Layer? _layerProyectos;
 public Layer? LayerProyectos => _layerProyectos;
 private IReadOnlyList<MunicipioGeoJsonDto>? _cachedMunicipios;
 private Dictionary<string, Geometry>? _cachedGeometries;
 private IReadOnlyList<string>? _ultimosCodigosMunicipio;
 private Dictionary<long, int> _oidToProjectId = new();

 // Track the MapView that currently displays this Map to release ownership when re-attaching
 private MapView? _ownerMapView;

 // Comando y evento para Home (MVVM)
 public IRelayCommand HomeCommand { get; }
 public event EventHandler? HomeRequested;
 public event EventHandler<ProyectoDetalleDto>? FichaProyectoSolicitada;

 // Nuevo constructor: recibe los filtros (opción B)
 public MapaViewModel(IProyectoRepository proyectos, IMunicipioRepository municipios, FiltrosViewModel filtros)
 {
 _proyectos = proyectos;
 _municipios = municipios;
 Filtros = filtros;

 HomeCommand = new RelayCommand(() => HomeRequested?.Invoke(this, EventArgs.Empty));

 SetupMap();
 }

 public async Task AbrirFichaProyectoAsync(int idProyecto)
 {
  try
  {
   var detalle = await _proyectos.ObtenerPorIdAsync(idProyecto);
   if (detalle != null)
    FichaProyectoSolicitada?.Invoke(this, detalle);
  }
  catch (Exception ex)
  {
   System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error cargando detalle de proyecto {idProyecto}: {ex}");
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

 if (Map != null)
 {
  if (_layerProyectos != null && Map.OperationalLayers.Contains(_layerProyectos))
  Map.OperationalLayers.Remove(_layerProyectos);
  if (_layerMunicipios != null && Map.OperationalLayers.Contains(_layerMunicipios))
  Map.OperationalLayers.Remove(_layerMunicipios);
 }
 _layerProyectos = null;
 _layerMunicipios = null;
 }

 /// <summary>
 /// Actualiza las capas del mapa con los proyectos filtrados.
 /// Llamar desde MapaView después de obtener los resultados filtrados.
 /// </summary>
 public async Task ActualizarCapasConFiltroAsync(IReadOnlyList<ProyectoDto> proyectosFiltrados)
 {
 if (Map == null) return;

 try
 {
  // 1. Recrear capa de proyectos con los datos filtrados
  if (_layerProyectos != null && Map.OperationalLayers.Contains(_layerProyectos))
  Map.OperationalLayers.Remove(_layerProyectos);

  _layerProyectos = await CrearCapaProyectosDesdeListaAsync(proyectosFiltrados);
  if (_layerProyectos != null)
  {
  Map.OperationalLayers.Add(_layerProyectos);
  await _layerProyectos.LoadAsync();
  }

  // 2. Obtener municipios de los proyectos filtrados
  var ids = proyectosFiltrados.Select(p => p.Id).ToList();
  var codigosMuni = ids.Count > 0
  ? await _proyectos.ObtenerCodigosMunicipioAsync(ids)
  : (IReadOnlyList<string>)Array.Empty<string>();
  _ultimosCodigosMunicipio = codigosMuni;

  // 3. Asegurar que el caché de geometrías esté poblado
  if (_cachedMunicipios == null)
  {
  var todosCodigosConProyecto = await _proyectos.ObtenerTodosCodigosMunicipioAsync();
  _cachedMunicipios = todosCodigosConProyecto.Count > 0
   ? await _municipios.PorCodigosGeoJsonAsync(todosCodigosConProyecto)
   : (IReadOnlyList<MunicipioGeoJsonDto>)Array.Empty<MunicipioGeoJsonDto>();
  }

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

  // 4. Recrear capa de municipios solo con los del filtro
  if (_layerMunicipios != null && Map.OperationalLayers.Contains(_layerMunicipios))
  Map.OperationalLayers.Remove(_layerMunicipios);
  _layerMunicipios = null;

  var filteredMuni = _cachedMunicipios.Where(m => codigosMuni.Contains(m.Codigo));
  _layerMunicipios = await CrearCapaMunicipiosFiltradaAsync(filteredMuni);
  if (_layerMunicipios != null)
  {
  Map.OperationalLayers.Insert(0, _layerMunicipios);
  await _layerMunicipios.LoadAsync();
  }
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
    xmin = Math.Min(xmin, ext.XMin);
    ymin = Math.Min(ymin, ext.YMin);
    xmax = Math.Max(xmax, ext.XMax);
    ymax = Math.Max(ymax, ext.YMax);
    any = true;
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
 private static Geometry? ParseGeoJson(string geoJson)
 {
  using var doc = JsonDocument.Parse(geoJson);
  var root = doc.RootElement;
  var type = root.GetProperty("type").GetString();
  var coordinates = root.GetProperty("coordinates");

  if (type is not ("Polygon" or "MultiPolygon")) return null;

  var builder = new PolygonBuilder(SpatialReferences.Wgs84);

  // MultiPolygon: [polygon, polygon, ...] where polygon = [ring, ring, ...]
  // Polygon: [ring, ring, ...] where ring = [[lon, lat], ...]
  IEnumerable<JsonElement> polygons = type == "MultiPolygon"
   ? coordinates.EnumerateArray()
   : new[] { coordinates };

  foreach (var polygon in polygons)
  {
   foreach (var ring in polygon.EnumerateArray())
   {
    var points = new List<MapPoint>();
    foreach (var point in ring.EnumerateArray())
    {
     var enumerator = point.EnumerateArray().GetEnumerator();
     enumerator.MoveNext(); double lon = enumerator.Current.GetDouble();
     enumerator.MoveNext(); double lat = enumerator.Current.GetDouble();
     points.Add(new MapPoint(lon, lat, SpatialReferences.Wgs84));
    }
    builder.AddPart(points);
   }
  }

  return builder.ToGeometry();
 }
 }
}
