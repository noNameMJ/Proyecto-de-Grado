using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Data.Repositories;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
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
 private IReadOnlyList<MunicipioGeoJsonDto>? _cachedMunicipios;
 private Dictionary<string, Geometry>? _cachedGeometries;

 // Track the MapView that currently displays this Map to release ownership when re-attaching
 private MapView? _ownerMapView;

 // Comando y evento para Home (MVVM)
 public IRelayCommand HomeCommand { get; }
 public event EventHandler? HomeRequested;

 // Nuevo constructor: recibe los filtros (opción B)
 public MapaViewModel(IProyectoRepository proyectos, IMunicipioRepository municipios, FiltrosViewModel filtros)
 {
 _proyectos = proyectos;
 _municipios = municipios;
 Filtros = filtros;

 HomeCommand = new RelayCommand(() => HomeRequested?.Invoke(this, EventArgs.Empty));

 // Suscribirse a cambios en filtros para actualizar capas
 Filtros.PropertyChanged += OnFiltrosPropertyChanged;

 SetupMap();
 _ = CargarCapasAsync();
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

 private async Task CargarCapasAsync()
 {
 try
 {
 // Create layers only once and reuse them to avoid ownership errors
 if (_layerProyectos == null)
 _layerProyectos = await CrearCapaProyectosAsync();

 // Add layers only if they're not already in the map to avoid ownership errors
 if (Map != null)
 {
 if (_layerProyectos != null && !Map.OperationalLayers.Contains(_layerProyectos))
 Map.OperationalLayers.Add(_layerProyectos);
 }

 if (_layerProyectos != null)
 await _layerProyectos.LoadAsync();

 // Aplicar filtro a municipios (crea la capa filtrada)
 await AplicarFiltroMunicipiosAsync();
 }
 catch (Exception ex)
 {
 System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error en CargarCapasAsync: {ex}");
 }
 }

 public async Task CargarResultadosAsync()
 {
 try
 {
 var items = await _proyectos.ListarAsync();
 // Ensure UI thread update
 await Application.Current.Dispatcher.InvokeAsync(() =>
 {
 Filtros.ResultadosLista.Clear();
 Filtros.ResultadosResumen.Clear();
 foreach (var p in items)
 {
 Filtros.ResultadosLista.Add(new FiltrosViewModel.ProyectoItem(p.Id, p.Titulo, p.Lon, p.Lat, p.RutaArchivos));
 }
 Filtros.ResultadosResumen.Add($"{items.Count} proyectos");
 foreach (var p in items.Take(5)) Filtros.ResultadosResumen.Add(p.Titulo);
 });
 }
 catch (Exception ex)
 {
 System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error cargando resultados: {ex}");
 }
 }

 private async Task<Layer> CrearCapaProyectosAsync()
 {
 // Campos
 var fields = new List<Field>
 {
 OID("oid"),
 Int("id_proyecto"),
 Str("titulo",200),
 Str("ruta_archivos",1024)
 };
 var table = new FeatureCollectionTable(fields, GeometryType.Point, SpatialReferences.Wgs84);


 // Poblar con datos
 var items = await _proyectos.ListarAsync();
 var features = new List<Feature>();
 int oid =1;
 foreach (var p in items)
 {
  var attrs = new Dictionary<string, object?>
  {
  ["oid"] = oid++,
  ["id_proyecto"] = p.Id,
  ["titulo"] = p.Titulo,
  ["ruta_archivos"] = p.RutaArchivos
  };
  var geom = new MapPoint(p.Lon, p.Lat, SpatialReferences.Wgs84);
  features.Add(table.CreateFeature(attrs, geom));
 }
 if (features.Count > 0)
  await table.AddFeaturesAsync(features);

 // Renderer en la tabla
 table.Renderer = new SimpleRenderer(
 new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.OrangeRed, 9));

 var collection = new FeatureCollection(new[] { table });
 return new FeatureCollectionLayer(collection);
 }
 private async Task<Layer> CrearCapaMunicipiosAsync()
 {
 var fields = new List<Field>
 {
 OID("oid"),
 Str("mpio_cdpmp",5),
 Str("mpio_cnmbr",200)
 };
 var table = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);

 var muni = await _municipios.TodosGeoJsonAsync();

 // Parsear en hilo de fondo
 var parsed = await Task.Run(() =>
 {
 var result = new List<(string Codigo, string Nombre, Geometry Geom)>();
 foreach (var m in muni)
 {
  if (string.IsNullOrEmpty(m.GeoJson)) continue;
  try
  {
  var geom = ParseGeoJson(m.GeoJson);
  if (geom != null) result.Add((m.Codigo, m.Nombre, geom));
  }
  catch (Exception ex)
  {
  System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error parsing GeoJson for {m.Codigo}: {ex}");
  }
 }
 return result;
 });

 var features = new List<Feature>();
 int oid = 1;
 foreach (var (codigo, nombre, geom) in parsed)
 {
 var attrs = new Dictionary<string, object?>
 {
  ["oid"] = oid++,
  ["mpio_cdpmp"] = codigo,
  ["mpio_cnmbr"] = nombre
 };
 features.Add(table.CreateFeature(attrs, geom));
 }
 if (features.Count > 0)
 await table.AddFeaturesAsync(features);

 table.Renderer = new SimpleRenderer(
 new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
  System.Drawing.Color.FromArgb(40, 33, 150, 243),
  new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
   System.Drawing.Color.FromArgb(180, 33, 150, 243), 1.5f)));

 var collection = new FeatureCollection(new[] { table });
 return new FeatureCollectionLayer(collection);
 }

 // Hook opcional para aplicar filtros sobre el mapa
 private void AplicarFiltrosEnMapa()
 {
 // TODO: usar Filtros?.PalabraClave / Desde / Hasta / AreaInteres
 // para construir consultas o DefinitionExpression en capas.
 }

 /// <summary>
 /// Invalida el caché de municipios y la capa de proyectos para reflejar datos nuevos.
 /// Llamar después de crear/eliminar un proyecto.
 /// </summary>
 public async Task InvalidarCacheYRecargarAsync()
 {
 _cachedMunicipios = null;
 _cachedGeometries = null;

 // Recrear capa de proyectos
 if (Map != null && _layerProyectos != null && Map.OperationalLayers.Contains(_layerProyectos))
  Map.OperationalLayers.Remove(_layerProyectos);
 _layerProyectos = null;

 await CargarCapasAsync();
 }

 private async void OnFiltrosPropertyChanged(object? sender, PropertyChangedEventArgs e)
 {
 if (e.PropertyName == nameof(FiltrosViewModel.SelectedProyecto))
 {
 await AplicarFiltroMunicipiosAsync();
 }
 }

 private async Task AplicarFiltroMunicipiosAsync()
 {
 if (Map == null) return;

 try
 {
 // Remover capa actual si existe
 if (_layerMunicipios != null && Map.OperationalLayers.Contains(_layerMunicipios))
 {
 Map.OperationalLayers.Remove(_layerMunicipios);
 _layerMunicipios = null;
 }

 // Cargar solo municipios que tienen al menos un proyecto (cachear)
 if (_cachedMunicipios == null)
 {
  var codigosConProyecto = await _proyectos.ObtenerTodosCodigosMunicipioAsync();
  if (codigosConProyecto.Count > 0)
  _cachedMunicipios = await _municipios.PorCodigosGeoJsonAsync(codigosConProyecto);
  else
  _cachedMunicipios = Array.Empty<MunicipioGeoJsonDto>();
 }
 var allMuni = _cachedMunicipios;

 // Pre-parsear geometrías en hilo de fondo (una sola vez)
 if (_cachedGeometries == null)
 {
  var munis = allMuni;
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
   catch (Exception ex)
   {
   System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error parsing GeoJson for {m.Codigo}: {ex}");
   }
  }
  return dict;
  });
 }

 IEnumerable<MunicipioGeoJsonDto> filteredMuni;
 if (Filtros.SelectedProyecto != null)
 {
 var codigos = await _proyectos.ObtenerCodigosMunicipioAsync(new[] { Filtros.SelectedProyecto.Id });
 filteredMuni = allMuni.Where(m => codigos.Contains(m.Codigo));
 System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Filtrando municipios para proyecto {Filtros.SelectedProyecto.Id}: {string.Join(",", codigos)}");
 }
 else
 {
 filteredMuni = allMuni;
 System.Diagnostics.Debug.WriteLine("[MapaViewModel] Mostrando municipios con proyectos");
 }

 // Crear nueva capa usando geometrías ya parseadas del caché
 _layerMunicipios = await CrearCapaMunicipiosFiltradaAsync(filteredMuni);

 // Agregar a mapa
 if (_layerMunicipios != null)
 {
 Map.OperationalLayers.Insert(0, _layerMunicipios);
 await _layerMunicipios.LoadAsync();
 }
 }
 catch (Exception ex)
 {
 System.Diagnostics.Debug.WriteLine($"[MapaViewModel] Error aplicando filtro municipios: {ex}");
 }
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
