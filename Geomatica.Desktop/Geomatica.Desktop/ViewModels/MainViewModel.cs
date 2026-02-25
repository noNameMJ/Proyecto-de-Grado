using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;

namespace Geomatica.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object? currentView;
        public string CurrentViewName => CurrentView switch
        {
            MapaViewModel => "Vista: Mapa",
            ArchivosViewModel => "Vista: Archivos",
            FichaProyectoViewModel => "Vista: Ficha de Proyecto",
            EditarProyectoViewModel => "Vista: Editar Proyecto",
            _ => "Vista: Creación"
        };

        public FiltrosViewModel Filtros { get; }

        private readonly Func<MapaViewModel> _mapFactory;
        private readonly Func<ArchivosViewModel> _filesFactory;
        private readonly Func<Action, Action?, CrearProyectoViewModel> _createFactory;
        private readonly Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel> _editFactory;

        private MapaViewModel? _mapVM;
        private ArchivosViewModel? _filesVM;
        private CrearProyectoViewModel? _createVM;

        public MainViewModel(
            FiltrosViewModel filtros,
            Func<MapaViewModel> mapFactory,
            Func<ArchivosViewModel> filesFactory,
            Func<Action, Action?, CrearProyectoViewModel> createFactory,
            Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel> editFactory)
        {
            Filtros = filtros;
            _mapFactory = mapFactory;
            _filesFactory = filesFactory;
            _createFactory = createFactory;
            _editFactory = editFactory;
            currentView = null;
        }

        [RelayCommand]
        private void ShowMapa()
        {
            if (_mapVM == null)
            {
                _mapVM = _mapFactory();
                _mapVM.FichaProyectoSolicitada += OnFichaProyectoSolicitada;
            }
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
                _mapVM?.InvalidarCache();
            });
            CurrentView = _createVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        private void OnFichaProyectoSolicitada(object? sender, ProyectoDetalleDto detalle)
        {
            var fichaVm = new FichaProyectoViewModel(detalle, ShowMapa);
            fichaVm.EditarSolicitado += OnEditarSolicitado;
            CurrentView = fichaVm;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        private void OnEditarSolicitado(object? sender, ProyectoDetalleDto detalle)
        {
            var editVm = _editFactory(detalle, ShowMapa, () =>
            {
                _mapVM?.InvalidarCache();
            });
            CurrentView = editVm;
            OnPropertyChanged(nameof(CurrentViewName));
        }
    }
}
