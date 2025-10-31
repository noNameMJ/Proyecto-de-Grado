using Geomatica.Desktop.ViewModels;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : UserControl
    {
        public MapaView()
        {
            InitializeComponent();

            // No resolver ni asignar manualmente el ViewModel.
            // El DataTemplate en App.xaml proporciona la instancia correcta (CurrentView).
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is MapaViewModel vm)
                {
                    // Si necesitas reaccionar una sola vez a que el VM esté listo,
                    // hazlo aquí (sin crear/obtener otra instancia).
                }
            };
        }
    }
}
