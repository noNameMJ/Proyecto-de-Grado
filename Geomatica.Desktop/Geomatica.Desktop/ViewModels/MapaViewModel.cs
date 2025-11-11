using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Data.Repositories;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using System;

namespace Geomatica.Desktop.ViewModels
{
 public class MapaViewModel : INotifyPropertyChanged
 {
 private readonly IProyectoRepository _proyectos;
 private readonly IMunicipioRepository _municipios;

 // Referencia opcional a los filtros compartidos
 public FiltrosViewModel Filtros { get; }
 private FeatureLayer? _layerMunicipios;
 private FeatureLayer? _layerProyectos;

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

 SetupMap();
 _ = CargarCapasAsync();
 }

 // Constructor existente (compatibilidad): crea filtros locales
 public MapaViewModel(IProyectoRepository proyectos, IMunicipioRepository municipios)
 : this(proyectos, municipios, new FiltrosViewModel()) { }

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
 => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeOID\",\"alias\":\"{name}\"}}");

 private static Field Int(string name, string? alias = null)
 => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeInteger\",\"alias\":\"{alias ?? name}\"}}");

 private static Field Str(string name, int length, string? alias = null)
 => Field.FromJson($"{{\"name\":\"{name}\",\"type\":\"esriFieldTypeString\",\"alias\":\"{alias ?? name}\",\"length\":{length}}}");

 private async Task CargarCapasAsync()
 {
 _layerMunicipios = await CrearCapaMunicipiosAsync();
 _layerProyectos = await CrearCapaProyectosAsync();

 Map!.OperationalLayers.Add(_layerMunicipios);
 Map!.OperationalLayers.Add(_layerProyectos);

 await _layerMunicipios.LoadAsync();
 await _layerProyectos.LoadAsync();
 }
 private async Task<FeatureLayer> CrearCapaProyectosAsync()
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
 var feat = table.CreateFeature(attrs, geom);
 await table.AddFeatureAsync(feat);
 }

 // Capa con renderer
 var layer = new FeatureLayer(table)
 {
 Renderer = new SimpleRenderer(
 new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.OrangeRed,9))
 };
 return layer;
 }
 private async Task<FeatureLayer> CrearCapaMunicipiosAsync()
 {
 var fields = new List<Field>
 {
 OID("oid"),
 Str("mpio_cdpmp",5),
 Str("mpio_cnmbr",200)
 };
 var table = new FeatureCollectionTable(fields, GeometryType.Polygon, SpatialReferences.Wgs84);


 var muni = await _municipios.TodosGeoJsonAsync();
 int oid =1;
 foreach (var m in muni)
 {
 var geom = (Geometry)Geometry.FromJson(m.GeoJson);
 var attrs = new Dictionary<string, object?>
 {
 ["oid"] = oid++,
 ["mpio_cdpmp"] = m.Codigo,
 ["mpio_cnmbr"] = m.Nombre
 };
 var feat = table.CreateFeature(attrs, geom);
 await table.AddFeatureAsync(feat);
 }

 var layer = new FeatureLayer(table);
 layer.Renderer = new SimpleRenderer(
 new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
 System.Drawing.Color.FromArgb(40,33,150,243),
 new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
 System.Drawing.Color.FromArgb(180,33,150,243),1.5f)));
 return layer;
 }

 // Hook opcional para aplicar filtros sobre el mapa
 private void AplicarFiltrosEnMapa()
 {
 // TODO: usar Filtros?.PalabraClave / Desde / Hasta / AreaInteres
 // para construir consultas o DefinitionExpression en capas.
 }
 }
}
