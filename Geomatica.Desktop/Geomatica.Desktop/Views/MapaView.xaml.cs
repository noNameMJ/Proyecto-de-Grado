using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Geomatica.Data.Repositories;
using System.Threading.Tasks;
using System.Linq;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : UserControl
    {
        private ViewModels.MapaViewModel? _currentVm;
        private MapView? _attachedMapView;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _hoverCts;
        private string? _lastHoverTitle;

        public MapaView()
        {
            InitializeComponent();

            DataContextChanged += MapaView_DataContextChanged;
            Unloaded += MapaView_Unloaded;
            controlMapView.MouseMove += ControlMapView_MouseMove;
            controlMapView.MouseLeave += ControlMapView_MouseLeave;
            controlMapView.GeoViewTapped += ControlMapView_GeoViewTapped;
        }

        private void MapaView_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            var mv = this.FindName("controlMapView") as MapView;

            if (e.OldValue is ViewModels.MapaViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.HomeRequested -= Vm_HomeRequested;
                if (oldVm.Filtros != null)
                {
                    oldVm.Filtros.BuscarSolicitado -= Filtros_BuscarSolicitado;
                    oldVm.Filtros.ProyectoSeleccionadoEnMapa -= Filtros_ProyectoSeleccionadoEnMapa;
                }

                // Save current viewpoint if possible
                if (mv != null && mv.Map == oldVm.Map)
                {
                    try
                    {
                        oldVm.LastViewpoint = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] No se pudo obtener viewpoint anterior: {ex}");
                    }

                    // Do not detach here; Unloaded will call DetachMapView to release ownership.
                }

                // Unsubscribe viewpoint changed from previous attached mapview
                if (_attachedMapView != null)
                {
                    try { _attachedMapView.ViewpointChanged -= AttachedMapView_ViewpointChanged; } catch { }
                    _attachedMapView = null;
                }
            }

            if (e.NewValue is ViewModels.MapaViewModel newVm)
            {
                _currentVm = newVm;
                newVm.PropertyChanged += Vm_PropertyChanged;
                newVm.HomeRequested += Vm_HomeRequested;
                if (newVm.Filtros != null)
                {
                    newVm.Filtros.BuscarSolicitado += Filtros_BuscarSolicitado;
                    newVm.Filtros.ProyectoSeleccionadoEnMapa += Filtros_ProyectoSeleccionadoEnMapa;
                }

                // Use the ViewModel to attach the MapView safely (it will detach any previous owner)
                if (mv != null && newVm.Map != null)
                {
                    try
                    {
                        newVm.AttachMapView(mv);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] Error al AttachMapView: {ex}");
                    }

                    // After attaching, restore viewpoint once the Map/layers are ready
                    _ = RestoreLastViewpointAsync(newVm, mv);

                    // Subscribe to ViewpointChanged to persist viewpoint continuously
                    try
                    {
                        if (_attachedMapView != mv)
                        {
                            if (_attachedMapView != null) _attachedMapView.ViewpointChanged -= AttachedMapView_ViewpointChanged;
                            _attachedMapView = mv;
                            _attachedMapView.ViewpointChanged += AttachedMapView_ViewpointChanged;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] Error suscribiendo ViewpointChanged: {ex}");
                    }
                }

                // Load proyectos into filtros results so user can see them
                _ = LoadProyectosAsync(newVm);
            }
        }

        private void AttachedMapView_ViewpointChanged(object? sender, System.EventArgs e)
        {
            try
            {
                var mv = sender as MapView;
                if (mv == null) return;
                if (DataContext is ViewModels.MapaViewModel vm)
                {
                    try
                    {
                        vm.LastViewpoint = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void Filtros_BuscarSolicitado(object? sender, System.EventArgs e)
        {
            try
            {
                if (_currentVm != null)
                {
                    _loadCts?.Cancel();
                    _loadCts = new CancellationTokenSource();
                    _ = LoadProyectosAsync(_currentVm, _loadCts.Token);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en BuscarSolicitado: {ex}");
            }
        }

        private void MapaView_Unloaded(object? sender, RoutedEventArgs e)
        {
            var mv = this.FindName("controlMapView") as MapView;

            if (DataContext is ViewModels.MapaViewModel vm)
            {
                if (vm.Filtros != null)
                {
                    vm.Filtros.BuscarSolicitado -= Filtros_BuscarSolicitado;
                    vm.Filtros.ProyectoSeleccionadoEnMapa -= Filtros_ProyectoSeleccionadoEnMapa;
                }

                if (mv != null && vm.Map != null)
                {
                    try
                    {
                        vm.LastViewpoint = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] No se pudo obtener viewpoint en Unloaded: {ex}");
                    }

                    // Let the ViewModel detach ownership of this MapView
                    try
                    {
                        vm.DetachMapView(mv);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] Error al DetachMapView: {ex}");
                        // As a fallback, clear the Map reference on the MapView
                        try { mv.Map = null; } catch { }
                    }
                }

                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.HomeRequested -= Vm_HomeRequested;
            }

            // Unsubscribe viewpoint changed
            if (_attachedMapView != null)
            {
                try { _attachedMapView.ViewpointChanged -= AttachedMapView_ViewpointChanged; } catch { }
                _attachedMapView = null;
            }

            // Limpiar estado de hover tooltip
            _hoverCts?.Cancel();
            _lastHoverTitle = null;

            _currentVm = null;
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MapaViewModel.Map) && sender is ViewModels.MapaViewModel vm)
            {
                Dispatcher.InvokeAsync(async () =>
                {
                    var mv = this.FindName("controlMapView") as MapView;
                    if (mv == null) return;

                    // Ask the ViewModel to attach the MapView (will handle detaching previous owner)
                    try
                    {
                        vm.AttachMapView(mv);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] Error en Vm_PropertyChanged AttachMapView: {ex}");
                    }

                    // Restore viewpoint after attaching
                    _ = RestoreLastViewpointAsync(vm, mv);

                    // subscribe viewpoint changed
                    try
                    {
                        if (_attachedMapView != mv)
                        {
                            if (_attachedMapView != null) _attachedMapView.ViewpointChanged -= AttachedMapView_ViewpointChanged;
                            _attachedMapView = mv;
                            _attachedMapView.ViewpointChanged += AttachedMapView_ViewpointChanged;
                        }
                    }
                    catch { }
                });
            }
        }

        private async void Vm_HomeRequested(object? sender, System.EventArgs e)
        {
            var mv = this.FindName("controlMapView") as MapView;
            if (sender is ViewModels.MapaViewModel vm && mv != null && vm.Map != null)
            {
                try
                {
                    var initial = vm.Map.InitialViewpoint;
                    if (initial != null)
                        await mv.SetViewpointAsync(initial);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MapaView] Error al ejecutar HomeRequested: {ex}");
                }
            }
        }

        // Wait for map and layers to be ready, then restore LastViewpoint
        private async Task RestoreLastViewpointAsync(ViewModels.MapaViewModel vm, MapView mv)
        {
            try
            {
                if (vm.LastViewpoint == null) return;

                // Wait until Map is assigned and loaded
                if (mv.Map == null)
                {
                    // small delay to allow attach to complete
                    await Task.Delay(100);
                }

                if (mv.Map != null && mv.Map.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                {
                    try { await mv.Map.LoadAsync(); } catch { }
                }

                // Also wait for layers to be loaded (if any)
                if (mv.Map?.OperationalLayers != null)
                {
                    foreach (var lyr in mv.Map.OperationalLayers.OfType<Esri.ArcGISRuntime.Mapping.Layer>())
                    {
                        try
                        {
                            if (lyr.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                                await lyr.LoadAsync();
                        }
                        catch { }
                    }
                }

                // Apply viewpoint once
                try
                {
                    await mv.SetViewpointAsync(vm.LastViewpoint);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MapaView] Error aplicando LastViewpoint: {ex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en RestoreLastViewpointAsync: {ex}");
            }
        }

        private async void Filtros_ProyectoSeleccionadoEnMapa(object? sender, ViewModels.FiltrosViewModel.ProyectoItem proyecto)
        {
            try
            {
                var mv = this.FindName("controlMapView") as MapView;
                if (mv == null) return;

                var punto = new Esri.ArcGISRuntime.Geometry.MapPoint(
                    proyecto.Lon, proyecto.Lat,
                    Esri.ArcGISRuntime.Geometry.SpatialReferences.Wgs84);

                await mv.SetViewpointCenterAsync(punto, 25_000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en zoom a proyecto seleccionado: {ex}");
            }
        }

        private async Task ZoomAResultadosAsync(IReadOnlyList<Geomatica.Data.Repositories.ProyectoDto> items, CancellationToken ct)
        {
            try
            {
                var mv = this.FindName("controlMapView") as MapView;
                if (mv == null) return;

                // Filtrar puntos con coordenadas válidas
                var puntos = items
                    .Where(p => !(p.Lon == 0 && p.Lat == 0))
                    .Select(p => new Esri.ArcGISRuntime.Geometry.MapPoint(p.Lon, p.Lat, Esri.ArcGISRuntime.Geometry.SpatialReferences.Wgs84))
                    .ToList();

                if (puntos.Count == 0) return;
                if (ct.IsCancellationRequested) return;

                if (puntos.Count == 1)
                {
                    // Un solo punto: centrar con escala a nivel de zona/barrio
                    await mv.SetViewpointCenterAsync(puntos[0], 25_000);
                }
                else
                {
                    // Múltiples puntos: calcular envelope y zoom con padding
                    var builder = new Esri.ArcGISRuntime.Geometry.EnvelopeBuilder(Esri.ArcGISRuntime.Geometry.SpatialReferences.Wgs84);
                    foreach (var p in puntos)
                        builder.UnionOf(p);

                    await mv.SetViewpointGeometryAsync(builder.ToGeometry(), 40);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en ZoomAResultadosAsync: {ex}");
            }
        }

        private async void ControlMapView_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var mv = sender as MapView;
            if (mv == null) return;

            _hoverCts?.Cancel();
            _hoverCts = new CancellationTokenSource();
            var ct = _hoverCts.Token;

            try
            {
                await Task.Delay(150, ct);
                if (ct.IsCancellationRequested) return;

                var screenPoint = e.GetPosition(mv);
                var results = await mv.IdentifyLayersAsync(screenPoint, 12, false, 1);
                if (ct.IsCancellationRequested) return;

                // Log solo una vez cuando hay resultados (para no spammear)
                if (results.Count > 0)
                {
                    System.Diagnostics.Trace.WriteLine($"[DIAG-HOVER] identify returned {results.Count} result(s) at {screenPoint}");
                    foreach (var r in results)
                        System.Diagnostics.Trace.WriteLine($"[DIAG-HOVER]   Layer='{r.LayerContent.Name}' GeoElements={r.GeoElements.Count} SubResults={r.SublayerResults.Count}");
                }

                string? titulo = null;
                foreach (var result in results)
                {
                    titulo = BuscarTituloEnResultado(result);
                    if (titulo != null) break;
                }

                if (ct.IsCancellationRequested) return;

                if (titulo != null)
                {
                    if (titulo != _lastHoverTitle)
                    {
                        _lastHoverTitle = titulo;
                        var mapLocation = mv.ScreenToLocation(screenPoint);
                        if (mapLocation != null)
                            mv.ShowCalloutAt(mapLocation, new CalloutDefinition(titulo));
                    }
                }
                else if (_lastHoverTitle != null)
                {
                    _lastHoverTitle = null;
                    mv.DismissCallout();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en hover tooltip: {ex}");
            }
        }

        private void ControlMapView_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _hoverCts?.Cancel();
            _lastHoverTitle = null;
            (sender as MapView)?.DismissCallout();
        }

        private static string? BuscarTituloEnResultado(IdentifyLayerResult result)
        {
            foreach (var element in result.GeoElements)
            {
                if (element.Attributes.TryGetValue("titulo", out var val) && val != null)
                    return val.ToString();
            }
            foreach (var subResult in result.SublayerResults)
            {
                var titulo = BuscarTituloEnResultado(subResult);
                if (titulo != null) return titulo;
            }
            return null;
        }

        private static int? BuscarIdProyectoEnResultado(IdentifyLayerResult result)
        {
            foreach (var element in result.GeoElements)
            {
                if (element.Attributes.TryGetValue("id_proyecto", out var val) && val != null)
                {
                    if (val is int i) return i;
                    if (val is long l) return (int)l;
                    if (int.TryParse(val.ToString(), out var parsed)) return parsed;
                }
            }
            foreach (var subResult in result.SublayerResults)
            {
                var id = BuscarIdProyectoEnResultado(subResult);
                if (id != null) return id;
            }
            return null;
        }

        private async void ControlMapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            try
            {
                var mv = sender as MapView;
                if (mv == null || _currentVm == null) return;

                // ── Diagnóstico: estado de capas ──
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] ══════════════════════════════════════");
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] Position: {e.Position}, Location: {e.Location}");
                if (mv.Map != null)
                {
                    System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] OperationalLayers count: {mv.Map.OperationalLayers.Count}");
                    foreach (var lyr in mv.Map.OperationalLayers)
                    {
                        System.Diagnostics.Trace.WriteLine($"[DIAG-TAP]   Layer: '{lyr.Name}' Type={lyr.GetType().Name} LoadStatus={lyr.LoadStatus} IsIdentifyEnabled={lyr.IsIdentifyEnabled} IsVisible={lyr.IsVisible}");
                        if (lyr is Esri.ArcGISRuntime.Mapping.FeatureCollectionLayer fcl)
                        {
                            foreach (var sub in fcl.Layers)
                            {
                                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP]     SubLayer: '{sub.Name}' Type={sub.GetType().Name} LoadStatus={sub.LoadStatus} IsIdentifyEnabled={sub.IsIdentifyEnabled} IsVisible={sub.IsVisible} FeatureCount(table)={sub.FeatureTable?.NumberOfFeatures}");
                            }
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] mv.Map is NULL!");
                }

                // ── Identify ──
                var results = await mv.IdentifyLayersAsync(e.Position, 15, false);
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] IdentifyLayersAsync returned {results.Count} result(s)");

                int? idProyecto = null;
                foreach (var result in results)
                {
                    DiagDumpIdentifyResult(result, indent: 1);
                    idProyecto ??= BuscarIdProyectoEnResultado(result);
                }

                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] idProyecto encontrado: {idProyecto?.ToString() ?? "NULL"}");
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] ══════════════════════════════════════");

                if (idProyecto != null)
                {
                    _hoverCts?.Cancel();
                    _lastHoverTitle = null;
                    mv.DismissCallout();

                    await _currentVm.AbrirFichaProyectoAsync(idProyecto.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] EXCEPTION: {ex}");
            }
        }

        private static void DiagDumpIdentifyResult(IdentifyLayerResult result, int indent)
        {
            var pad = new string(' ', indent * 2);
            System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] {pad}LayerContent: '{result.LayerContent.Name}' Type={result.LayerContent.GetType().Name}");
            System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] {pad}GeoElements: {result.GeoElements.Count}, SublayerResults: {result.SublayerResults.Count}, Error: {result.Error?.Message ?? "none"}");
            foreach (var el in result.GeoElements)
            {
                System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] {pad}  GeoElement Type={el.GetType().Name} Geometry={el.Geometry?.GeometryType}");
                foreach (var kvp in el.Attributes)
                {
                    System.Diagnostics.Trace.WriteLine($"[DIAG-TAP] {pad}    Attr '{kvp.Key}' = {kvp.Value} (type={kvp.Value?.GetType().Name ?? "null"})");
                }
            }
            foreach (var sub in result.SublayerResults)
            {
                DiagDumpIdentifyResult(sub, indent + 1);
            }
        }

        private async Task LoadProyectosAsync(ViewModels.MapaViewModel vm, CancellationToken ct = default)
        {
            try
            {
                IServiceProvider? provider = null;
                if (Application.Current.Properties.Contains("ServiceProvider"))
                    provider = Application.Current.Properties["ServiceProvider"] as IServiceProvider;

                if (provider == null)
                {
                    Debug.WriteLine("[MapaView] No se encontró ServiceProvider para cargar proyectos.");
                    return;
                }

                var repo = provider.GetService<IProyectoRepository>();
                var muniRepo = provider.GetService<IMunicipioRepository>();
                if (repo == null || muniRepo == null)
                {
                    Debug.WriteLine("[MapaView] IProyectoRepository o IMunicipioRepository no está registrado en DI.");
                    return;
                }

                IReadOnlyList<Geomatica.Data.Repositories.ProyectoDto> items;


                // Check for Municipio selection first (ignore sentinel "— Todos —")
                if (vm.Filtros.AreaInteres is ViewModels.FiltrosViewModel.MunicipioItem muni
                    && !string.IsNullOrEmpty(muni.Codigo))
                {
                    items = await repo.ListarPorMunicipioAsync(muni.Codigo, vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave);
                }
                // Then check for Department selection (ignore sentinel "— Todos —")
                else if (vm.Filtros.SelectedDepartamento is ViewModels.FiltrosViewModel.DepartamentoItem dept
                         && !string.IsNullOrEmpty(dept.Codigo))
                {
                    items = await repo.ListarPorDepartamentoAsync(dept.Codigo, vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave);
                }
                else
                {
                    // No geo filter: list all projects
                    items = await repo.ListarAsync(vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave, null);
                }

                // Si llegó una búsqueda más reciente, descartar estos resultados
                if (ct.IsCancellationRequested) return;

                // Actualizar capas del mapa con los proyectos filtrados
                await vm.ActualizarCapasConFiltroAsync(items);

                if (ct.IsCancellationRequested) return;

                // Zoom al extent de los municipios con proyectos si hay filtro geográfico activo
                bool tieneFiltroDepartamento = vm.Filtros.SelectedDepartamento != null
                    && !string.IsNullOrEmpty(vm.Filtros.SelectedDepartamento.Codigo);
                bool tieneFilterMunicipio = vm.Filtros.AreaInteres is ViewModels.FiltrosViewModel.MunicipioItem muniSel
                    && !string.IsNullOrEmpty(muniSel.Codigo);

                if (items.Count > 0 && (tieneFiltroDepartamento || tieneFilterMunicipio))
                {
                    var extent = vm.ObtenerExtentMunicipiosFiltrados();
                    if (extent != null)
                    {
                        var mvZoom = this.FindName("controlMapView") as MapView;
                        if (mvZoom != null && !ct.IsCancellationRequested)
                        {
                            await mvZoom.SetViewpointGeometryAsync(extent, 40);
                        }
                    }
                }
                else if (!tieneFiltroDepartamento && !tieneFilterMunicipio)
                {
                    // Sin filtro geográfico (Todos o Limpiar): volver a vista inicial
                    var mvHome = this.FindName("controlMapView") as MapView;
                    if (mvHome != null && vm.Map?.InitialViewpoint != null && !ct.IsCancellationRequested)
                    {
                        await mvHome.SetViewpointAsync(vm.Map.InitialViewpoint);
                    }
                }

                // Update UI-bound collections on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;

                    vm.Filtros.ResultadosLista.Clear();
                    vm.Filtros.ResultadosResumen.Clear();

                    foreach (var p in items)
                    {
                        vm.Filtros.ResultadosLista.Add(new ViewModels.FiltrosViewModel.ProyectoItem(p.Id, p.Titulo, p.Lon, p.Lat, p.RutaArchivos));
                    }

                    vm.Filtros.ResultadosResumen.Add($"{items.Count} proyectos");
                    for (int i = 0; i < Math.Min(5, items.Count); i++)
                    {
                        vm.Filtros.ResultadosResumen.Add(items[i].Titulo);
                    }

                    if (items.Count == 0)
                    {
                        Debug.WriteLine("[MapaView] No se encontraron proyectos en la consulta.");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Query cancelada por un filtro más reciente, es esperado
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error cargando proyectos: {ex}");
            }
        }
    }
}
