using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Geomatica.Data.Repositories;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : UserControl
    {
        private ViewModels.MapaViewModel? _currentVm;
        private MapView? _attachedMapView;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _restoreVpCts;

        public MapaView()
        {
            InitializeComponent();

            DataContextChanged += MapaView_DataContextChanged;
            Unloaded += MapaView_Unloaded;
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
                    _restoreVpCts?.Cancel();
                    _restoreVpCts = new CancellationTokenSource();
                    _ = RestoreLastViewpointAsync(newVm, mv, _restoreVpCts.Token);

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

                // Load proyectos: solo si es la primera carga o si el caché fue invalidado.
                // Al volver al mapa (LastViewpoint != null) con capas ya cargadas, no hay necesidad
                // de re-consultar la BD ni recrear las capas.
                bool necesitaCargar = newVm.LastViewpoint == null || newVm.LayerProyectos == null;
                if (necesitaCargar)
                {
                    _loadCts?.Cancel();
                    _loadCts = new CancellationTokenSource();
                    _ = LoadProyectosAsync(newVm, _loadCts.Token, adjustViewpoint: newVm.LastViewpoint == null);
                }
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
                    _restoreVpCts?.Cancel();
                    _loadCts?.Cancel();
                    _loadCts = new CancellationTokenSource();
                    _ = LoadProyectosAsync(_currentVm, _loadCts.Token, adjustViewpoint: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en BuscarSolicitado: {ex}");
            }
        }

        private void MapaView_Unloaded(object? sender, RoutedEventArgs e)
        {
            _restoreVpCts?.Cancel();
            _loadCts?.Cancel();

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
                    _restoreVpCts?.Cancel();
                    _restoreVpCts = new CancellationTokenSource();
                    _ = RestoreLastViewpointAsync(vm, mv, _restoreVpCts.Token);

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
            _restoreVpCts?.Cancel();
            _loadCts?.Cancel();
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
        private async Task RestoreLastViewpointAsync(ViewModels.MapaViewModel vm, MapView mv, CancellationToken ct)
        {
            try
            {
                if (vm.LastViewpoint == null) return;

                // Esperar a que el Map esté asignado
                if (mv.Map == null)
                {
                    await Task.Delay(100);
                    if (ct.IsCancellationRequested) return;
                }

                if (mv.Map == null) return;

                if (mv.Map.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                {
                    try { await mv.Map.LoadAsync(); } catch { }
                }

                if (ct.IsCancellationRequested) return;

                // Aplicar viewpoint directamente (las capas ya están cargadas en el Map del singleton)
                await mv.SetViewpointAsync(vm.LastViewpoint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en RestoreLastViewpointAsync: {ex}");
            }
        }

        private async void Filtros_ProyectoSeleccionadoEnMapa(object? sender, ViewModels.FiltrosViewModel.ProyectoItem proyecto)
        {
            _restoreVpCts?.Cancel();
            _loadCts?.Cancel();
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

        private static long? BuscarOidEnResultado(IdentifyLayerResult result)
        {
            foreach (var element in result.GeoElements)
            {
                if (element.Attributes.TryGetValue("oid", out var val) && val != null)
                {
                    if (val is long l) return l;
                    if (val is int i) return i;
                    if (long.TryParse(val.ToString(), out var parsed)) return parsed;
                }
            }
            foreach (var subResult in result.SublayerResults)
            {
                var oid = BuscarOidEnResultado(subResult);
                if (oid != null) return oid;
            }
            return null;
        }

        private async void ControlMapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            try
            {
                var mv = sender as MapView;
                if (mv == null || _currentVm == null) return;

                int? idProyecto = null;
                var layerProyectos = _currentVm.LayerProyectos;
                if (layerProyectos != null)
                {
                    var result = await mv.IdentifyLayerAsync(layerProyectos, e.Position, 5, false);

                    var oid = BuscarOidEnResultado(result);
                    if (oid != null)
                        idProyecto = _currentVm.BuscarIdProyectoPorOid(oid.Value);
                }

                if (idProyecto != null)
                {
                    mv.DismissCallout();
                    await _currentVm.AbrirFichaProyectoAsync(idProyecto.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en GeoViewTapped: {ex}");
            }
        }

        private async Task LoadProyectosAsync(ViewModels.MapaViewModel vm, CancellationToken ct = default, bool adjustViewpoint = true)
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

                if (adjustViewpoint)
                {
                    // Cancelar restauración de viewpoint pendiente antes de establecer uno nuevo
                    _restoreVpCts?.Cancel();

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
                        // No auto-zoom a "home" al simplemente realizar búsquedas por texto o fechas.
                        // El usuario puede usar el botón 🏠 manualmente si desea regresar a la vista inicial.
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
