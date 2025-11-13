using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;

namespace Geomatica.Desktop.Views
{
    public partial class ArchivosView : UserControl
    {
        public ArchivosView() 
        { 
            InitializeComponent();
            this.DataContextChanged += ArchivosView_DataContextChanged;
            this.Loaded += ArchivosView_Loaded;
            this.Unloaded += ArchivosView_Unloaded;
        }

        private void ArchivosView_Loaded(object? sender, RoutedEventArgs e)
        {
            AttachToFiltros(DataContext as ViewModels.ArchivosViewModel);
        }

        private void ArchivosView_Unloaded(object? sender, RoutedEventArgs e)
        {
            DetachFromFiltros(DataContext as ViewModels.ArchivosViewModel);
        }

        private void ArchivosView_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ViewModels.ArchivosViewModel oldVm)
            {
                DetachFromFiltros(oldVm);
            }

            if (e.NewValue is ViewModels.ArchivosViewModel newVm)
            {
                AttachToFiltros(newVm);
            }
        }

        private void AttachToFiltros(ViewModels.ArchivosViewModel? vm)
        {
            if (vm == null) return;
            if (vm.Filtros != null)
            {
                // subscribe to property change to detect SelectedProyecto
                vm.Filtros.PropertyChanged += Filtros_PropertyChanged;
                // if already selected, handle it
                var sel = vm.Filtros.SelectedProyecto;
                if (sel != null)
                {
                    OnProyectoSeleccionado(vm, sel);
                }
            }
        }

        private void DetachFromFiltros(ViewModels.ArchivosViewModel? vm)
        {
            if (vm == null) return;
            if (vm.Filtros != null)
            {
                vm.Filtros.PropertyChanged -= Filtros_PropertyChanged;
            }
        }

        private void Filtros_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.FiltrosViewModel.SelectedProyecto))
            {
                if (sender is ViewModels.FiltrosViewModel filtros)
                {
                    var vm = DataContext as ViewModels.ArchivosViewModel;
                    if (vm != null)
                    {
                        var proj = filtros.SelectedProyecto;
                        OnProyectoSeleccionado(vm, proj);
                    }
                }
            }
        }

        private void OnProyectoSeleccionado(ViewModels.ArchivosViewModel vm, ViewModels.FiltrosViewModel.ProyectoItem? proj)
        {
            if (proj == null) return;
            // If project provides a ruta/server path, set as RutaActual so files list updates
            if (!string.IsNullOrWhiteSpace(proj.Ruta))
            {
                // update RutaActual on UI thread
                Dispatcher.InvokeAsync(() =>
                {
                    vm.RutaActual = proj.Ruta;
                    // use generated command to refresh
                    vm.RefrescarCommand.Execute(null);
                });
            }
        }

        private void ListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.ArchivosViewModel vm && ((FrameworkElement)e.OriginalSource).DataContext != null)
            {
                var item = ((FrameworkElement)e.OriginalSource).DataContext;
                if (item is ViewModels.CarpetaNode carpeta)
                {
                    // navigate into folder
                    vm.RutaActual = carpeta.Ruta;
                    vm.RefrescarCommand.Execute(null);
                }
                else if (item is ViewModels.ArchivoItem archivo)
                {
                    // open file
                    vm.AbrirCommand.Execute(null);
                }
            }
        }
    }
}
