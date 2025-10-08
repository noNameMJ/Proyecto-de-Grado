using System.Windows;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class ArchivosView : UserControl
    {
        public ArchivosView() { InitializeComponent(); }
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ViewModels.ArchivosViewModel vm && e.NewValue is ViewModels.CarpetaNode nodo)
                vm.NavegarANodo(nodo);
        }
    }
}
