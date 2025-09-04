using System.Windows;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;

namespace Geomatica.Desktop.Views
{
    public partial class MapaView : Window
    {
        public MapaView()
        {
            InitializeComponent();

            // Establecer un mapa base si no se definió en XAML
            if (MapViewControl.Map == null)
            {
                MapViewControl.Map = new Map(BasemapStyle.ArcGISLightGray);
            }

            // Enfocar el mapa sobre Santander, Colombia (aprox. WGS84)
            var santander = new Envelope(
                -74.5, 5.0,   // minX, minY aprox
                -72.5, 7.8,   // maxX, maxY aprox
                SpatialReferences.Wgs84);

            MapViewControl.SetViewpointGeometryAsync(santander, 40);
        }
    }
}
