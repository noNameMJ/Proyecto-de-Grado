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
using Geomatica.Desktop.Views;
using Geomatica.Desktop.Services;

namespace Geomatica.Desktop.ViewModels
{
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

        // Herramientas de Medición SIG (Desacopladas en MapaMedicionController)
        private readonly MapaMedicionController _medicionController = new();
        public GraphicsOverlay OverlayMedicion => _medicionController.OverlayMedicion;
        [ObservableProperty] private bool isHerramientasMedicionVisible;
        [ObservableProperty] private string modoMedicion = "Ninguno"; // "Ninguno", "Distancia", "Area"
        [ObservableProperty] private string resultadoMedicion = "";
        [ObservableProperty] private string detalleMedicion = "";
        [ObservableProperty] private bool hasResultadoMedicion;

        // Visualización de Coordenadas y Escala en Vivo
        [ObservableProperty] private string coordenadasCursorTexto = "Lat: -- | Lon: --";
        [ObservableProperty] private string escalaMapaTexto = "Escala: 1:--";

        // Elemento Identificado (Identify / Popup interactivo de capas de usuario y GDB)
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasElementoIdentificado))]
        private ElementoIdentificadoInfo? elementoIdentificado;

        public bool HasElementoIdentificado => ElementoIdentificado != null;

        [RelayCommand]
        private void CerrarElementoIdentificado()
        {
            ElementoIdentificado = null;
        }

        [RelayCommand]
        private async Task CentrarElementoIdentificadoAsync()
        {
            if (ElementoIdentificado?.Geometria != null)
            {
                await CentrarEnGeometriaAsync(ElementoIdentificado.Geometria);
            }
        }

        // Indicador de Progreso Determinista para Operaciones Asíncronas Pesadas (LiDAR, Rasters)
        [ObservableProperty] private bool isOperacionEnProgreso;
        [ObservableProperty] private double progresoPorcentaje;
        [ObservableProperty] private string progresoTitulo = "";
        [ObservableProperty] private string progresoDetalle = "";

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

        // Inyección de notificaciones y servicios de importación
        private readonly Geomatica.Desktop.Services.INotificationService? _notifications;
        private readonly Geomatica.Desktop.Services.IFileGdbImporterService _gdbImporter;
        private readonly Geomatica.Desktop.Services.ICadImporterService _cadImporter;
        public Geomatica.Desktop.Services.IFileGdbImporterService GdbImporter => _gdbImporter;
        public Geomatica.Desktop.Services.ICadImporterService CadImporter => _cadImporter;

        public MapaViewModel(
            BuscarProyectosUseCase buscarProyectos, 
            IProyectoRepository proyectos, 
            IMunicipioRepository municipios, 
            FiltrosViewModel filtros, 
            ArchivosViewModel archivosVM,
            Geomatica.Desktop.Services.INotificationService? notifications = null,
            Geomatica.Desktop.Services.IFileGdbImporterService? gdbImporter = null,
            Geomatica.Desktop.Services.ICadImporterService? cadImporter = null)
        {
            _buscarProyectos = buscarProyectos;
            _proyectos = proyectos;
            _municipios = municipios;
            Filtros = filtros;
            ArchivosVM = archivosVM;
            _notifications = notifications;
            _gdbImporter = gdbImporter ?? new Geomatica.Desktop.Services.FileGdbImporterService();
            _cadImporter = cadImporter ?? new Geomatica.Desktop.Services.CadImporterService();

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
            ArchivosVM.AbrirCapaEnMapaSolicitado += async (s, args) => await CargarCapaGdbAsync(args.RutaGdb, args.NombreCapa);
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
                    var validacionShp = ShapefileValidator.Validar(path);
                    if (!validacionShp.PuedeCargar)
                    {
                        _notifications?.ShowError(validacionShp.MensajeError ?? "El archivo Shapefile no es válido.", "Shapefile Incompleto");
                        return;
                    }
                    if (!string.IsNullOrEmpty(validacionShp.MensajeAdvertencia))
                    {
                        _notifications?.ShowWarning(validacionShp.MensajeAdvertencia, "Advertencia Shapefile");
                    }
                    var shapefile = await ShapefileFeatureTable.OpenAsync(path);
                    layer = new FeatureLayer(shapefile);
                }
                else if (ext == ".gpkg")
                {
                    await CargarGeoPackageAsync(path);
                    return;
                }
                else if (ext == ".kml" || ext == ".kmz")
                {
                    var dataset = new Esri.ArcGISRuntime.Ogc.KmlDataset(new Uri(path));
                    layer = new KmlLayer(dataset);
                }
                else if (ext == ".geodatabase")
                {
                    var gdb = await Geodatabase.OpenAsync(path);
                    var table = gdb.GeodatabaseFeatureTables.FirstOrDefault();
                    if (table != null) layer = new FeatureLayer(table);
                }
                else if (ext == ".gdb")
                {
                    await CargarFileGeodatabaseAsync(path);
                    return;
                }
                else if (ext == ".dwg" || ext == ".dxf")
                {
                    await CargarCadAsync(path);
                    return;
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
            if (layer is FeatureLayer fl && fl.FeatureTable != null)
            {
                item.AbrirTablaAtributosCommand = new AsyncRelayCommand(() => AbrirTablaAtributosAsync(item, fl.FeatureTable));
            }
            item.QuitarCommand = new RelayCommand(() => 
            {
                if (item.Capa != null)
                {
                    Map.OperationalLayers.Remove(item.Capa);
                    _rasterExtentsSeguros.Remove(item.Capa);
                }
                CapasAdicionales.Remove(item);
                item.Dispose();
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

    private async Task CargarFileGeodatabaseAsync(string path)
    {
        await CargarCapaGdbAsync(path, null);
    }

    public async Task CargarCapaGdbAsync(string path, string? nombreCapaEspecifica = null)
    {
        if (Map == null) return;
        try
        {
            string nombreGdb = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string tituloNotif = string.IsNullOrEmpty(nombreCapaEspecifica)
                ? $"Procesando Geodatabase '{nombreGdb}'..."
                : $"Cargando capa '{nombreCapaEspecifica}' de '{nombreGdb}'...";

            RasterDiagnostics.Log($"[MapaViewModel] Solicitando carga de GDB: {path}, Capa: {nombreCapaEspecifica ?? "(Todas)"}");
            _notifications?.ShowInfo(tituloNotif, "Cargando Geodatabase");

            var resultado = await _gdbImporter.ImportarGdbAsync(path);

            if (!resultado.Success || string.IsNullOrEmpty(resultado.GeoPackagePath))
            {
                _notifications?.ShowError(
                    resultado.MensajeError ?? "No se pudo procesar la File Geodatabase.",
                    "Error al cargar Geodatabase"
                );
                return;
            }

            await CargarGeoPackageAsync(resultado.GeoPackagePath, nombreOrigen: nombreGdb, rutaOriginalGdb: path, capaFiltro: nombreCapaEspecifica);

            if (!resultado.FromCache)
            {
                _notifications?.ShowSuccess($"Geodatabase '{nombreGdb}' cargada con éxito.", "Geodatabase Lista");
            }
        }
        catch (Exception ex)
        {
            RasterDiagnostics.Log($"[MapaViewModel] Error inesperado cargando File Geodatabase: {ex}");
            _notifications?.ShowError($"Error inesperado cargando Geodatabase: {ex.Message}", "Error");
        }
    }

    public async Task CargarCadAsync(string path)
    {
        if (Map == null) return;
        try
        {
            string nombreCad = Path.GetFileName(path);
            RasterDiagnostics.Log($"[MapaViewModel] Solicitando carga de CAD: {path}");
            _notifications?.ShowInfo($"Procesando plano CAD '{nombreCad}' ({_cadImporter.ProveedorActivo})...", "Cargando Plano CAD");

            var resultado = await _cadImporter.ImportarCadAsync(path);

            if (!resultado.Success || string.IsNullOrEmpty(resultado.GeoPackagePath))
            {
                _notifications?.ShowError(
                    resultado.MensajeError ?? "No se pudo procesar el plano CAD.",
                    "Error al cargar CAD"
                );
                return;
            }

            await CargarGeoPackageAsync(resultado.GeoPackagePath, nombreOrigen: nombreCad, rutaOriginalGdb: path);

            string origenTexto = resultado.FromCache ? "caché local" : resultado.ProveedorUtilizado;
            _notifications?.ShowSuccess(
                $"Plano CAD '{nombreCad}' cargado correctamente ({origenTexto}).",
                "Plano CAD Cargado"
            );
        }
        catch (Exception ex)
        {
            RasterDiagnostics.Log($"[MapaViewModel] Error inesperado cargando CAD: {ex}");
            _notifications?.ShowError($"Error inesperado cargando plano CAD: {ex.Message}", "Error CAD");
        }
    }

    private async Task CargarGeoPackageAsync(string path, string? nombreOrigen = null, string? rutaOriginalGdb = null, string? capaFiltro = null)
    {
        if (Map == null) return;
        try
        {
            RasterDiagnostics.Log($"Abriendo contenedor GeoPackage: {path}");
            var gpkg = await GeoPackage.OpenAsync(path);
            int capasCargadas = 0;
            Layer? primeraCapa = null;
            string nombreBase = !string.IsNullOrWhiteSpace(nombreOrigen) ? nombreOrigen : Path.GetFileNameWithoutExtension(path);
            string rutaMostrar = !string.IsNullOrWhiteSpace(rutaOriginalGdb) ? rutaOriginalGdb : path;
            bool esGdb = !string.IsNullOrWhiteSpace(rutaOriginalGdb) && rutaOriginalGdb.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase);
            bool esCad = !string.IsNullOrWhiteSpace(rutaOriginalGdb) && (rutaOriginalGdb.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) || rutaOriginalGdb.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase));

            // 1. Capas vectoriales (GeoPackageFeatureTables)
            foreach (var table in gpkg.GeoPackageFeatureTables)
            {
                if (!string.IsNullOrWhiteSpace(capaFiltro) && !table.TableName.Equals(capaFiltro, StringComparison.OrdinalIgnoreCase))
                    continue;

                var featureLayer = new FeatureLayer(table)
                {
                    Name = $"{nombreBase} - {table.TableName}",
                    ShowInLegend = false
                };

                // Asignar simbología mejorada de alta visibilidad según tipo de geometría
                if (table.GeometryType == GeometryType.Point || table.GeometryType == GeometryType.Multipoint)
                {
                    var markerSymbol = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.FromArgb(230, 0x1B, 0x5E, 0x20), // Verde esmeralda UIS
                        9.0)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1.2)
                    };
                    featureLayer.Renderer = new SimpleRenderer(markerSymbol);
                }
                else if (table.GeometryType == GeometryType.Polyline)
                {
                    var lineSymbol = new SimpleLineSymbol(
                        SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(230, 0x0D, 0x47, 0xA1), // Azul marino
                        2.5);
                    featureLayer.Renderer = new SimpleRenderer(lineSymbol);
                }
                else if (table.GeometryType == GeometryType.Polygon)
                {
                    var fillSymbol = new SimpleFillSymbol(
                        SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(90, 0x00, 0x79, 0x6B), // Turquesa translúcido
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(220, 0x00, 0x4D, 0x40), 1.5)
                    );
                    featureLayer.Renderer = new SimpleRenderer(fillSymbol);
                }

                await Application.Current.Dispatcher.InvokeAsync(() => Map.OperationalLayers.Add(featureLayer));
                await featureLayer.LoadAsync();

                long featureCount = 0;
                try
                {
                    featureCount = await table.QueryFeatureCountAsync(new QueryParameters());
                }
                catch { }

                string tipoGeometriaTexto = table.GeometryType switch
                {
                    GeometryType.Point or GeometryType.Multipoint => "Puntos",
                    GeometryType.Polyline => "Líneas",
                    GeometryType.Polygon => "Polígonos",
                    _ => table.GeometryType.ToString()
                };

                string tipoIcono = table.GeometryType switch
                {
                    GeometryType.Point or GeometryType.Multipoint => "📍",
                    GeometryType.Polyline => "📏",
                    GeometryType.Polygon => "⬡",
                    _ => esGdb ? "🗃️" : (esCad ? "📐" : "📦")
                };

                string tipoTexto = esGdb
                    ? $"Vectorial GDB ({tipoGeometriaTexto})"
                    : (esCad ? $"Plano CAD ({tipoGeometriaTexto})" : $"Vectorial GeoPackage ({tipoGeometriaTexto})");

                var itemCapa = new CapaUsuarioItem
                {
                    Nombre = featureLayer.Name,
                    NombreContenedor = nombreBase,
                    NombreCapaInterna = table.TableName,
                    TipoGeometria = tipoGeometriaTexto,
                    CantidadElementos = featureCount,
                    RutaCompleta = rutaMostrar,
                    TipoIcono = tipoIcono,
                    TipoTexto = tipoTexto,
                    Capa = featureLayer,
                    ContenedorGeoPackage = gpkg,
                    ExtentParaZoom = featureLayer.FullExtent,
                    IsVisible = true,
                    Opacidad = 1.0
                };

                itemCapa.AbrirTablaAtributosCommand = new AsyncRelayCommand(() => AbrirTablaAtributosAsync(itemCapa, table));

                itemCapa.QuitarCommand = new RelayCommand(() =>
                {
                    Map.OperationalLayers.Remove(featureLayer);
                    CapasAdicionales.Remove(itemCapa);
                    itemCapa.Dispose();
                    if (CapasAdicionales.Count == 0) IsPanelCapasVisible = false;
                });
                itemCapa.ZoomCommand = new RelayCommand(async () =>
                {
                    if (itemCapa.Capa != null) await ZoomCapaSeguraAsync(itemCapa.Capa, 50, "manual");
                });

                CapasAdicionales.Add(itemCapa);
                if (primeraCapa == null) primeraCapa = featureLayer;
                capasCargadas++;
            }

            // 2. Capas ráster (GeoPackageRasters)
            if (string.IsNullOrWhiteSpace(capaFiltro))
            {
                foreach (var raster in gpkg.GeoPackageRasters)
                {
                    var rasterLayer = new RasterLayer(raster)
                    {
                        Name = $"{nombreBase} - Ráster {capasCargadas + 1}",
                        ShowInLegend = false,
                        IsVisible = true,
                        Opacity = 1.0
                    };
                    await Application.Current.Dispatcher.InvokeAsync(() => Map.OperationalLayers.Add(rasterLayer));
                    await rasterLayer.LoadAsync();

                    var itemCapa = new CapaUsuarioItem
                    {
                        Nombre = rasterLayer.Name,
                        NombreContenedor = nombreBase,
                        NombreCapaInterna = rasterLayer.Name,
                        TipoGeometria = "Ráster",
                        CantidadElementos = 1,
                        RutaCompleta = rutaMostrar,
                        TipoIcono = esGdb ? "🗃️" : "📦",
                        TipoTexto = esGdb ? "Ráster File Geodatabase" : "Ráster GeoPackage",
                        Capa = rasterLayer,
                        ContenedorGeoPackage = gpkg,
                        ExtentParaZoom = rasterLayer.FullExtent,
                        IsVisible = true,
                        Opacidad = 1.0
                    };
                    itemCapa.QuitarCommand = new RelayCommand(() =>
                    {
                        Map.OperationalLayers.Remove(rasterLayer);
                        CapasAdicionales.Remove(itemCapa);
                        itemCapa.Dispose();
                        if (CapasAdicionales.Count == 0) IsPanelCapasVisible = false;
                    });
                    itemCapa.ZoomCommand = new RelayCommand(async () =>
                    {
                        if (itemCapa.Capa != null) await ZoomCapaSeguraAsync(itemCapa.Capa, 50, "manual");
                    });

                    CapasAdicionales.Add(itemCapa);
                    if (primeraCapa == null) primeraCapa = rasterLayer;
                    capasCargadas++;
                }
            }

            if (capasCargadas == 0)
            {
                gpkg.Close();
                _notifications?.ShowWarning($"No se encontraron capas para cargar en '{Path.GetFileName(path)}'.", "Contenedor");
                return;
            }

            IsPanelCapasVisible = true;
            IsSelectorMapasBaseVisible = false;
            IsHerramientasMedicionVisible = false;

            if (primeraCapa != null)
            {
                await Task.Delay(200);
                await ZoomCapaSeguraAsync(primeraCapa, 20, "geopackage inicial");
            }

            _notifications?.ShowSuccess($"Se cargó exitosamente ({capasCargadas} capa(s)).", "Capas Cargadas");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Error cargando GeoPackage: {path}", ex);
            _notifications?.ShowError($"Error al abrir GeoPackage: {ex.Message}", "Error GeoPackage");
        }
    }

    public async Task CentrarEnGeometriaAsync(Geometry geom)
    {
        if (_ownerMapView == null || geom == null) return;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (geom is MapPoint pt)
                {
                    await _ownerMapView.SetViewpointCenterAsync(pt, 5000);
                }
                else if (geom.Extent != null)
                {
                    var ext = geom.Extent;
                    if (ext.Width == 0 && ext.Height == 0)
                    {
                        var center = new MapPoint(ext.XMin, ext.YMin, ext.SpatialReference);
                        await _ownerMapView.SetViewpointCenterAsync(center, 5000);
                    }
                    else
                    {
                        await _ownerMapView.SetViewpointGeometryAsync(ext, 60);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            RasterDiagnostics.Log($"[MapaViewModel] Error centrando en geometría: {ex.Message}");
        }
    }

    private async Task AbrirTablaAtributosAsync(CapaUsuarioItem itemCapa, FeatureTable table)
    {
        try
        {
            _notifications?.ShowInfo($"Consultando registros de '{itemCapa.NombreCapaInterna}'...", "Tabla de Atributos");

            var query = new QueryParameters { WhereClause = "1=1" };
            var featureResult = await table.QueryFeaturesAsync(query);

            var dataTable = new System.Data.DataTable();
            var geometrias = new Dictionary<System.Data.DataRow, Geometry?>();

            // Crear columnas basadas en los campos de la tabla
            foreach (var field in table.Fields)
            {
                Type colType = field.FieldType switch
                {
                    FieldType.Int16 => typeof(short),
                    FieldType.Int32 => typeof(int),
                    FieldType.Int64 => typeof(long),
                    FieldType.Float32 => typeof(float),
                    FieldType.Float64 => typeof(double),
                    FieldType.Date => typeof(DateTime),
                    _ => typeof(string)
                };

                dataTable.Columns.Add(field.Name, Nullable.GetUnderlyingType(colType) ?? colType);
            }

            // Agregar filas
            foreach (var feature in featureResult)
            {
                var row = dataTable.NewRow();
                foreach (var field in table.Fields)
                {
                    if (feature.Attributes.TryGetValue(field.Name, out var val) && val != null)
                    {
                        try
                        {
                            row[field.Name] = Convert.ChangeType(val, dataTable.Columns[field.Name]!.DataType);
                        }
                        catch
                        {
                            row[field.Name] = val.ToString();
                        }
                    }
                    else
                    {
                        row[field.Name] = DBNull.Value;
                    }
                }
                dataTable.Rows.Add(row);
                geometrias[row] = feature.Geometry;
            }

            var win = new TablaAtributosView();
            win.Owner = Application.Current.MainWindow;
            win.CargarDatos(
                !string.IsNullOrWhiteSpace(itemCapa.NombreCapaInterna) ? itemCapa.NombreCapaInterna : itemCapa.Nombre,
                itemCapa.TipoIcono,
                dataTable,
                geometrias,
                geom =>
                {
                    if (geom != null)
                    {
                        _ = CentrarEnGeometriaAsync(geom);
                    }
                },
                rutaGdb: itemCapa.RutaCompleta,
                gdbImporter: _gdbImporter
            );
            win.Show();
        }
        catch (Exception ex)
        {
            RasterDiagnostics.Log($"[MapaViewModel] Error al abrir tabla de atributos: {ex}");
            _notifications?.ShowError($"No se pudo abrir la tabla de atributos: {ex.Message}", "Error");
        }
    }

    private async Task<Layer?> CrearRasterLayerValidadoAsync(string path)
    {
        IsOperacionEnProgreso = true;
        ProgresoPorcentaje = 0;
        ProgresoTitulo = $"Cargando ráster: {Path.GetFileName(path)}";
        ProgresoDetalle = "Verificando archivo y metadatos...";

        IProgress<(int porcentaje, string detalle)> progress = new Progress<(int porcentaje, string detalle)>(p =>
        {
            ProgresoPorcentaje = p.porcentaje;
            ProgresoDetalle = p.detalle;
        });

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

            // 0. Si el GeoTIFF posee canal alfa (RGBA, 4 bandas como ortomosaicos de Pix4D/Agisoft),
            // generar una vista VRT con NoData=0 para que el fondo/borde sea 100% transparente en el mapa.
            // Para GeoTIFFs sin canal alfa (1 o 3 bandas), NO se genera VRT, preservando el fondo negro
            // exactamente como fue solicitado ("para tiff sin canal alfa el fondo se vea negro cosa que es correcto").
            Raster? raster = null;
            string rasterPath = path;
            bool isDirectLoad = true;

            bool tieneAlfa = GeoTiffSidecarResolver.TieneCanalAlfa(path);
            if (tieneAlfa)
            {
                progress.Report((10, "Detectado canal alfa en GeoTIFF. Configurando transparencia de fondo..."));
                RasterDiagnostics.Log($"GeoTIFF has alpha channel. Building transparent alpha VRT for: {path}");
                var vrtAlfaPath = GeoTiffSidecarResolver.ObtenerOCrearVrtConAlfaTransparente(path, progress);
                if (!string.IsNullOrEmpty(vrtAlfaPath) && File.Exists(vrtAlfaPath))
                {
                    try
                    {
                        var vrtRaster = new Raster(vrtAlfaPath);
                        await vrtRaster.LoadAsync();
                        if (vrtRaster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded && vrtRaster.RasterInfo?.SpatialReference != null)
                        {
                            raster = vrtRaster;
                            rasterPath = vrtAlfaPath;
                            isDirectLoad = true;
                            progress.Report((70, "GeoTIFF cargado con transparencia de canal alfa activa."));
                            RasterDiagnostics.Log($"Transparent alpha VRT loaded successfully with SpatialReference={raster.RasterInfo.SpatialReference}");
                        }
                    }
                    catch (Exception ex)
                    {
                        RasterDiagnostics.LogException("Transparent alpha VRT Raster.LoadAsync exception", ex);
                    }
                }
            }

            // 1. Intentar carga directa del raster desde su ruta original (para TIFFs sin canal alfa o fallback)
            if (raster == null)
            {
                try
                {
                    progress.Report((15, "Intentando lectura directa del GeoTIFF con ArcGIS Runtime..."));
                    var directRaster = new Raster(path);
                    await directRaster.LoadAsync();
                    if (directRaster.LoadStatus == Esri.ArcGISRuntime.LoadStatus.Loaded && directRaster.RasterInfo?.SpatialReference != null)
                    {
                        raster = directRaster;
                        progress.Report((70, "GeoTIFF leído correctamente con referencia espacial embebida."));
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
            }

            // 2. Si la carga directa no obtuvo SpatialReference, intentar el fallback con sidecars (.prj + .tfw)
            if (raster == null)
            {
                RasterDiagnostics.Log($"Attempting sidecar resolution for: {path}");
                progress.Report((35, "Procesando sidecars y caché de georreferenciación..."));
                
                try
                {
                    await GeoTiffSidecarResolver.AsegurarAuxXmlGeorreferenciadoAsync(path, progress);
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
        finally
        {
            IsOperacionEnProgreso = false;
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

    private static readonly Camera CamColombia3D = new Camera(4.680486, -74.146592, 1_800_000.0, 0.0, 0.0, 0.0);
    private static readonly MapPoint CentroColombia = new MapPoint(-74.146592, 4.680486, SpatialReferences.Wgs84);

    private void SetupScene()
    {
        var basemapStyle = MapaBaseSeleccionado?.Style ?? BasemapStyle.ArcGISTopographic;
        var newScene = new Scene(basemapStyle);
        newScene.InitialViewpoint = new Viewpoint(CentroColombia, CamColombia3D);

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
        _ = PrecalentarEscena3DAsync(newScene);

        if (_ownerSceneView != null)
        {
            _ownerSceneView.Scene = newScene;
            _ = _ownerSceneView.SetViewpointCameraAsync(CamColombia3D);
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

    private async Task PrecalentarEscena3DAsync(Scene scene)
    {
        try
        {
            await scene.LoadAsync();
            if (scene.BaseSurface != null)
            {
                await scene.BaseSurface.LoadAsync();
                foreach (var src in scene.BaseSurface.ElevationSources)
                {
                    try { await src.LoadAsync(); } catch { }
                }
            }
            if (_ownerSceneView != null)
            {
                _ = _ownerSceneView.SetViewpointCameraAsync(CamColombia3D);
            }
            AppLogger.Info("[MapaViewModel] Escena 3D y superficie de elevación precargadas y listas en Colombia.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[MapaViewModel] Advertencia al precargar escena 3D: {ex.Message}");
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
            if (Capas3D.Count == 0)
            {
                _ = _ownerSceneView.SetViewpointCameraAsync(CamColombia3D);
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
            item.Dispose();
        }
        CapasAdicionales.Clear();
        Capas3D.Clear();
        Capa3DSeleccionada = null;
        _anclajeLocalActual3D = null;
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
        _medicionController.Limpiar();
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

        var res = _medicionController.ProcesarNuevoPunto(punto, ModoMedicion);
        ResultadoMedicion = res.Resultado;
        DetalleMedicion = res.Detalle;
        HasResultadoMedicion = res.HasResultado;
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

}
}
