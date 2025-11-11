using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : UserControl
    {
        public MapaView()
        {
            InitializeComponent();

            var filtros = (ViewModels.FiltrosViewModel)Resources["FiltrosVM"];
            filtros.BuscarSolicitado += (_, __) => { /* noop */ };

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
                newVm.PropertyChanged += Vm_PropertyChanged;
                newVm.HomeRequested += Vm_HomeRequested;

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
            }
        }

        private void MapaView_Unloaded(object? sender, RoutedEventArgs e)
        {
            var mv = this.FindName("controlMapView") as MapView;

            if (DataContext is ViewModels.MapaViewModel vm)
            {
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

        private async void Vm_HomeRequested(object? sender, EventArgs e)
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
    }
}
