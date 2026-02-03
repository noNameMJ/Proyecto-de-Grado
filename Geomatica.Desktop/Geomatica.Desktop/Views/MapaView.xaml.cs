using Esri.ArcGISRuntime.Mapping;
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

        public MapaView()
        {
            InitializeComponent();

            DataContextChanged += MapaView_DataContextChanged;
            Unloaded += MapaView_Unloaded;
        }

        private void MapaView_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            var mv = this.FindName("controlMapView") as MapView;

            if (e.OldValue is ViewModels.MapaViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.HomeRequested -= Vm_HomeRequested;
                if (oldVm.Filtros != null)
                    oldVm.Filtros.BuscarSolicitado -= Filtros_BuscarSolicitado;

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
                    newVm.Filtros.BuscarSolicitado += Filtros_BuscarSolicitado;

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
                    _ = LoadProyectosAsync(_currentVm);
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
                    vm.Filtros.BuscarSolicitado -= Filtros_BuscarSolicitado;

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

                // Apply viewpoint (retry a few times since layers may change rendering)
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await mv.SetViewpointAsync(vm.LastViewpoint);
                        // short delay to let view update
                        await Task.Delay(150);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] Intento {i} fallo aplicando LastViewpoint: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error en RestoreLastViewpointAsync: {ex}");
            }
        }

        private async Task LoadProyectosAsync(ViewModels.MapaViewModel vm)
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

                IEnumerable<Geomatica.Data.Repositories.ProyectoDto> items;


                // Check for Municipio selection first
                if (vm.Filtros.AreaInteres is ViewModels.FiltrosViewModel.MunicipioItem muni)
                {
                    items = await repo.ListarPorMunicipioAsync(muni.Codigo, vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave);
                }
                // Then check for key Department selection
                else if (vm.Filtros.SelectedDepartamento is ViewModels.FiltrosViewModel.DepartamentoItem dept)
                {
                    items = await repo.ListarPorDepartamentoAsync(dept.Codigo, vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave);
                }
                else
                {
                    // No geo filter: list all projects
                    items = await repo.ListarAsync(vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave, null);
                }

                // Update UI-bound collections on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    vm.Filtros.ResultadosLista.Clear();
                    vm.Filtros.ResultadosResumen.Clear();

                    foreach (var p in items)
                    {
                        // Log ruta for debugging
                        Debug.WriteLine($"[MapaView] Proyecto cargado: id={p.Id}, titulo='{p.Titulo}', ruta='{p.RutaArchivos}'");
                        vm.Filtros.ResultadosLista.Add(new ViewModels.FiltrosViewModel.ProyectoItem(p.Id, p.Titulo, p.Lon, p.Lat, p.RutaArchivos));
                    }

                    // resumen: mostrar conteo y primeros5 titulos
                    vm.Filtros.ResultadosResumen.Add($"{items.Count()} proyectos");
                    foreach (var p in items.Take(5))
                    {
                        vm.Filtros.ResultadosResumen.Add(p.Titulo);
                    }

                    if (!items.Any())
                    {
                        Debug.WriteLine("[MapaView] No se encontraron proyectos en la consulta.");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error cargando proyectos: {ex}");
            }
        }
    }
}
