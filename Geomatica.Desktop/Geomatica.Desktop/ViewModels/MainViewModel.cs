using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Geomatica.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private object currentView;

    public string CurrentViewName => CurrentView is MapaViewModel ? "Vista: Mapa" : "Vista: Proyectos";

    public FiltrosViewModel Filtros { get; } = new();

    private readonly MapaViewModel _mapVm;
    private readonly ArchivosViewModel _filesVm;

    // Recibe casos de uso vía DI
    public MainViewModel(Geomatica.AppCore.UseCases.BuscarProyectosUseCase buscar)
    {
        _mapVm = new MapaViewModel(buscar, Filtros);
        _filesVm = new ArchivosViewModel(_mapVm);

        currentView = _mapVm;
    }

    [RelayCommand] private void ShowMapa() { CurrentView = _mapVm; OnPropertyChanged(nameof(CurrentViewName)); }
    [RelayCommand] private void ShowArchivos() { CurrentView = _filesVm; OnPropertyChanged(nameof(CurrentViewName)); }
}
