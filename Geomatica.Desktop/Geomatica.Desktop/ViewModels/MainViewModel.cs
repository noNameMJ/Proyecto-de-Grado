using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Geomatica.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object currentView;
        public string CurrentViewName => CurrentView is MapaViewModel ? "Vista: Mapa" : "Vista: Archivos";

        // Instancia compartida
        public FiltrosViewModel Filtros { get; } = new();

        private readonly MapaViewModel _mapVM;
        private readonly ArchivosViewModel _filesVM;

        public MainViewModel()
        {
            _mapVM = new MapaViewModel(Filtros);      // pasar filtros compartidos
            _filesVM = new ArchivosViewModel(Filtros); // pasar filtros compartidos
            currentView = _mapVM; // vista inicial
        }

        [RelayCommand] private void ShowMapa() { CurrentView = _mapVM; OnPropertyChanged(nameof(CurrentViewName)); }
        [RelayCommand] private void ShowArchivos() { CurrentView = _filesVM; OnPropertyChanged(nameof(CurrentViewName)); }
    }
}
