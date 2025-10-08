using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Geomatica.Desktop.ViewModels
{
    internal class MapaViewModel : INotifyPropertyChanged
    {
        // Referencia opcional a los filtros compartidos
        public FiltrosViewModel? Filtros { get; }

        // Nuevo constructor: recibe los filtros (opción B)
        public MapaViewModel(FiltrosViewModel filtros)
        {
            Filtros = filtros;
            // Si deseas reaccionar a “Buscar” más adelante:
            // Filtros.BuscarSolicitado += (_,__) => AplicarFiltrosEnMapa();
            SetupMap();
        }

        // Constructor existente (compatibilidad): crea filtros locales
        public MapaViewModel() : this(new FiltrosViewModel())
        {
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

        // Hook opcional para aplicar filtros sobre el mapa
        private void AplicarFiltrosEnMapa()
        {
            // TODO: usar Filtros?.PalabraClave / Desde / Hasta / AreaInteres
            // para construir consultas o DefinitionExpression en capas.
        }
    }
}
