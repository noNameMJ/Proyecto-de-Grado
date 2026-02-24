using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Geomatica.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object? currentView;
        public string CurrentViewName => CurrentView is MapaViewModel ? "Vista: Mapa" : (CurrentView is ArchivosViewModel ? "Vista: Archivos" : "Vista: Creación");

        public FiltrosViewModel Filtros { get; }

        private readonly Func<MapaViewModel> _mapFactory;
        private readonly Func<ArchivosViewModel> _filesFactory;
        private readonly Func<Action, Action?, CrearProyectoViewModel> _createFactory;
        
        private MapaViewModel? _mapVM;
        private ArchivosViewModel? _filesVM;
        private CrearProyectoViewModel? _createVM;

        public MainViewModel(FiltrosViewModel filtros, Func<MapaViewModel> mapFactory, Func<ArchivosViewModel> filesFactory, Func<Action, Action?, CrearProyectoViewModel> createFactory)
        {
            Filtros = filtros;
            _mapFactory = mapFactory;
            _filesFactory = filesFactory;
            _createFactory = createFactory;
            // No crear vistas aquí: se crearán bajo demanda.
            currentView = null;
        }

        [RelayCommand]
        private void ShowMapa()
        {
            _mapVM ??= _mapFactory();
            CurrentView = _mapVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        [RelayCommand]
        private void ShowArchivos()
        {
            _filesVM ??= _filesFactory();
            CurrentView = _filesVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        [RelayCommand]
        public void ShowCrearProyecto()
        {
            _createVM = _createFactory(ShowMapa, () =>
            {
                if (_mapVM != null)
                    _ = _mapVM.InvalidarCacheYRecargarAsync();
            });
            CurrentView = _createVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }
    }
}
