using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Geomatica.Desktop.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    public partial class CrearProyectoView : UserControl
    {
        private readonly GraphicsOverlay _pinOverlay = new();
        private readonly GraphicsOverlay _municipioOverlay = new();
        private readonly Esri.ArcGISRuntime.UI.Controls.MapView _pickerMapView;
        private CrearProyectoViewModel? _currentVm;

        public CrearProyectoView()
        {
            CargarXaml();

            _pickerMapView = FindName("pickerMapView") as Esri.ArcGISRuntime.UI.Controls.MapView
                ?? throw new InvalidOperationException("No se encontró el control 'pickerMapView' en CrearProyectoView.xaml.");

            var map = new Map(BasemapStyle.ArcGISTopographic);
            var center = new MapPoint(-73.1198, 7.1254, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(center, 2_000_000);
            _pickerMapView.Map = map;
            _pickerMapView.GraphicsOverlays?.Add(_municipioOverlay);
            _pickerMapView.GraphicsOverlays?.Add(_pinOverlay);
            _pickerMapView.GeoViewTapped += PickerMapView_GeoViewTapped;

            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) =>
            {
                DetachVm();
                _pickerMapView.GeoViewTapped -= PickerMapView_GeoViewTapped;
                _pickerMapView.Map = null;
            };
        }

        private void CargarXaml()
        {
            var resourceLocator = new Uri("/Geomatica.Desktop;component/Views/CrearProyectoView.xaml", UriKind.Relative);
            Application.LoadComponent(this, resourceLocator);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachVm();
            if (e.NewValue is CrearProyectoViewModel vm)
            {
                _currentVm = vm;
                vm.MunicipioGeoJsonChanged += OnMunicipioGeoJsonChanged;
            }
        }

        private void DetachVm()
        {
            if (_currentVm != null)
            {
                _currentVm.MunicipioGeoJsonChanged -= OnMunicipioGeoJsonChanged;
                _currentVm = null;
            }
        }

        private void OnMunicipioGeoJsonChanged(string? geoJson)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _municipioOverlay.Graphics.Clear();
                if (string.IsNullOrEmpty(geoJson)) return;

                var geom = MapaViewModel.ParseGeoJson(geoJson);
                if (geom == null) return;

                var fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(40, 0, 102, 51),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(180, 0, 102, 51), 1.5f));

                _municipioOverlay.Graphics.Add(new Graphic(geom, fill));

                if (geom.Extent != null)
                {
                    await _pickerMapView.SetViewpointGeometryAsync(geom.Extent, 40);
                }
            });
        }

        private void PickerMapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            if (e.Location == null) return;

            var wgs84 = (MapPoint)e.Location.Project(SpatialReferences.Wgs84);
            if (wgs84 == null) return;

            if (DataContext is CrearProyectoViewModel vm)
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
