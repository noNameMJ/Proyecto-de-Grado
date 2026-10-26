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
        private readonly GraphicsOverlay _subproyectosOverlay = new();
        private FormularioProyectoViewModel? _currentVm;

        public FormularioProyectoView()
        {
            InitializeComponent();

            var map = new Map(BasemapStyle.ArcGISTopographic);
            var center = new MapPoint(-73.1198, 7.1254, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(center, 2_000_000);
            pickerMapView.Map = map;
            pickerMapView.GraphicsOverlays?.Add(_municipioOverlay);
            pickerMapView.GraphicsOverlays?.Add(_subproyectosOverlay);
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
                vm.MunicipiosGeoJsonListChanged += OnMunicipiosGeoJsonListChanged;
                vm.CoordenadasPinChanged += OnCoordenadasPinChanged;
                vm.SubproyectosPinsChanged += OnSubproyectosPinsChanged;
                vm.CentrarEnSubproyectoSolicitado += OnCentrarEnSubproyectoSolicitado;

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

                // Mostrar pines de subproyectos si ya están disponibles
                if (vm.Subproyectos.Count > 0)
                {
                    ActualizarSubproyectosPins(vm.Subproyectos);
                }
            }
        }

        private void DetachVm()
        {
            if (_currentVm != null)
            {
                _currentVm.MunicipiosGeoJsonListChanged -= OnMunicipiosGeoJsonListChanged;
                _currentVm.CoordenadasPinChanged -= OnCoordenadasPinChanged;
                _currentVm.SubproyectosPinsChanged -= OnSubproyectosPinsChanged;
                _currentVm.CentrarEnSubproyectoSolicitado -= OnCentrarEnSubproyectoSolicitado;
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

        private void OnMunicipiosGeoJsonListChanged(System.Collections.Generic.IReadOnlyList<string> geoJsons)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                _municipioOverlay.Graphics.Clear();
                if (geoJsons == null || geoJsons.Count == 0) return;

                var fill = new SimpleFillSymbol(SimpleFillSymbolStyle.Solid,
                    System.Drawing.Color.FromArgb(40, 0, 102, 51),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(180, 0, 102, 51), 1.5f));

                var extents = new System.Collections.Generic.List<Envelope>();

                foreach (var geoJson in geoJsons)
                {
                    if (string.IsNullOrWhiteSpace(geoJson)) continue;
                    var geom = MapaViewModel.ParseGeoJson(geoJson);
                    if (geom == null) continue;

                    _municipioOverlay.Graphics.Add(new Graphic(geom, fill));
                    if (geom.Extent != null)
                    {
                        extents.Add(geom.Extent);
                    }
                }

                if (extents.Count > 0)
                {
                    var combinedExtent = GeometryEngine.CombineExtents(extents);
                    if (combinedExtent != null)
                    {
                        await pickerMapView.SetViewpointGeometryAsync(combinedExtent, 40);
                    }
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
        private void OnSubproyectosPinsChanged(System.Collections.Generic.IReadOnlyList<SubproyectoItemFormulario> subproyectos)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ActualizarSubproyectosPins(subproyectos);
            });
        }

        private void OnCentrarEnSubproyectoSolicitado(double lon, double lat)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                if (lon != 0 || lat != 0)
                {
                    var point = new MapPoint(lon, lat, SpatialReferences.Wgs84);
                    await pickerMapView.SetViewpointCenterAsync(point, 25_000);
                }
            });
        }

        private void ActualizarSubproyectosPins(System.Collections.Generic.IReadOnlyList<SubproyectoItemFormulario>? subproyectos)
        {
            _subproyectosOverlay.Graphics.Clear();
            if (subproyectos == null || subproyectos.Count == 0) return;

            // Símbolo diamante ámbar/naranja con borde blanco para destacar frentes de trabajo / subproyectos
            var subMarker = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond,
                System.Drawing.Color.FromArgb(240, 230, 126, 34), 11)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid,
                    System.Drawing.Color.White, 1.8f)
            };

            foreach (var sub in subproyectos)
            {
                if (sub.Lon == 0 && sub.Lat == 0) continue;

                var point = new MapPoint(sub.Lon, sub.Lat, SpatialReferences.Wgs84);
                _subproyectosOverlay.Graphics.Add(new Graphic(point, subMarker));

                if (!string.IsNullOrWhiteSpace(sub.Nombre))
                {
                    var textSymbol = new TextSymbol(
                        sub.Nombre,
                        System.Drawing.Color.FromArgb(230, 30, 41, 59),
                        9.5,
                        Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Left,
                        Esri.ArcGISRuntime.Symbology.VerticalAlignment.Middle)
                    {
                        OffsetX = 9,
                        OffsetY = 0,
                        HaloColor = System.Drawing.Color.White,
                        HaloWidth = 2
                    };
                    _subproyectosOverlay.Graphics.Add(new Graphic(point, textSymbol));
                }
            }
        }
    }
}