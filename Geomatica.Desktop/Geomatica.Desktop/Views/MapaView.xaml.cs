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

                    // clear to avoid Map being owned by multiple MapViews
                    mv.Map = null;
                }
            }

            if (e.NewValue is ViewModels.MapaViewModel newVm)
            {
                _currentVm = newVm;
                newVm.PropertyChanged += Vm_PropertyChanged;
                newVm.HomeRequested += Vm_HomeRequested;
                if (newVm.Filtros != null)
                    newVm.Filtros.BuscarSolicitado += Filtros_BuscarSolicitado;

                // restore viewpoint if available
                if (mv != null && newVm.LastViewpoint != null)
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            await mv.SetViewpointAsync(newVm.LastViewpoint);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MapaView] Error restaurando LastViewpoint: {ex}");
                        }
                    });
                }

                // Load departamentos into filters Areas collection (refresh)
                _ = LoadDepartamentosAsync(newVm);

                // Load proyectos into filtros results so user can see them
                _ = LoadProyectosAsync(newVm);
            }
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

                if (mv != null && mv.Map == vm.Map)
                {
                    try
                    {
                        vm.LastViewpoint = mv.GetCurrentViewpoint(ViewpointType.CenterAndScale);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MapaView] No se pudo obtener viewpoint en Unloaded: {ex}");
                    }

                    mv.Map = null;
                }

                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.HomeRequested -= Vm_HomeRequested;
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

                    // reasignar el Map (puede ser null o nuevo objeto)
                    mv.Map = vm.Map;

                    if (vm.LastViewpoint != null)
                    {
                        try
                        {
                            await mv.SetViewpointAsync(vm.LastViewpoint);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MapaView] Error restaurando LastViewpoint en PropertyChanged: {ex}");
                        }
                    }
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

        private async Task LoadDepartamentosAsync(ViewModels.MapaViewModel vm)
        {
            try
            {
                // Get service provider from Application.Properties
                IServiceProvider? provider = null;
                if (Application.Current.Properties.Contains("ServiceProvider"))
                    provider = Application.Current.Properties["ServiceProvider"] as IServiceProvider;

                if (provider == null)
                {
                    Debug.WriteLine("[MapaView] No se encontró ServiceProvider para cargar departamentos.");
                    return;
                }

                var repo = provider.GetService<IMunicipioRepository>();
                if (repo == null)
                {
                    Debug.WriteLine("[MapaView] IMunicipioRepository no está registrado en DI.");
                    return;
                }

                var deps = await repo.ListarDepartamentosAsync();

                // If Areas already populated, avoid clearing to preserve selection identity
                if (vm.Filtros.Areas.Count ==0)
                {
                    foreach (var d in deps)
                    {
                        vm.Filtros.Areas.Add(new ViewModels.FiltrosViewModel.DepartamentoItem(d.Codigo, d.Nombre));
                    }
                }
                else
                {
                    // Ensure if AreaInteres is set by previous selection (from another view) we reuse the matching instance
                    if (vm.Filtros.AreaInteres is ViewModels.FiltrosViewModel.DepartamentoItem existingSelection)
                    {
                        var match = vm.Filtros.Areas.OfType<ViewModels.FiltrosViewModel.DepartamentoItem>().FirstOrDefault(a => a.Codigo == existingSelection.Codigo);
                        if (match != null)
                        {
                            // keep the same reference so ComboBox selection remains
                            vm.Filtros.AreaInteres = match;
                        }
                        else
                        {
                            // selection not present in current list; try to add it
                            vm.Filtros.Areas.Add(existingSelection);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapaView] Error cargando departamentos: {ex}");
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

                if (vm.Filtros.AreaInteres is ViewModels.FiltrosViewModel.DepartamentoItem dept)
                {
                    // Use department-based join query to find projects linked via proyecto_municipio
                    items = await repo.ListarPorDepartamentoAsync(dept.Codigo, vm.Filtros.Desde, vm.Filtros.Hasta, vm.Filtros.PalabraClave);
                }
                else
                {
                    // No department selected: list all projects (no area filter)
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
