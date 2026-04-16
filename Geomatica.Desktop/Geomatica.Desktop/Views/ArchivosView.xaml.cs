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
            // Omitted to avoid redundant handling since ViewModel manages it
        }

        private void DetachFromFiltros(ViewModels.ArchivosViewModel? vm)
        {
        }

        private void OnProyectoSeleccionado(ViewModels.ArchivosViewModel vm, ViewModels.FiltrosViewModel.ProyectoItem? proj)
        {
        }

        private void ListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.ArchivosViewModel vm && ((FrameworkElement)e.OriginalSource).DataContext != null)
            {
                var item = ((FrameworkElement)e.OriginalSource).DataContext;
                if (item is Models.CarpetaVirtual carpeta)
                {
                    // navigate into folder
                    vm.Seleccionado = carpeta;
                    vm.AbrirCommand.Execute(null);
                }
                else if (item is Models.ArchivoVirtual archivo)
                {
                    // open file
                    vm.Seleccionado = archivo;
                    vm.AbrirCommand.Execute(null);
                }
            }
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (DataContext is ViewModels.ArchivosViewModel vm && files != null && files.Length > 0)
                {
                    vm.ProcesarArchivosDroppeados(files);
                }
            }
        }
    }
}
