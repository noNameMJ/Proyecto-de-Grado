using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : UserControl
    {
        public MapaView()
        {
            InitializeComponent();
            var filtros = (ViewModels.FiltrosViewModel)Resources["FiltrosVM"];
            filtros.BuscarSolicitado += (_, __) =>
            {
                // Aquí puedes leer filtros.PalabraClave/Desde/Hasta
                // y reaccionar en el mapa (añadir luego logic).
            };
        }
    }
}
