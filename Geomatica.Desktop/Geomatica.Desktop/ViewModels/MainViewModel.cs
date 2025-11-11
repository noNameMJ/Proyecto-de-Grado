using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Geomatica.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object? currentView;
        public string CurrentViewName => CurrentView is MapaViewModel ? "Vista: Mapa" : "Vista: Archivos";

        public FiltrosViewModel Filtros { get; }

        private readonly Func<MapaViewModel> _mapFactory;
        private readonly Func<ArchivosViewModel> _filesFactory;
        private MapaViewModel? _mapVM;
        private ArchivosViewModel? _filesVM;

        public MainViewModel(FiltrosViewModel filtros, Func<MapaViewModel> mapFactory, Func<ArchivosViewModel> filesFactory)
        {
            Filtros = filtros;
            _mapFactory = mapFactory;
            _filesFactory = filesFactory;
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
    }
}
