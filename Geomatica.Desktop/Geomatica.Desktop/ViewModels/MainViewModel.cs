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
            FormularioProyectoViewModel f when f.EsModoEdicion => "✏️ Vista: Editar Proyecto",
            FormularioProyectoViewModel => "➕ Vista: Creación",
            _ => "➕ Vista: Creación"
        };

        public bool IsMapaActive => CurrentView is MapaViewModel;
        public bool IsCrearProyectoActive => CurrentView is FormularioProyectoViewModel f ? !f.EsModoEdicion : CurrentView is CrearProyectoViewModel;
        public string UsuarioWindowsActual => $"{Environment.UserDomainName}\\{Environment.UserName}";

        public FiltrosViewModel Filtros { get; }
        public INotificationService Notifications { get; }

        private readonly EliminarProyectoUseCase _eliminarProyectoUseCase;
        private readonly ProyectoArchivosService _archivosService;
        private readonly IProyectoRepository _proyectoRepository;
        private readonly ObtenerHistorialProyectoUseCase? _historialUseCase;
        private readonly ISeguridadService? _seguridadService;
        private readonly ObtenerSubproyectosUseCase? _obtenerSubproyectosUseCase;
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
            IProyectoRepository proyectoRepository,
            ObtenerHistorialProyectoUseCase? historialUseCase = null,
            ISeguridadService? seguridadService = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null)
        {
            Filtros = filtros;
            Notifications = notifications;
            _eliminarProyectoUseCase = eliminarProyectoUseCase;
            _archivosService = archivosService;
            _proyectoRepository = proyectoRepository;
            _historialUseCase = historialUseCase ?? new ObtenerHistorialProyectoUseCase(proyectoRepository);
            _seguridadService = seguridadService;
            _obtenerSubproyectosUseCase = obtenerSubproyectosUseCase;
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
                _mapVM.SubcarpetaNavegacionSolicitada -= OnSubcarpetaNavegacionSolicitada;
                _mapVM.SubcarpetaNavegacionSolicitada += OnSubcarpetaNavegacionSolicitada;
                _mapVM.ArchivosVM.PropertyChanged -= FilesVM_PropertyChanged;
                _mapVM.ArchivosVM.PropertyChanged += FilesVM_PropertyChanged;
            }
            CurrentView = _mapVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        [RelayCommand]
        private void ShowArchivos()
        {
            if (_filesVM == null)
            {
                _filesVM = _filesFactory();
                _filesVM.PropertyChanged -= FilesVM_PropertyChanged;
                _filesVM.PropertyChanged += FilesVM_PropertyChanged;
            }
            _filesVM.ProyectoDetalle = null;
            CurrentView = _filesVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        [RelayCommand]
        public void ShowCrearProyecto()
        {
            _createVM = _createFactory(ShowMapa, () =>
            {
                if (_mapVM != null)
                {
                    _mapVM.ArchivosVM.ProyectoDetalle = null;
                    _mapVM.InvalidarCache();
                    _ = _mapVM.BuscarConFiltrosGeograficosAsync();
                }
            });
            CurrentView = _createVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        private void OnFichaProyectoSolicitada(object? sender, ProyectoDetalleDto detalle)
        {
            if (_mapVM == null) return;

            var archivosVm = _mapVM.ArchivosVM;
            if (archivosVm.ProyectoDetalle != null)
            {
                archivosVm.ProyectoDetalle.EditarSolicitado -= OnEditarSolicitado;
            }

            // Reutilizar evaluación de permisos ya disponible en el visor de archivos para evitar I/O redundante en red
            EvaluacionPermisos? permisosPrecalculados = null;
            if (archivosVm.PermisosCarpeta != null &&
                string.Equals(archivosVm.RutaRaizProyecto, detalle.RutaArchivos, StringComparison.OrdinalIgnoreCase))
            {
                permisosPrecalculados = archivosVm.PermisosCarpeta;
            }

            var fichaVm = new FichaProyectoViewModel(
                detalle,
                () =>
                {
                    // "Cerrar detalle" limpia la ficha pero nos quedamos en MapaView.
                    archivosVm.ProyectoDetalle = null;
                    _mapVM?.LimpiarSubproyectos();
                },
                _eliminarProyectoUseCase,
                () =>
                {
                    archivosVm.ProyectoDetalle = null;
                    _mapVM?.LimpiarSubproyectos();
                    if (Filtros.SelectedProyecto?.Id == detalle.Id)
                    {
                        Filtros.SelectedProyecto = null;
                    }
                    _mapVM?.InvalidarCache();
                    Filtros.BuscarCommand.Execute(null);
                },
                _archivosService,
                Notifications,
                _proyectoRepository,
                _historialUseCase,
                _seguridadService,
                permisosPrecalculados,
                _obtenerSubproyectosUseCase);
            fichaVm.EditarSolicitado += OnEditarSolicitado;
            fichaVm.SubproyectosCargados += (s, lista) =>
            {
                _mapVM?.MostrarSubproyectosEnMapa(lista, actualizarListaGlobal: false);
            };
            fichaVm.NavegacionSubcarpetaSolicitada += (s, subcarpeta) =>
            {
                archivosVm.NavegarASubcarpeta(subcarpeta);
                CurrentView = _mapVM;
                OnPropertyChanged(nameof(CurrentViewName));
            };
            fichaVm.UbicarEnMapaSubproyectoSolicitado += async (s, coords) =>
            {
                if (_mapVM != null)
                {
                    CurrentView = _mapVM;
                    await _mapVM.CentrarEnSubproyectoAsync(coords.lon, coords.lat, coords.nombre);
                }
            };

            archivosVm.ProyectoDetalle = fichaVm;

            // Asegurarse de que el current view siga siendo Mapa y se actualice el visual.
            CurrentView = _mapVM;
            OnPropertyChanged(nameof(CurrentViewName));
        }

        private void OnSubcarpetaNavegacionSolicitada(object? sender, string subcarpeta)
        {
            if (_mapVM != null)
            {
                _mapVM.ArchivosVM.NavegarASubcarpeta(subcarpeta);
                CurrentView = _mapVM;
                OnPropertyChanged(nameof(CurrentViewName));
            }
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
                    _ = _mapVM.BuscarConFiltrosGeograficosAsync();
                }
            });
            CurrentView = editVm;
            OnPropertyChanged(nameof(CurrentViewName));
        }
    }
}
