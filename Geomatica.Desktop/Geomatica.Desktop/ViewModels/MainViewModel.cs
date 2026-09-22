using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.Desktop.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentViewName))]
        [NotifyPropertyChangedFor(nameof(IsMapaActive))]
        [NotifyPropertyChangedFor(nameof(IsCrearProyectoActive))]
        private object? currentView;

        public string CurrentViewName => CurrentView switch
        {
            MapaViewModel => "🗺️ Vista: Mapa",
            ArchivosViewModel a when a.HasProyectoDetalle => "📋 Vista: Ficha de Proyecto",
            ArchivosViewModel => "📂 Vista: Archivos",
            EditarProyectoViewModel => "✏️ Vista: Editar Proyecto",
            _ => "➕ Vista: Creación"
        };

        public bool IsMapaActive => CurrentView is MapaViewModel;
        public bool IsCrearProyectoActive => CurrentView is CrearProyectoViewModel;
        public string UsuarioWindowsActual => $"{Environment.UserDomainName}\\{Environment.UserName}";

        public FiltrosViewModel Filtros { get; }
        public INotificationService Notifications { get; }

        private readonly EliminarProyectoUseCase _eliminarProyectoUseCase;
        private readonly ProyectoArchivosService _archivosService;
        private readonly IProyectoRepository _proyectoRepository;
        private readonly Func<MapaViewModel> _mapFactory;
        private readonly Func<ArchivosViewModel> _filesFactory;
        private readonly Func<Action, Action?, CrearProyectoViewModel> _createFactory;
        private readonly Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel> _editFactory;

        private MapaViewModel? _mapVM;
        private ArchivosViewModel? _filesVM;
        private CrearProyectoViewModel? _createVM;

        public MainViewModel(
            FiltrosViewModel filtros,
            INotificationService notifications,
            EliminarProyectoUseCase eliminarProyectoUseCase,
            ProyectoArchivosService archivosService,
            Func<MapaViewModel> mapFactory,
            Func<ArchivosViewModel> filesFactory,
            Func<Action, Action?, CrearProyectoViewModel> createFactory,
            Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel> editFactory,
            IProyectoRepository proyectoRepository)
        {
            Filtros = filtros;
            Notifications = notifications;
            _eliminarProyectoUseCase = eliminarProyectoUseCase;
            _archivosService = archivosService;
            _proyectoRepository = proyectoRepository;
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
            _filesVM.ProyectoDetalle = null;
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
            if (_mapVM == null) return;

            var archivosVm = _mapVM.ArchivosVM;

            var fichaVm = new FichaProyectoViewModel(
                detalle,
                () =>
                {
                    // "Cerrar detalle" limpia la ficha pero nos quedamos en MapaView.
                    archivosVm.ProyectoDetalle = null;
                },
                _eliminarProyectoUseCase,
                () =>
                {
                    archivosVm.ProyectoDetalle = null;
                    if (Filtros.SelectedProyecto?.Id == detalle.Id)
                    {
                        Filtros.SelectedProyecto = null;
                    }
                    _mapVM?.InvalidarCache();
                    Filtros.BuscarCommand.Execute(null);
                },
                _archivosService,
                Notifications,
                _proyectoRepository);
            fichaVm.EditarSolicitado += OnEditarSolicitado;

            archivosVm.ProyectoDetalle = fichaVm;

            // Asegurarse de que el current view siga siendo Mapa y se actualice el visual.
            CurrentView = _mapVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        private void FilesVM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ArchivosViewModel.HasProyectoDetalle))
                OnPropertyChanged(nameof(CurrentViewName));
        }

        private void OnEditarSolicitado(object? sender, ProyectoDetalleDto detalle)
        {
            var editVm = _editFactory(detalle, ShowMapa, () =>
            {
                if (_mapVM != null)
                {
                    _mapVM.ArchivosVM.ProyectoDetalle = null;
                    if (Filtros.SelectedProyecto?.Id == detalle.Id)
                    {
                        Filtros.SelectedProyecto = null;
                    }
                    _mapVM.InvalidarCache();
                }
                Filtros.BuscarCommand.Execute(null);
            });
            CurrentView = editVm;
            OnPropertyChanged(nameof(CurrentViewName));
        }
    }
}
