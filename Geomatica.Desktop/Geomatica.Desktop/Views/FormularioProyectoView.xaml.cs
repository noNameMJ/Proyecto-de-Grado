using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Geomatica.Desktop.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Vista unificada para el formulario de proyectos (Creación y Edición).
    /// Centraliza la interacción con el mapa picker de ArcGIS y la sincronización con el ViewModel.
    /// </summary>
    public partial class FormularioProyectoView : UserControl
    {
        private readonly GraphicsOverlay _pinOverlay = new();
        private readonly GraphicsOverlay _municipioOverlay = new();
        private FormularioProyectoViewModel? _currentVm;

        public FormularioProyectoView()
        {
            InitializeComponent();

            var map = new Map(BasemapStyle.ArcGISTopographic);
            var center = new MapPoint(-73.1198, 7.1254, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(center, 2_000_000);
            pickerMapView.Map = map;
            pickerMapView.GraphicsOverlays?.Add(_municipioOverlay);
            pickerMapView.GraphicsOverlays?.Add(_pinOverlay);
            pickerMapView.GeoViewTapped += PickerMapView_GeoViewTapped;

            DataContextChanged += OnDataContextChanged;
            Unloaded += (_, _) =>
            {
                DetachVm();
                pickerMapView.GeoViewTapped -= PickerMapView_GeoViewTapped;
                pickerMapView.Map = null;
            };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachVm();
            if (e.NewValue is FormularioProyectoViewModel vm)
            {
                _currentVm = vm;
                vm.MunicipioGeoJsonChanged += OnMunicipioGeoJsonChanged;
                vm.CoordenadasPinChanged += OnCoordenadasPinChanged;

                // Mostrar pin inicial si el ViewModel ya cuenta con coordenadas válidas (ej. modo edición)
                if (!string.IsNullOrWhiteSpace(vm.LatStr) && !string.IsNullOrWhiteSpace(vm.LonStr))
                {
                    var latNorm = vm.LatStr.Replace(',', '.');
                    var lonNorm = vm.LonStr.Replace(',', '.');
                    if (double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                        && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                    {
                        ActualizarPin(new MapPoint(lon, lat, SpatialReferences.Wgs84));
                    }
                }
            }
        }

        private void DetachVm()
        {
            if (_currentVm != null)
            {
                _currentVm.MunicipioGeoJsonChanged -= OnMunicipioGeoJsonChanged;
                _currentVm.CoordenadasPinChanged -= OnCoordenadasPinChanged;
                _currentVm = null;
            }
        }

        private void OnCoordenadasPinChanged(double? lat, double? lon)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (lat.HasValue && lon.HasValue)
                {
                    ActualizarPin(new MapPoint(lon.Value, lat.Value, SpatialReferences.Wgs84));
                }
                else
                {
                    _pinOverlay.Graphics.Clear();
                }
            });
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
                    await pickerMapView.SetViewpointGeometryAsync(geom.Extent, 40);
                }
            });
        }

        private async void PickerMapView_GeoViewTapped(object? sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            if (e.Location == null) return;

            var wgs84 = (MapPoint)e.Location.Project(SpatialReferences.Wgs84);
            if (wgs84 == null) return;

            if (DataContext is FormularioProyectoViewModel vm)
            {
                await vm.ProcesarClickMapaAsync(wgs84.Y, wgs84.X);
            }
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
