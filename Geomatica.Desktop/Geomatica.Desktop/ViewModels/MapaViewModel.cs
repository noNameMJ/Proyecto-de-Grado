using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Geomatica.AppCore.UseCases;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Geomatica.Desktop.ViewModels
{
    public class MapaViewModel : INotifyPropertyChanged
    {
        private readonly BuscarProyectosUseCase _buscar;     // 1) caso de uso inyectado
        // Referencia opcional a los filtros compartidos
        public FiltrosViewModel? Filtros { get; }

        // Nuevo constructor: recibe los filtros (opción B)
        public MapaViewModel(BuscarProyectosUseCase buscar, FiltrosViewModel filtros)
        {
            _buscar = buscar;
            Filtros = filtros;
            if (Filtros != null)
                Filtros.BuscarSolicitado += async (_, __) => await AplicarFiltrosEnMapaAsync();
            SetupMap();
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private Map? _map;
        public Map? Map
        {
            get => _map;
            set { _map = value; OnPropertyChanged(); }
        }
        private void SetupMap()
        {
            var map = new Map(BasemapStyle.ArcGISTopographic);
            var mapCenterPoint = new MapPoint(-74.146592, 4.680486, SpatialReferences.Wgs84);
            map.InitialViewpoint = new Viewpoint(mapCenterPoint, 12000000);
            Map = map;
        }

        public async Task OnMapViewChangedAsync(Geometry newExtent)
        {
            
            var geojson = newExtent.ToJson(); // ArcGIS Runtime devuelve JSON de la geometría

            
            if (Filtros != null)
            {
                Filtros.AreaWkt = geojson; // renómbralo si quieres a AreaJson
                await AplicarFiltrosEnMapaAsync();
            }
        }

        private async Task AplicarFiltrosEnMapaAsync()
        {
            var proyectos = await _buscar.PorAOIAsync(
                Filtros.AreaWkt,
                Filtros.Desde,
                Filtros.Hasta,
                Filtros.PalabraClave);

            ProyectosCargados?.Invoke(this, proyectos); // usa ?. para evitar NullReference
        }

        public event EventHandler<IReadOnlyList<Geomatica.Domain.Entities.Proyecto>>? ProyectosCargados;

    }
}