using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class EditarProyectoView : UserControl
    {
        private readonly GraphicsOverlay _pinOverlay = new();

        public EditarProyectoView()
        {
            InitializeComponent();

            var map = new Map(BasemapStyle.ArcGISTopographic);
            var center = new MapPoint(-73.1198, 7.1254, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(center, 2_000_000);
            pickerMapView.Map = map;
            pickerMapView.GraphicsOverlays.Add(_pinOverlay);
            pickerMapView.GeoViewTapped += PickerMapView_GeoViewTapped;

            DataContextChanged += EditarProyectoView_DataContextChanged;
            Unloaded += (_, _) =>
            {
                pickerMapView.GeoViewTapped -= PickerMapView_GeoViewTapped;
                pickerMapView.Map = null;
            };
        }

        private void EditarProyectoView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ViewModels.EditarProyectoViewModel vm
                && !string.IsNullOrWhiteSpace(vm.LatStr)
                && !string.IsNullOrWhiteSpace(vm.LonStr))
            {
                var latNorm = vm.LatStr.Replace(',', '.');
                var lonNorm = vm.LonStr.Replace(',', '.');

                if (double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    var point = new MapPoint(lon, lat, SpatialReferences.Wgs84);
                    ActualizarPin(point);
                    pickerMapView.SetViewpointCenterAsync(point, 500_000);
                }
            }
        }

        private void PickerMapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            if (e.Location == null) return;

            var wgs84 = (MapPoint)e.Location.Project(SpatialReferences.Wgs84);
            if (wgs84 == null) return;

            if (DataContext is ViewModels.EditarProyectoViewModel vm)
                vm.SetCoordenadas(wgs84.Y, wgs84.X);

            ActualizarPin(wgs84);
        }

        private void ActualizarPin(MapPoint point)
        {
            _pinOverlay.Graphics.Clear();

            var marker = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle,
                System.Drawing.Color.FromArgb(255, 0, 102, 51), 12)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                    System.Drawing.Color.White, 2)
            };

            _pinOverlay.Graphics.Add(new Graphic(point, marker));
        }
    }
}
