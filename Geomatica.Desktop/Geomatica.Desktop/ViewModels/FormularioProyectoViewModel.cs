using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Domain.Catalogos;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel unificado para la creación y edición de proyectos geomáticos.
    /// Consolida la lógica compartida de metadatos ISO 19115, validación espacial,
    /// cascada de departamentos/municipios DIVIPOLA e interacción cartográfica ArcGIS.
    /// </summary>
    public partial class FormularioProyectoViewModel : ObservableObject
    {
        private readonly IMunicipioRepository _municipioRepository;
        private readonly INotificationService? _notifications;
        private readonly Action _navigateBack;
        private readonly Action? _onGuardadoExitoso;
        private readonly CrearProyectoUseCase? _crearProyectoUseCase;
        private readonly ActualizarProyectoUseCase? _actualizarProyectoUseCase;
        private readonly EliminarProyectoUseCase? _eliminarProyectoUseCase;
        private readonly ProyectoArchivosService? _archivosService;
        private readonly ProyectoDetalleDto? _proyectoOriginal;
        private readonly ISeguridadService? _seguridadService;
        private readonly GuardarSubproyectoUseCase? _guardarSubproyectoUseCase;
        private readonly ObtenerSubproyectosUseCase? _obtenerSubproyectosUseCase;
        private readonly EliminarSubproyectoUseCase? _eliminarSubproyectoUseCase;
        private bool _isUpdatingProgrammatically;

        public bool EsModoEdicion { get; }
        public int? IdProyecto => _proyectoOriginal?.Id;

        public string TituloVista => EsModoEdicion ? "Editar Proyecto" : "Crear Nuevo Proyecto";
        public string TextoBotonGuardar => EsModoEdicion ? "Guardar Cambios" : "Guardar Proyecto";
        public string TooltipGuardar => EsSoloLecturaOAccesoRestringido
            ? "Acceso restringido: Se requieren permisos de escritura en la carpeta del servidor o rol de Administrador para editar este proyecto (geomaticaad@uis.edu.co)."
            : (EsModoEdicion ? "Guardar las modificaciones realizadas al proyecto" : "Registrar y guardar el nuevo proyecto en el sistema");

        public EvaluacionPermisos? PermisosCarpeta { get; }
        public bool PuedeEscribir => (PermisosCarpeta?.PuedeEscribir ?? true) || (_seguridadService?.PuedeEditarProyectos() ?? false);
        public bool EsSoloLecturaOAccesoRestringido => EsModoEdicion && !PuedeEscribir;
        public string MensajeRestriccion => PermisosCarpeta?.Mensaje ?? string.Empty;
        public bool PuedeEliminar => EsModoEdicion && (_seguridadService?.PuedeEliminarProyectos() ?? false);
        public string TooltipEliminar => PuedeEliminar
            ? "Eliminar este proyecto del sistema"
            : "Acceso restringido: Solo los miembros del rol Administrador (grupo AD 'G4_GEOMATICA_ADM') pueden eliminar proyectos.";

        // ═══════════════════════════════════════════════════════════════
        // PROPIEDADES OBSERVABLES DEL FORMULARIO
        // ═══════════════════════════════════════════════════════════════

        // Sección 1: Ruta y Metadatos Técnicos ISO 19115
        [ObservableProperty] private string? ruta;

        partial void OnRutaChanged(string? value)
        {
            ActualizarSubcarpetasDisponibles();
        }
        [ObservableProperty] private string? sistemaReferencia;
        [ObservableProperty] private string? formatoDatos;
        [ObservableProperty] private string? linaje;

        // Sección 2: Identificación General
        [ObservableProperty] private string titulo = string.Empty;
        [ObservableProperty] private string? descripcion;
        [ObservableProperty] private DateTime? fechaInicio = DateTime.Today;
        [ObservableProperty] private DateTime? fechaFin;
        [ObservableProperty] private string? representante;
        [ObservableProperty] private string? entidades;
        [ObservableProperty] private string? palabraClave;
        [ObservableProperty] private bool tituloInvalido;

        // Sección 3: Localización Geográfica
        [ObservableProperty] private string? latStr;
        [ObservableProperty] private string? lonStr;
        [ObservableProperty] private DepartamentoItem? selectedDepartamento;
        [ObservableProperty] private MunicipioItem? selectedMunicipio;

        partial void OnTituloChanged(string value)
        {
            if (TituloInvalido && !string.IsNullOrWhiteSpace(value))
                TituloInvalido = false;
        }

        // Eventos para el control MapView en la vista
        public event Action<string?>? MunicipioGeoJsonChanged;
        public event Action<System.Collections.Generic.IReadOnlyList<string>>? MunicipiosGeoJsonListChanged;
        public event Action<double?, double?>? CoordenadasPinChanged;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();
        public ObservableCollection<MunicipioSeleccionadoItem> MunicipiosSeleccionados { get; } = new();

        public bool HasMunicipiosSeleccionados => MunicipiosSeleccionados.Count > 0;
        public string ResumenMunicipiosSeleccionados => MunicipiosSeleccionados.Count switch
        {
            0 => "Ningún municipio agregado aún (se usará el seleccionado en la lista o agregue varios para extensión multidepartamental).",
            1 => $"1 municipio asociado: {MunicipiosSeleccionados[0].EtiquetaCompleta}",
            _ => $"{MunicipiosSeleccionados.Count} municipios asociados ({MunicipiosSeleccionados.Select(m => m.DepartamentoCodigo).Distinct().Count()} departamento(s))"
        };

        // Comandos del formulario
        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }
        public IAsyncRelayCommand EliminarCommand { get; }
        public IRelayCommand SeleccionarCarpetaCommand { get; }
        public IRelayCommand LimpiarFechaFinCommand { get; }
        public IAsyncRelayCommand ExtraerMetadatosCommand { get; }
        public IAsyncRelayCommand AgregarMunicipioCommand { get; }
        public IAsyncRelayCommand<MunicipioSeleccionadoItem> QuitarMunicipioCommand { get; }
        public IAsyncRelayCommand AgregarTodoDepartamentoCommand { get; }

        /// <summary>
        /// Constructor para MODO CREACIÓN de nuevo proyecto.
        /// </summary>
        public FormularioProyectoViewModel(
            CrearProyectoUseCase crearProyectoUseCase,
            IMunicipioRepository municipioRepository,
            ProyectoArchivosService proyectoArchivosService,
            Action navigateBack,
            Action? onProyectoCreado = null,
            INotificationService? notifications = null,
            GuardarSubproyectoUseCase? guardarSubproyectoUseCase = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null,
            EliminarSubproyectoUseCase? eliminarSubproyectoUseCase = null)
        {
            EsModoEdicion = false;
            _crearProyectoUseCase = crearProyectoUseCase;
            _municipioRepository = municipioRepository;
            _archivosService = proyectoArchivosService;
            _navigateBack = navigateBack;
            _onGuardadoExitoso = onProyectoCreado;
            _notifications = notifications;
            _guardarSubproyectoUseCase = guardarSubproyectoUseCase;
            _obtenerSubproyectosUseCase = obtenerSubproyectosUseCase;
            _eliminarSubproyectoUseCase = eliminarSubproyectoUseCase;

            MunicipiosSeleccionados.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasMunicipiosSeleccionados));
                OnPropertyChanged(nameof(ResumenMunicipiosSeleccionados));
                ActualizarMunicipiosDisponiblesSubproyecto();
            };

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);
            EliminarCommand = new AsyncRelayCommand(() => Task.CompletedTask);
            SeleccionarCarpetaCommand = new RelayCommand(SeleccionarCarpeta);
            LimpiarFechaFinCommand = new RelayCommand(() => FechaFin = null);
            ExtraerMetadatosCommand = new AsyncRelayCommand(ExtraerMetadatosAsync);
            AgregarMunicipioCommand = new AsyncRelayCommand(AgregarMunicipioSeleccionadoAsync);
            QuitarMunicipioCommand = new AsyncRelayCommand<MunicipioSeleccionadoItem>(QuitarMunicipioSeleccionadoAsync);
            AgregarTodoDepartamentoCommand = new AsyncRelayCommand(AgregarTodoDepartamentoSeleccionadoAsync);

            MostrarFormularioSubproyectoCommand = new RelayCommand(MostrarFormularioSubproyecto);
            CancelarFormularioSubproyectoCommand = new RelayCommand(CancelarFormularioSubproyecto);
            GuardarNuevoSubproyectoCommand = new RelayCommand(GuardarNuevoSubproyecto);
            EditarSubproyectoCommand = new RelayCommand<SubproyectoItemFormulario>(EditarSubproyecto);
            UbicarSubproyectoEnMapaCommand = new RelayCommand<SubproyectoItemFormulario>(UbicarSubproyectoEnMapa);
            QuitarSubproyectoCommand = new RelayCommand<SubproyectoItemFormulario>(QuitarSubproyecto);
            UsarCoordenadasMapaParaSubproyectoCommand = new RelayCommand(UsarCoordenadasMapaParaSubproyecto);
            SeleccionarSubcarpetaCommand = new RelayCommand(SeleccionarSubcarpeta);
            ImportarSubproyectosCsvCommand = new RelayCommand(ImportarSubproyectosCsv);
            AbrirEditorTablaSubproyectosCommand = new RelayCommand(AbrirEditorTablaSubproyectos);

            _ = CargarDatosInicialesAsync(null);
        }

        /// <summary>
        /// Constructor para MODO EDICIÓN de proyecto existente.
        /// </summary>
        public FormularioProyectoViewModel(
            ActualizarProyectoUseCase actualizarProyectoUseCase,
            IMunicipioRepository municipioRepository,
            ProyectoDetalleDto proyecto,
            Action navigateBack,
            Action? onProyectoEditado = null,
            EliminarProyectoUseCase? eliminarProyectoUseCase = null,
            INotificationService? notifications = null,
            ProyectoArchivosService? archivosService = null,
            ISeguridadService? seguridadService = null,
            GuardarSubproyectoUseCase? guardarSubproyectoUseCase = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null,
            EliminarSubproyectoUseCase? eliminarSubproyectoUseCase = null)
        {
            EsModoEdicion = true;
            _actualizarProyectoUseCase = actualizarProyectoUseCase;
            _municipioRepository = municipioRepository;
            _proyectoOriginal = proyecto;
            _navigateBack = navigateBack;
            _onGuardadoExitoso = onProyectoEditado;
            _eliminarProyectoUseCase = eliminarProyectoUseCase;
            _notifications = notifications;
            _archivosService = archivosService;
            _seguridadService = seguridadService ?? new WindowsSeguridadService();
            _guardarSubproyectoUseCase = guardarSubproyectoUseCase;
            _obtenerSubproyectosUseCase = obtenerSubproyectosUseCase;
            _eliminarSubproyectoUseCase = eliminarSubproyectoUseCase;

            MunicipiosSeleccionados.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasMunicipiosSeleccionados));
                OnPropertyChanged(nameof(ResumenMunicipiosSeleccionados));
                ActualizarMunicipiosDisponiblesSubproyecto();
            };

            if (_archivosService != null && !string.IsNullOrWhiteSpace(proyecto.RutaArchivos))
            {
                PermisosCarpeta = _archivosService.EvaluarPermisosCarpeta(proyecto.RutaArchivos);
            }

            // Cargar datos del proyecto en edición
            Titulo = proyecto.Titulo;
            Descripcion = proyecto.Descripcion;
            FechaInicio = proyecto.FechaInicio ?? DateTime.Today;
            FechaFin = proyecto.FechaFin;
            PalabraClave = proyecto.PalabraClave;
            Ruta = proyecto.RutaArchivos;
            Entidades = proyecto.Entidades;
            Representante = proyecto.Representante;
            SistemaReferencia = proyecto.SistemaReferencia;
            FormatoDatos = proyecto.FormatoDatos;
            Linaje = proyecto.Linaje;

            if (proyecto.Lat != 0 || proyecto.Lon != 0)
            {
                LatStr = proyecto.Lat.ToString("F6", CultureInfo.InvariantCulture);
                LonStr = proyecto.Lon.ToString("F6", CultureInfo.InvariantCulture);
            }

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);
            EliminarCommand = new AsyncRelayCommand(EliminarProyectoAsync);
            SeleccionarCarpetaCommand = new RelayCommand(SeleccionarCarpeta);
            LimpiarFechaFinCommand = new RelayCommand(() => FechaFin = null);
            ExtraerMetadatosCommand = new AsyncRelayCommand(ExtraerMetadatosAsync);
            AgregarMunicipioCommand = new AsyncRelayCommand(AgregarMunicipioSeleccionadoAsync);
            QuitarMunicipioCommand = new AsyncRelayCommand<MunicipioSeleccionadoItem>(QuitarMunicipioSeleccionadoAsync);
            AgregarTodoDepartamentoCommand = new AsyncRelayCommand(AgregarTodoDepartamentoSeleccionadoAsync);

            MostrarFormularioSubproyectoCommand = new RelayCommand(MostrarFormularioSubproyecto);
            CancelarFormularioSubproyectoCommand = new RelayCommand(CancelarFormularioSubproyecto);
            GuardarNuevoSubproyectoCommand = new RelayCommand(GuardarNuevoSubproyecto);
            EditarSubproyectoCommand = new RelayCommand<SubproyectoItemFormulario>(EditarSubproyecto);
            UbicarSubproyectoEnMapaCommand = new RelayCommand<SubproyectoItemFormulario>(UbicarSubproyectoEnMapa);
            QuitarSubproyectoCommand = new RelayCommand<SubproyectoItemFormulario>(QuitarSubproyecto);
            UsarCoordenadasMapaParaSubproyectoCommand = new RelayCommand(UsarCoordenadasMapaParaSubproyecto);
            SeleccionarSubcarpetaCommand = new RelayCommand(SeleccionarSubcarpeta);
            ImportarSubproyectosCsvCommand = new RelayCommand(ImportarSubproyectosCsv);
            AbrirEditorTablaSubproyectosCommand = new RelayCommand(AbrirEditorTablaSubproyectos);

            _ = CargarDatosInicialesAsync(proyecto.MunicipioCodigo);
        }

        // ═══════════════════════════════════════════════════════════════
        // CARGA Y CASCADA DE MUNICIPIOS (DIVIPOLA + MULTIDEPARTAMENTAL)
        // ═══════════════════════════════════════════════════════════════


        // ═══════════════════════════════════════════════════════════════
        // SECCIÓN 4: SUBPROYECTOS Y FRENTES DE TRABAJO
        // ═══════════════════════════════════════════════════════════════
        public ObservableCollection<SubproyectoItemFormulario> Subproyectos { get; } = new();
        private readonly List<int> _subproyectosAEliminar = new();
        public ObservableCollection<string> SubcarpetasDisponibles { get; } = new();
        public ObservableCollection<MunicipioItem> MunicipiosDisponiblesSubproyecto { get; } = new();

        public bool HasSubproyectos => Subproyectos.Count > 0;
        public bool NoHaySubproyectos => Subproyectos.Count == 0;
        public int TotalSubproyectos => Subproyectos.Count;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BotonAgregarSubproyectoVisible))]
        private bool formularioSubproyectoVisible;

        public bool BotonAgregarSubproyectoVisible => !FormularioSubproyectoVisible;

        [ObservableProperty] private string? nuevoSubproyectoNombre;
        [ObservableProperty] private MunicipioItem? nuevoSubproyectoMunicipio;
        [ObservableProperty] private string? nuevoSubproyectoSubcarpeta;
        [ObservableProperty] private string? nuevoSubproyectoLatStr;
        [ObservableProperty] private string? nuevoSubproyectoLonStr;
        [ObservableProperty] private string? nuevoSubproyectoDescripcion;

        private SubproyectoItemFormulario? _subproyectoEnEdicion;

        public bool EsEdicionSubproyecto => _subproyectoEnEdicion != null;

        public string TituloFormularioSubproyecto => EsEdicionSubproyecto
            ? "✏️ Editar Subproyecto / Frente de Trabajo"
            : "📍 Nuevo Subproyecto / Frente de Trabajo";

        public string TextoBotonGuardarSubproyecto => EsEdicionSubproyecto
            ? "💾 Actualizar Subproyecto"
            : "💾 Añadir a la Lista de Subproyectos";

        public event Action<IReadOnlyList<SubproyectoItemFormulario>>? SubproyectosPinsChanged;
        public event Action<double, double>? CentrarEnSubproyectoSolicitado;

        public IRelayCommand MostrarFormularioSubproyectoCommand { get; }
        public IRelayCommand CancelarFormularioSubproyectoCommand { get; }
        public IRelayCommand GuardarNuevoSubproyectoCommand { get; }
        public IRelayCommand<SubproyectoItemFormulario> EditarSubproyectoCommand { get; }
        public IRelayCommand<SubproyectoItemFormulario> QuitarSubproyectoCommand { get; }
        public IRelayCommand<SubproyectoItemFormulario> UbicarSubproyectoEnMapaCommand { get; }
        public IRelayCommand UsarCoordenadasMapaParaSubproyectoCommand { get; }
        public IRelayCommand SeleccionarSubcarpetaCommand { get; }
        public IRelayCommand ImportarSubproyectosCsvCommand { get; }
        public IRelayCommand AbrirEditorTablaSubproyectosCommand { get; }
        public Func<string, IReadOnlyList<SubproyectoItemFormulario>?>? DialogoImportacionCsvHandler { get; set; }
        public Func<string?>? SelectorArchivoCsvHandler { get; set; }
        public Func<IEnumerable<SubproyectoItemFormulario>, IReadOnlyList<SubproyectoItemFormulario>?>? DialogoEditorTablaHandler { get; set; }
        public Func<SubproyectoItemFormulario, bool?>? DialogoEditarSubproyectoHandler { get; set; }
        public Func<SubproyectoItemFormulario?>? DialogoNuevoSubproyectoHandler { get; set; }
        public IReadOnlyList<MunicipioItem>? TodosMunicipiosCatalogo { get; private set; }

        private void ActualizarTextosFormularioSubproyecto()
        {
            OnPropertyChanged(nameof(EsEdicionSubproyecto));
            OnPropertyChanged(nameof(TituloFormularioSubproyecto));
            OnPropertyChanged(nameof(TextoBotonGuardarSubproyecto));
        }

        private void MostrarFormularioSubproyecto()
        {
            _subproyectoEnEdicion = null;
            ActualizarTextosFormularioSubproyecto();
            ActualizarSubcarpetasDisponibles();
            ActualizarMunicipiosDisponiblesSubproyecto();

            if (NuevoSubproyectoMunicipio == null)
            {
                NuevoSubproyectoMunicipio = MunicipiosDisponiblesSubproyecto.FirstOrDefault(m => m.Codigo == SelectedMunicipio?.Codigo)
                    ?? MunicipiosDisponiblesSubproyecto.FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(NuevoSubproyectoLatStr) && !string.IsNullOrWhiteSpace(LatStr))
            {
                NuevoSubproyectoLatStr = LatStr;
                NuevoSubproyectoLonStr = LonStr;
            }

            // 1. Delegado para pruebas unitarias / inyección desacoplada
            if (DialogoNuevoSubproyectoHandler != null)
            {
                var nuevo = DialogoNuevoSubproyectoHandler();
                if (nuevo != null)
                {
                    Subproyectos.Add(nuevo);
                    OnPropertyChanged(nameof(HasSubproyectos));
                    OnPropertyChanged(nameof(NoHaySubproyectos));
                    OnPropertyChanged(nameof(TotalSubproyectos));
                    NotificarCambioSubproyectosPins();
                    _notifications?.ShowInfo($"Subproyecto '{nuevo.Nombre}' añadido a la lista.", "Subproyecto Preparado");
                }
                return;
            }

            // 2. Ventana emergente modal en entorno WPF interactivo
            if (System.Windows.Application.Current?.MainWindow is MainWindow)
            {
                var nuevo = MostrarDialogoNuevoSubproyecto();
                if (nuevo != null)
                {
                    Subproyectos.Add(nuevo);
                    OnPropertyChanged(nameof(HasSubproyectos));
                    OnPropertyChanged(nameof(NoHaySubproyectos));
                    OnPropertyChanged(nameof(TotalSubproyectos));
                    NotificarCambioSubproyectosPins();
                    _notifications?.ShowInfo($"Subproyecto '{nuevo.Nombre}' añadido a la lista.", "Subproyecto Preparado");
                }
            }
            else
            {
                // Fallback para entornos de prueba sin UI / Application.Current
                FormularioSubproyectoVisible = true;
            }
        }

        private SubproyectoItemFormulario? MostrarDialogoNuevoSubproyecto()
        {
            try
            {
                var vm = new EditarSubproyectoViewModel(
                    subproyecto: null,
                    Ruta,
                    SubcarpetasDisponibles,
                    MunicipiosDisponiblesSubproyecto,
                    TodosMunicipiosCatalogo,
                    LatStr,
                    LonStr);

                var window = new Views.EditarSubproyectoWindow(vm);
                if (System.Windows.Application.Current?.MainWindow != null &&
                    System.Windows.Application.Current.MainWindow != window)
                {
                    window.Owner = System.Windows.Application.Current.MainWindow;
                }

                if (window.ShowDialog() == true)
                {
                    var nuevo = vm.CrearNuevoSubproyecto();
                    nuevo.IdProyecto = IdProyecto ?? 0;
                    return nuevo;
                }
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error al abrir ventana emergente de creación de subproyecto", ex);
                _notifications?.ShowError($"Error abriendo editor de subproyecto: {ex.Message}", "Nuevo Subproyecto");
                return null;
            }
        }

        public void EditarSubproyecto(SubproyectoItemFormulario? subproyecto)
        {
            if (subproyecto == null) return;

            _subproyectoEnEdicion = subproyecto;
            ActualizarTextosFormularioSubproyecto();
            ActualizarSubcarpetasDisponibles();
            ActualizarMunicipiosDisponiblesSubproyecto();

            NuevoSubproyectoNombre = subproyecto.Nombre;
            NuevoSubproyectoSubcarpeta = subproyecto.Subcarpeta;
            NuevoSubproyectoMunicipio = MunicipiosDisponiblesSubproyecto.FirstOrDefault(m => m.Codigo == subproyecto.MpioCdpmp)
                ?? (string.IsNullOrWhiteSpace(subproyecto.MpioCdpmp) ? null : new MunicipioItem(subproyecto.MpioCdpmp, subproyecto.MunicipioNombre ?? subproyecto.MpioCdpmp));

            NuevoSubproyectoLatStr = subproyecto.Lat != 0 ? subproyecto.Lat.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
            NuevoSubproyectoLonStr = subproyecto.Lon != 0 ? subproyecto.Lon.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
            NuevoSubproyectoDescripcion = subproyecto.Descripcion;

            if (subproyecto.Lon != 0 || subproyecto.Lat != 0)
            {
                CentrarEnSubproyectoSolicitado?.Invoke(subproyecto.Lon, subproyecto.Lat);
            }

            // 1. Delegado para pruebas unitarias / inyección desacoplada
            if (DialogoEditarSubproyectoHandler != null)
            {
                var editado = DialogoEditarSubproyectoHandler(subproyecto);
                if (editado == true)
                {
                    subproyecto.EsModificado = true;
                    subproyecto.NotificarTodos();
                    NotificarCambioSubproyectosPins();
                    _notifications?.ShowInfo($"Subproyecto '{subproyecto.Nombre}' actualizado.", "Subproyecto Modificado");
                }
                return;
            }

            // 2. Ventana emergente modal en entorno WPF interactivo
            if (System.Windows.Application.Current?.MainWindow is MainWindow)
            {
                var editado = MostrarDialogoEditarSubproyecto(subproyecto);
                if (editado == true)
                {
                    subproyecto.EsModificado = true;
                    subproyecto.NotificarTodos();
                    NotificarCambioSubproyectosPins();
                    _notifications?.ShowInfo($"Subproyecto '{subproyecto.Nombre}' actualizado.", "Subproyecto Modificado");
                }
            }
            else
            {
                // Fallback para entornos de prueba sin UI / Application.Current
                FormularioSubproyectoVisible = true;
            }
        }

        private bool? MostrarDialogoEditarSubproyecto(SubproyectoItemFormulario subproyecto)
        {
            try
            {
                var vm = new EditarSubproyectoViewModel(
                    subproyecto,
                    Ruta,
                    SubcarpetasDisponibles,
                    MunicipiosDisponiblesSubproyecto,
                    TodosMunicipiosCatalogo,
                    LatStr,
                    LonStr);

                var window = new Views.EditarSubproyectoWindow(vm);
                if (System.Windows.Application.Current?.MainWindow != null &&
                    System.Windows.Application.Current.MainWindow != window)
                {
                    window.Owner = System.Windows.Application.Current.MainWindow;
                }

                if (window.ShowDialog() == true)
                {
                    vm.AplicarCambiosASubproyecto(subproyecto);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error al abrir ventana emergente de edición de subproyecto", ex);
                _notifications?.ShowError($"Error abriendo editor de subproyecto: {ex.Message}", "Editor de Subproyecto");
                return null;
            }
        }

        public void UbicarSubproyectoEnMapa(SubproyectoItemFormulario? subproyecto)
        {
            if (subproyecto == null) return;

            if (subproyecto.Lon != 0 || subproyecto.Lat != 0)
            {
                CentrarEnSubproyectoSolicitado?.Invoke(subproyecto.Lon, subproyecto.Lat);
                _notifications?.ShowInfo($"Ubicación centrada en: {subproyecto.Nombre} ({subproyecto.CoordenadasTexto})", "Mapa");
            }
            else
            {
                _notifications?.ShowWarning($"El subproyecto '{subproyecto.Nombre}' no cuenta con coordenadas geográficas.", "Sin Coordenadas");
            }
        }

        private void CancelarFormularioSubproyecto()
        {
            _subproyectoEnEdicion = null;
            ActualizarTextosFormularioSubproyecto();
            FormularioSubproyectoVisible = false;
            NuevoSubproyectoNombre = null;
            NuevoSubproyectoSubcarpeta = null;
            NuevoSubproyectoLatStr = null;
            NuevoSubproyectoLonStr = null;
            NuevoSubproyectoDescripcion = null;
        }

        private void UsarCoordenadasMapaParaSubproyecto()
        {
            if (!string.IsNullOrWhiteSpace(LatStr) && !string.IsNullOrWhiteSpace(LonStr))
            {
                NuevoSubproyectoLatStr = LatStr;
                NuevoSubproyectoLonStr = LonStr;
                _notifications?.ShowInfo($"Coordenadas copiadas del mapa ({LatStr}, {LonStr}).", "Coordenadas Asignadas");
            }
            else
            {
                _notifications?.ShowWarning("Haga clic primero en el mapa en la Sección 3 para marcar el punto deseado.", "Sin Punto Seleccionado");
            }
        }

        private void GuardarNuevoSubproyecto()
        {
            if (string.IsNullOrWhiteSpace(NuevoSubproyectoNombre))
            {
                _notifications?.ShowWarning("El nombre del subproyecto es obligatorio.", "Validación");
                return;
            }

            if (string.IsNullOrWhiteSpace(NuevoSubproyectoSubcarpeta))
            {
                _notifications?.ShowWarning("Debe indicar o seleccionar la subcarpeta asociada al subproyecto.", "Validación");
                return;
            }

            double lat = 0;
            double lon = 0;
            var latNorm = NuevoSubproyectoLatStr?.Replace(',', '.');
            var lonNorm = NuevoSubproyectoLonStr?.Replace(',', '.');

            if (!string.IsNullOrWhiteSpace(latNorm) && !double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out lat))
            {
                _notifications?.ShowWarning("La latitud debe ser un número decimal válido.", "Validación");
                return;
            }

            if (!string.IsNullOrWhiteSpace(lonNorm) && !double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
            {
                _notifications?.ShowWarning("La longitud debe ser un número decimal válido.", "Validación");
                return;
            }

            string subcarpetaNorm = NuevoSubproyectoSubcarpeta.Trim().Replace('/', '\\').Trim('\\');
            string? nombreMpioLimpio = NuevoSubproyectoMunicipio?.Nombre;
            if (!string.IsNullOrWhiteSpace(NuevoSubproyectoMunicipio?.Codigo))
            {
                var mSel = MunicipiosSeleccionados.FirstOrDefault(m => string.Equals(m.Codigo, NuevoSubproyectoMunicipio.Codigo, StringComparison.OrdinalIgnoreCase));
                if (mSel != null)
                {
                    nombreMpioLimpio = mSel.Nombre;
                }
            }

            if (_subproyectoEnEdicion != null)
            {
                _subproyectoEnEdicion.Nombre = NuevoSubproyectoNombre.Trim();
                _subproyectoEnEdicion.MpioCdpmp = NuevoSubproyectoMunicipio?.Codigo;
                _subproyectoEnEdicion.MunicipioNombre = nombreMpioLimpio;
                _subproyectoEnEdicion.Subcarpeta = subcarpetaNorm;
                _subproyectoEnEdicion.Lon = lon;
                _subproyectoEnEdicion.Lat = lat;
                _subproyectoEnEdicion.Descripcion = NuevoSubproyectoDescripcion?.Trim();
                _subproyectoEnEdicion.EsModificado = true;
                _subproyectoEnEdicion.NotificarTodos();

                _notifications?.ShowInfo($"Subproyecto '{_subproyectoEnEdicion.Nombre}' actualizado.", "Subproyecto Modificado");
            }
            else
            {
                var item = new SubproyectoItemFormulario
                {
                    IdSubproyecto = 0,
                    IdProyecto = IdProyecto ?? 0,
                    Nombre = NuevoSubproyectoNombre.Trim(),
                    MpioCdpmp = NuevoSubproyectoMunicipio?.Codigo,
                    MunicipioNombre = nombreMpioLimpio,
                    Subcarpeta = subcarpetaNorm,
                    Lon = lon,
                    Lat = lat,
                    Descripcion = NuevoSubproyectoDescripcion?.Trim(),
                    EsModificado = true
                };

                Subproyectos.Add(item);
                _notifications?.ShowInfo($"Subproyecto '{item.Nombre}' añadido a la lista.", "Subproyecto Preparado");
            }

            OnPropertyChanged(nameof(HasSubproyectos));
            OnPropertyChanged(nameof(NoHaySubproyectos));
            OnPropertyChanged(nameof(TotalSubproyectos));

            NotificarCambioSubproyectosPins();
            CancelarFormularioSubproyecto();
        }

        private void QuitarSubproyecto(SubproyectoItemFormulario? subproyecto)
        {
            if (subproyecto == null) return;

            if (subproyecto.IdSubproyecto > 0)
            {
                _subproyectosAEliminar.Add(subproyecto.IdSubproyecto);
            }

            if (_subproyectoEnEdicion == subproyecto)
            {
                CancelarFormularioSubproyecto();
            }

            Subproyectos.Remove(subproyecto);
            OnPropertyChanged(nameof(HasSubproyectos));
            OnPropertyChanged(nameof(NoHaySubproyectos));
            OnPropertyChanged(nameof(TotalSubproyectos));

            NotificarCambioSubproyectosPins();
        }

        public void NotificarCambioSubproyectosPins()
        {
            SubproyectosPinsChanged?.Invoke(Subproyectos.ToList());
        }

        private void SeleccionarSubcarpeta()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar subcarpeta del subproyecto / frente de trabajo",
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(Ruta) && Directory.Exists(Ruta))
                {
                    dialog.InitialDirectory = Ruta;
                }

                if (dialog.ShowDialog() == true)
                {
                    var seleccion = dialog.FolderName;
                    string subcarpetaRel;

                    if (!string.IsNullOrWhiteSpace(Ruta) && seleccion.StartsWith(Ruta, StringComparison.OrdinalIgnoreCase))
                    {
                        subcarpetaRel = Path.GetRelativePath(Ruta, seleccion);
                        if (subcarpetaRel == ".") subcarpetaRel = "";
                    }
                    else
                    {
                        subcarpetaRel = Path.GetFileName(seleccion) ?? seleccion;
                    }

                    NuevoSubproyectoSubcarpeta = subcarpetaRel;

                    // Agregar a la lista disponible si no existe
                    if (!string.IsNullOrWhiteSpace(subcarpetaRel) && !SubcarpetasDisponibles.Contains(subcarpetaRel))
                    {
                        SubcarpetasDisponibles.Insert(0, subcarpetaRel);
                    }

                    // Si el nombre del subproyecto aún no fue asignado, sugerir el nombre de la carpeta seleccionada
                    if (string.IsNullOrWhiteSpace(NuevoSubproyectoNombre))
                    {
                        NuevoSubproyectoNombre = Path.GetFileName(seleccion);
                    }
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al seleccionar subcarpeta: {ex.Message}", "Explorador");
            }
        }

        public void ActualizarSubcarpetasDisponibles()
        {
            SubcarpetasDisponibles.Clear();
            if (string.IsNullOrWhiteSpace(Ruta) || !Directory.Exists(Ruta)) return;

            try
            {
                var rootDir = new DirectoryInfo(Ruta);
                var listaRelativas = new List<string>();

                void EscanearRecursivo(DirectoryInfo dir, int profundidadActual, int maxProfundidad)
                {
                    if (profundidadActual > maxProfundidad || listaRelativas.Count >= 200) return;

                    DirectoryInfo[] subdirs;
                    try
                    {
                        subdirs = dir.GetDirectories();
                    }
                    catch
                    {
                        return;
                    }

                    foreach (var sub in subdirs.OrderBy(d => d.Name))
                    {
                        if (listaRelativas.Count >= 200) break;

                        // Omitir carpetas ocultas, del sistema o contenedores internos de FileGDB
                        if (sub.Attributes.HasFlag(FileAttributes.Hidden) ||
                            sub.Name.StartsWith('.') ||
                            sub.Name.StartsWith('$') ||
                            sub.Name.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var rutaRelativa = Path.GetRelativePath(rootDir.FullName, sub.FullName);
                        listaRelativas.Add(rutaRelativa);

                        EscanearRecursivo(sub, profundidadActual + 1, maxProfundidad);
                    }
                }

                EscanearRecursivo(rootDir, 1, 4);

                foreach (var rel in listaRelativas)
                {
                    SubcarpetasDisponibles.Add(rel);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Error escaneando subdirectorios en {Ruta}: {ex.Message}");
            }
        }

        public void AbrirEditorTablaSubproyectos()
        {
            try
            {
                IReadOnlyList<SubproyectoItemFormulario>? subproyectosActualizados = null;

                if (DialogoEditorTablaHandler != null)
                {
                    subproyectosActualizados = DialogoEditorTablaHandler(Subproyectos);
                }
                else
                {
                    subproyectosActualizados = MostrarDialogoEditorTablaSubproyectos();
                }

                if (subproyectosActualizados != null)
                {
                    AplicarSubproyectosDesdeTabla(subproyectosActualizados);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error en el editor tabular de subproyectos", ex);
                _notifications?.ShowError($"Error en el editor de subproyectos: {ex.Message}", "Editor de Subproyectos");
            }
        }

        private IReadOnlyList<SubproyectoItemFormulario>? MostrarDialogoEditorTablaSubproyectos()
        {
            if (System.Windows.Application.Current?.MainWindow is not MainWindow)
            {
                return null;
            }

            var vm = new ImportarSubproyectosCsvViewModel(
                IdProyecto ?? 0,
                MunicipiosDisponiblesSubproyecto,
                Ruta,
                SubcarpetasDisponibles,
                TodosMunicipiosCatalogo);

            vm.CargarSubproyectosExistentes(Subproyectos);

            var window = new Views.ImportarSubproyectosCsvWindow(vm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                return vm.ObtenerSubproyectosAprobados();
            }
            return null;
        }

        public void AplicarSubproyectosDesdeTabla(IReadOnlyList<SubproyectoItemFormulario> nuevos)
        {
            var nuevosIds = new HashSet<int>(nuevos.Where(s => s.IdSubproyecto > 0).Select(s => s.IdSubproyecto));
            foreach (var existente in Subproyectos)
            {
                if (existente.IdSubproyecto > 0 && !nuevosIds.Contains(existente.IdSubproyecto))
                {
                    _subproyectosAEliminar.Add(existente.IdSubproyecto);
                }
            }

            Subproyectos.Clear();
            foreach (var sub in nuevos)
            {
                sub.IdProyecto = IdProyecto ?? 0;
                sub.EsModificado = true;
                Subproyectos.Add(sub);
            }

            OnPropertyChanged(nameof(HasSubproyectos));
            OnPropertyChanged(nameof(NoHaySubproyectos));
            OnPropertyChanged(nameof(TotalSubproyectos));

            NotificarCambioSubproyectosPins();
            _notifications?.ShowSuccess(
                $"Se actualizaron {nuevos.Count} subproyectos exitosamente.",
                "Subproyectos Actualizados");
        }

        private void ImportarSubproyectosCsv()
        {
            if (SelectorArchivoCsvHandler == null)
            {
                AbrirEditorTablaSubproyectos();
                return;
            }

            try
            {
                string? rutaArchivo = SelectorArchivoCsvHandler();
                if (string.IsNullOrWhiteSpace(rutaArchivo) || !File.Exists(rutaArchivo))
                    return;

                IReadOnlyList<SubproyectoItemFormulario>? subproyectosImportados = null;

                if (DialogoImportacionCsvHandler != null)
                {
                    subproyectosImportados = DialogoImportacionCsvHandler(rutaArchivo);
                }
                else
                {
                    subproyectosImportados = MostrarDialogoImportacionCsv(rutaArchivo);
                }

                if (subproyectosImportados != null && subproyectosImportados.Count > 0)
                {
                    foreach (var sub in subproyectosImportados)
                    {
                        sub.IdProyecto = IdProyecto ?? 0;
                        sub.EsModificado = true;
                        Subproyectos.Add(sub);
                    }

                    OnPropertyChanged(nameof(HasSubproyectos));
                    OnPropertyChanged(nameof(NoHaySubproyectos));
                    OnPropertyChanged(nameof(TotalSubproyectos));

                    NotificarCambioSubproyectosPins();
                    _notifications?.ShowSuccess(
                        $"Se importaron {subproyectosImportados.Count} subproyectos exitosamente.",
                        "Importación Completada");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Error al importar subproyectos desde CSV/TXT", ex);
                _notifications?.ShowError($"Error durante la importación: {ex.Message}", "Importar CSV");
            }
        }

        private IReadOnlyList<SubproyectoItemFormulario>? MostrarDialogoImportacionCsv(string rutaArchivo)
        {
            if (System.Windows.Application.Current?.MainWindow is not MainWindow)
            {
                return null;
            }

            var vm = new ImportarSubproyectosCsvViewModel(
                IdProyecto ?? 0,
                MunicipiosDisponiblesSubproyecto,
                Ruta,
                SubcarpetasDisponibles,
                TodosMunicipiosCatalogo);
            vm.CargarArchivo(rutaArchivo);

            var window = new Views.ImportarSubproyectosCsvWindow(vm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                return vm.ObtenerSubproyectosAprobados();
            }
            return null;
        }

        public void ActualizarMunicipiosDisponiblesSubproyecto()
        {
            var codigoPrevio = NuevoSubproyectoMunicipio?.Codigo;
            var nombrePrevio = NuevoSubproyectoMunicipio?.Nombre;

            MunicipiosDisponiblesSubproyecto.Clear();

            foreach (var m in MunicipiosSeleccionados)
            {
                if (MunicipiosDisponiblesSubproyecto.All(item => item.Codigo != m.Codigo))
                    MunicipiosDisponiblesSubproyecto.Add(new MunicipioItem(m.Codigo, m.EtiquetaCompleta));
            }

            if (SelectedMunicipio != null && MunicipiosDisponiblesSubproyecto.All(item => item.Codigo != SelectedMunicipio.Codigo))
            {
                MunicipiosDisponiblesSubproyecto.Add(SelectedMunicipio);
            }

            if (MunicipiosDisponiblesSubproyecto.Count == 0)
            {
                foreach (var m in Municipios)
                    MunicipiosDisponiblesSubproyecto.Add(m);
            }

            if (!string.IsNullOrWhiteSpace(codigoPrevio))
            {
                NuevoSubproyectoMunicipio = MunicipiosDisponiblesSubproyecto.FirstOrDefault(m => string.Equals(m.Codigo, codigoPrevio, StringComparison.OrdinalIgnoreCase))
                    ?? new MunicipioItem(codigoPrevio, nombrePrevio ?? codigoPrevio);
            }
        }

        public async Task CargarDatosInicialesAsync(string? municipioCodigo = null)
        {
            _isUpdatingProgrammatically = true;
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                try
                {
                    var todos = await _municipioRepository.ListarTodosMunicipiosAsync();
                    TodosMunicipiosCatalogo = todos.Select(m => new MunicipioItem(m.Codigo, m.Nombre)).ToList();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"No se pudo precargar catálogo completo de municipios: {ex.Message}");
                }
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }

                if (!string.IsNullOrWhiteSpace(municipioCodigo))
                {
                    var codigos = municipioCodigo
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (codigos.Count > 0)
                    {
                        var todosMuns = await _municipioRepository.ListarTodosMunicipiosAsync();
                        var mapaMuns = todosMuns.ToDictionary(m => m.Codigo, m => m.Nombre, StringComparer.OrdinalIgnoreCase);

                        MunicipiosSeleccionados.Clear();
                        foreach (var cod in codigos)
                        {
                            var dptoCod = cod.Length >= 2 ? cod.Substring(0, 2) : "";
                            var dptoNombre = Departamentos.FirstOrDefault(d => d.Codigo == dptoCod)?.Nombre ?? dptoCod;
                            var mpioNombre = mapaMuns.TryGetValue(cod, out var n) ? n : cod;
                            MunicipiosSeleccionados.Add(new MunicipioSeleccionadoItem(cod, mpioNombre, dptoCod, dptoNombre));
                        }

                        var primerCodigo = codigos[0];
                        var primerDpto = primerCodigo.Length >= 2 ? primerCodigo.Substring(0, 2) : "";
                        var dep = Departamentos.FirstOrDefault(d => d.Codigo == primerDpto);
                        if (dep != null)
                        {
                            SelectedDepartamento = dep;
                            var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(dep.Codigo);
                            Municipios.Clear();
                            foreach (var m in muns)
                            {
                                var item = new MunicipioItem(m.Codigo, m.Nombre);
                                Municipios.Add(item);
                                if (m.Codigo == primerCodigo)
                                {
                                    SelectedMunicipio = item;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error cargando departamentos: {ex.Message}", "Departamentos");
            }
            finally
            {
                _isUpdatingProgrammatically = false;
            }

            await ActualizarGeometriasMapaAsync();

            ActualizarSubcarpetasDisponibles();

            if (EsModoEdicion && IdProyecto.HasValue && _obtenerSubproyectosUseCase != null)
            {
                try
                {
                    var subs = await _obtenerSubproyectosUseCase.EjecutarAsync(IdProyecto.Value);
                    Subproyectos.Clear();
                    foreach (var s in subs)
                    {
                        Subproyectos.Add(new SubproyectoItemFormulario
                        {
                            IdSubproyecto = s.IdSubproyecto,
                            IdProyecto = s.IdProyecto,
                            Nombre = s.Nombre,
                            MpioCdpmp = s.MunicipioCodigo,
                            MunicipioNombre = s.MunicipioNombre,
                            Subcarpeta = s.Subcarpeta,
                            Lon = s.Lon,
                            Lat = s.Lat,
                            Descripcion = s.Descripcion
                        });
                    }
                    OnPropertyChanged(nameof(HasSubproyectos));
                    OnPropertyChanged(nameof(NoHaySubproyectos));
                    OnPropertyChanged(nameof(TotalSubproyectos));
                    NotificarCambioSubproyectosPins();
                }
                catch (Exception exSubs)
                {
                    AppLogger.Error("Error al cargar subproyectos existentes en formulario", exSubs);
                }
            }
        }

        private System.Collections.Generic.IReadOnlyList<string> ObtenerCodigosMunicipiosEfectivos()
        {
            if (MunicipiosSeleccionados.Count > 0)
            {
                return MunicipiosSeleccionados.Select(m => m.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (SelectedMunicipio != null && !string.IsNullOrWhiteSpace(SelectedMunicipio.Codigo))
            {
                return new[] { SelectedMunicipio.Codigo };
            }

            return Array.Empty<string>();
        }

        private async Task ActualizarGeometriasMapaAsync()
        {
            var codigos = ObtenerCodigosMunicipiosEfectivos();
            if (codigos.Count == 0)
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                MunicipiosGeoJsonListChanged?.Invoke(Array.Empty<string>());
                return;
            }

            try
            {
                var results = await _municipioRepository.PorCodigosGeoJsonAsync(codigos);
                var geoJsons = results
                    .Where(r => !string.IsNullOrWhiteSpace(r.GeoJson))
                    .Select(r => r.GeoJson!)
                    .ToList();

                MunicipioGeoJsonChanged?.Invoke(geoJsons.FirstOrDefault());
                MunicipiosGeoJsonListChanged?.Invoke(geoJsons);
            }
            catch
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                MunicipiosGeoJsonListChanged?.Invoke(Array.Empty<string>());
            }
        }

        private async Task AgregarMunicipioSeleccionadoAsync()
        {
            if (SelectedMunicipio == null || string.IsNullOrWhiteSpace(SelectedMunicipio.Codigo))
            {
                _notifications?.ShowWarning("Seleccione primero un departamento y un municipio para agregarlo a la lista.", "Agregar Municipio");
                return;
            }

            var dptoCod = SelectedDepartamento?.Codigo ?? (SelectedMunicipio.Codigo.Length >= 2 ? SelectedMunicipio.Codigo.Substring(0, 2) : "");
            var dptoNombre = SelectedDepartamento?.Nombre ?? Departamentos.FirstOrDefault(d => d.Codigo == dptoCod)?.Nombre ?? dptoCod;

            if (MunicipiosSeleccionados.Any(m => string.Equals(m.Codigo, SelectedMunicipio.Codigo, StringComparison.OrdinalIgnoreCase)))
            {
                _notifications?.ShowInfo($"El municipio {SelectedMunicipio.Nombre} ({dptoNombre}) ya está en la lista del proyecto.", "Municipio ya agregado");
                return;
            }

            MunicipiosSeleccionados.Add(new MunicipioSeleccionadoItem(
                SelectedMunicipio.Codigo,
                SelectedMunicipio.Nombre,
                dptoCod,
                dptoNombre));

            await ActualizarGeometriasMapaAsync();
        }

        private async Task AgregarTodoDepartamentoSeleccionadoAsync()
        {
            if (SelectedDepartamento == null || string.IsNullOrWhiteSpace(SelectedDepartamento.Codigo))
            {
                _notifications?.ShowWarning("Seleccione primero un departamento para agregar todos sus municipios.", "Agregar Departamento");
                return;
            }

            try
            {
                var muns = Municipios.Count > 0
                    ? Municipios.Select(m => (m.Codigo, m.Nombre)).ToList()
                    : (await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(SelectedDepartamento.Codigo))
                        .Select(m => (m.Codigo, m.Nombre)).ToList();

                int agregados = 0;
                foreach (var m in muns)
                {
                    if (!MunicipiosSeleccionados.Any(existing => string.Equals(existing.Codigo, m.Codigo, StringComparison.OrdinalIgnoreCase)))
                    {
                        MunicipiosSeleccionados.Add(new MunicipioSeleccionadoItem(
                            m.Codigo,
                            m.Nombre,
                            SelectedDepartamento.Codigo,
                            SelectedDepartamento.Nombre));
                        agregados++;
                    }
                }

                if (agregados > 0)
                {
                    _notifications?.ShowSuccess($"Se agregaron {agregados} municipios del departamento de {SelectedDepartamento.Nombre}.", "Departamento Agregado");
                    await ActualizarGeometriasMapaAsync();
                }
                else
                {
                    _notifications?.ShowInfo($"Todos los municipios de {SelectedDepartamento.Nombre} ya estaban agregados.", "Sin cambios");
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al agregar municipios del departamento: {ex.Message}", "Error");
            }
        }

        private async Task QuitarMunicipioSeleccionadoAsync(MunicipioSeleccionadoItem? item)
        {
            if (item == null) return;
            MunicipiosSeleccionados.Remove(item);
            await ActualizarGeometriasMapaAsync();
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            if (_isUpdatingProgrammatically) return;

            Municipios.Clear();
            SelectedMunicipio = null;

            if (MunicipiosSeleccionados.Count == 0)
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                MunicipiosGeoJsonListChanged?.Invoke(Array.Empty<string>());
                LatStr = null;
                LonStr = null;
                CoordenadasPinChanged?.Invoke(null, null);
            }

            if (value == null || string.IsNullOrEmpty(value.Codigo)) return;

            try
            {
                var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                foreach (var m in muns)
                {
                    Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error cargando municipios: {ex.Message}", "Municipios");
            }
        }

        async partial void OnSelectedMunicipioChanged(MunicipioItem? value)
        {
            if (FormularioSubproyectoVisible)
            {
                ActualizarMunicipiosDisponiblesSubproyecto();
            }

            if (_isUpdatingProgrammatically) return;

            if (value == null || string.IsNullOrEmpty(value.Codigo))
            {
                if (MunicipiosSeleccionados.Count == 0)
                {
                    MunicipioGeoJsonChanged?.Invoke(null);
                    MunicipiosGeoJsonListChanged?.Invoke(Array.Empty<string>());
                }
                return;
            }

            try
            {
                // Si el usuario aún no usa la lista multidepartamental, mantener el comportamiento clásico de 1 municipio
                if (MunicipiosSeleccionados.Count == 0)
                {
                    await ActualizarGeometriasMapaAsync();

                    if (ObtenerCoordenadasActuales(out var lat, out var lon))
                    {
                        var adentro = await _municipioRepository.PuntoEstaEnMunicipioAsync(value.Codigo, lon, lat);
                        if (!adentro)
                        {
                            LatStr = null;
                            LonStr = null;
                            CoordenadasPinChanged?.Invoke(null, null);
                            _notifications?.ShowWarning($"El punto marcado previamente no pertenece a {value.Nombre} y ha sido removido.", "Ubicación");
                        }
                    }
                }
            }
            catch
            {
                if (MunicipiosSeleccionados.Count == 0)
                {
                    MunicipioGeoJsonChanged?.Invoke(null);
                    MunicipiosGeoJsonListChanged?.Invoke(Array.Empty<string>());
                }
            }
        }

        public bool ObtenerCoordenadasActuales(out double lat, out double lon)
        {
            lat = 0;
            lon = 0;
            var latNorm = LatStr?.Replace(',', '.');
            var lonNorm = LonStr?.Replace(',', '.');

            if (!string.IsNullOrWhiteSpace(latNorm) && !string.IsNullOrWhiteSpace(lonNorm)
                && double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var o))
            {
                lat = l;
                lon = o;
                return true;
            }
            return false;
        }

        public async Task<bool> ProcesarClickMapaAsync(double lat, double lon)
        {
            try
            {
                var ubicacion = await _municipioRepository.ObtenerPorPuntoAsync(lon, lat);
                if (ubicacion == null)
                {
                    _notifications?.ShowWarning("El punto seleccionado no se encuentra dentro del territorio de ningún municipio registrado.", "Ubicación Inválida");
                    return false;
                }

                if (FormularioSubproyectoVisible)
                {
                    NuevoSubproyectoLatStr = lat.ToString("F6", CultureInfo.InvariantCulture);
                    NuevoSubproyectoLonStr = lon.ToString("F6", CultureInfo.InvariantCulture);

                    if (SelectedMunicipio == null && MunicipiosSeleccionados.Count == 0)
                    {
                        await AsignarUbicacionDetectadaAsync(ubicacion);
                        SetCoordenadas(lat, lon);
                    }

                    ActualizarMunicipiosDisponiblesSubproyecto();
                    var matchMuni = MunicipiosDisponiblesSubproyecto.FirstOrDefault(m => string.Equals(m.Codigo, ubicacion.MunicipioCodigo, StringComparison.OrdinalIgnoreCase))
                        ?? new MunicipioItem(ubicacion.MunicipioCodigo, ubicacion.MunicipioNombre);
                    NuevoSubproyectoMunicipio = matchMuni;
                    return true;
                }

                // Si ya pertenece a alguno de los municipios seleccionados en un proyecto multidepartamental/multimunicipal
                if (MunicipiosSeleccionados.Any(m => string.Equals(m.Codigo, ubicacion.MunicipioCodigo, StringComparison.OrdinalIgnoreCase)))
                {
                    SetCoordenadas(lat, lon);
                    return true;
                }

                if (SelectedMunicipio == null && MunicipiosSeleccionados.Count == 0)
                {
                    await AsignarUbicacionDetectadaAsync(ubicacion);
                    SetCoordenadas(lat, lon);
                    _notifications?.ShowInfo($"Municipio detectado: {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}).", "Ubicación Asignada");
                    return true;
                }

                if (MunicipiosSeleccionados.Count == 0 && SelectedMunicipio?.Codigo == ubicacion.MunicipioCodigo)
                {
                    SetCoordenadas(lat, lon);
                    return true;
                }

                if (MunicipiosSeleccionados.Count > 0)
                {
                    var msgMulti = $"El punto seleccionado pertenece a {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}), que aún no está en la lista de municipios del proyecto.\n\n¿Desea agregar {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}) al proyecto y ubicar allí el punto?";
                    var resMulti = MessageBox.Show(msgMulti, "Agregar municipio al proyecto", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (resMulti == MessageBoxResult.Yes)
                    {
                        MunicipiosSeleccionados.Add(new MunicipioSeleccionadoItem(
                            ubicacion.MunicipioCodigo,
                            ubicacion.MunicipioNombre,
                            ubicacion.DepartamentoCodigo,
                            ubicacion.DepartamentoNombre));
                        await AsignarUbicacionDetectadaAsync(ubicacion);
                        await ActualizarGeometriasMapaAsync();
                        SetCoordenadas(lat, lon);
                        return true;
                    }
                    return false;
                }

                var msg = $"El punto seleccionado pertenece a {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}), pero actualmente tiene seleccionado {SelectedMunicipio!.Nombre}.\n\n¿Desea cambiar el municipio del proyecto a {ubicacion.MunicipioNombre}?";
                var res = MessageBox.Show(msg, "Punto fuera del municipio", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    await AsignarUbicacionDetectadaAsync(ubicacion);
                    SetCoordenadas(lat, lon);
                    return true;
                }
                else
                {
                    _notifications?.ShowWarning($"Se mantuvo el municipio actual ({SelectedMunicipio.Nombre}). El punto fuera del límite no fue asignado.", "Punto Rechazado");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al verificar la ubicación geográfica: {ex.Message}", "Error de Ubicación");
                return false;
            }
        }

        private async Task AsignarUbicacionDetectadaAsync(MunicipioUbicacionDto ubicacion)
        {
            _isUpdatingProgrammatically = true;
            try
            {
                if (SelectedDepartamento?.Codigo != ubicacion.DepartamentoCodigo)
                {
                    var dep = Departamentos.FirstOrDefault(d => d.Codigo == ubicacion.DepartamentoCodigo);
                    if (dep == null && Departamentos.Count == 0)
                    {
                        await CargarDatosInicialesAsync(ubicacion.MunicipioCodigo);
                        return;
                    }

                    if (dep != null)
                    {
                        SelectedDepartamento = dep;
                        var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(dep.Codigo);
                        Municipios.Clear();
                        foreach (var m in muns)
                        {
                            Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                        }
                    }
                }
                else if (Municipios.Count == 0)
                {
                    var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(ubicacion.DepartamentoCodigo);
                    Municipios.Clear();
                    foreach (var m in muns)
                    {
                        Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                    }
                }

                SelectedMunicipio = Municipios.FirstOrDefault(m => m.Codigo == ubicacion.MunicipioCodigo);
                if (SelectedMunicipio != null && MunicipiosSeleccionados.Count == 0)
                {
                    var results = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { SelectedMunicipio.Codigo });
                    var firstGeo = results.FirstOrDefault()?.GeoJson;
                    MunicipioGeoJsonChanged?.Invoke(firstGeo);
                    MunicipiosGeoJsonListChanged?.Invoke(firstGeo != null ? new[] { firstGeo } : Array.Empty<string>());
                }
            }
            finally
            {
                _isUpdatingProgrammatically = false;
            }
        }

        public void SetCoordenadas(double lat, double lon)
        {
            LatStr = lat.ToString("F6", CultureInfo.InvariantCulture);
            LonStr = lon.ToString("F6", CultureInfo.InvariantCulture);
            CoordenadasPinChanged?.Invoke(lat, lon);

            if (FormularioSubproyectoVisible)
            {
                NuevoSubproyectoLatStr = LatStr;
                NuevoSubproyectoLonStr = LonStr;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // METADATOS TÉCNICOS ISO 19115 Y GESTIÓN DE ARCHIVOS
        // ═══════════════════════════════════════════════════════════════

        private async Task ExtraerMetadatosAsync()
        {
            if (string.IsNullOrWhiteSpace(Ruta) || !Directory.Exists(Ruta))
            {
                _notifications?.ShowError("La ruta de archivos no existe o no es válida.", "Error de Extracción");
                return;
            }

            try
            {
                var extractor = new Geomatica.Infrastructure.Gis.Services.Iso19115MetadataExtractor();

                IEnumerable<string>? rutasArchivos = null;
                IEnumerable<string>? directoriosGdb = null;

                if (_archivosService != null && _archivosService.TryObtenerIndice(Ruta, out var idx) && idx != null)
                {
                    rutasArchivos = idx.ObtenerRutasFisicasPorExtensiones(
                        CatalogoFormatosGis.ExtensionesVectoriales
                        .Concat(CatalogoFormatosGis.ExtensionesRaster)
                        .Concat(CatalogoFormatosGis.ExtensionesLidar)
                        .Concat(CatalogoFormatosGis.ExtensionesCad));

                    directoriosGdb = idx.ObtenerRutasFisicasPorExtension(".gdb");
                }

                var result = await extractor.ExtraerDesdeRutaAsync(Ruta, rutasArchivos, directoriosGdb);

                bool updated = false;

                if (!string.IsNullOrWhiteSpace(result.SistemaReferencia))
                {
                    SistemaReferencia = result.SistemaReferencia;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(result.FormatoDatos))
                {
                    FormatoDatos = result.FormatoDatos;
                    updated = true;
                }

                if (!string.IsNullOrWhiteSpace(result.Linaje))
                {
                    Linaje = result.Linaje;
                    updated = true;
                }

                if (updated)
                {
                    _notifications?.ShowSuccess("Metadatos extraídos correctamente de los archivos físicos.", "Extracción Exitosa");
                }
                else
                {
                    _notifications?.ShowInfo("No se encontraron metadatos espaciales en la ruta especificada.", "Sin Metadatos");
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Ocurrió un error al extraer metadatos: {ex.Message}", "Error");
            }
        }

        private void SeleccionarCarpeta()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para el proyecto",
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(Ruta) && Directory.Exists(Ruta))
                {
                    dialog.InitialDirectory = Ruta;
                }

                if (dialog.ShowDialog() == true)
                {
                    Ruta = dialog.FolderName;

                    // Si estamos creando un proyecto y el título está vacío, sugerir el nombre de la carpeta
                    if (!EsModoEdicion && string.IsNullOrWhiteSpace(Titulo))
                    {
                        try
                        {
                            var dirName = Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            if (!string.IsNullOrWhiteSpace(dirName))
                            {
                                Titulo = dirName.Replace('_', ' ').Replace('-', ' ');
                            }
                        }
                        catch { }
                    }

                    // Auto-detectar metadatos espaciales en segundo plano
                    _ = ExtraerMetadatosAsync();
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al abrir el selector de carpetas: {ex.Message}", "Selector de Carpetas");
            }
        }

        /// <summary>
        /// Delegado inyectable para consultar al usuario sobre la creación de la estructura de carpetas estándar.
        /// Por defecto invoca MessageBox; puede ser interceptado en pruebas unitarias.
        /// </summary>
        public Func<string, bool>? ConfirmarCreacionEstructuraHandler { get; set; }

        private bool ConsultarDeseaCrearEstructura(string ruta)
        {
            if (ConfirmarCreacionEstructuraHandler != null)
                return ConfirmarCreacionEstructuraHandler(ruta);

            var result = MessageBox.Show(
                $"La carpeta seleccionada para el proyecto no contiene subcarpetas de organización:\n\n\"{ruta}\"\n\n¿Desea crear la estructura de carpetas estándar del proyecto?\n\n• Datos_Espaciales/\n• Documentos/\n• Entregables/\n• Otros/",
                "Estructura de Carpetas del Proyecto",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        // ═══════════════════════════════════════════════════════════════
        // GUARDAR / ACTUALIZAR / ELIMINAR
        // ═══════════════════════════════════════════════════════════════

        private async Task GuardarAsync()
        {
            if (!PuedeEscribir)
            {
                _notifications?.ShowError(TooltipGuardar, "Acceso Restringido");
                return;
            }

            TituloInvalido = string.IsNullOrWhiteSpace(Titulo);
            if (TituloInvalido)
            {
                _notifications?.ShowWarning("El título es obligatorio.", "Validación");
                return;
            }

            var codigosMunicipios = ObtenerCodigosMunicipiosEfectivos();
            if (codigosMunicipios.Count == 0)
            {
                _notifications?.ShowWarning("Debe seleccionar al menos un municipio.", "Validación");
                return;
            }

            string codigosConcatenados = string.Join(",", codigosMunicipios);

            double? lat = null;
            double? lon = null;
            var latNorm = LatStr?.Replace(',', '.');
            var lonNorm = LonStr?.Replace(',', '.');

            if (!string.IsNullOrWhiteSpace(latNorm) && double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)) lat = l;
            if (!string.IsNullOrWhiteSpace(lonNorm) && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) lon = o;

            if ((lat.HasValue && !lon.HasValue) || (!lat.HasValue && lon.HasValue))
            {
                _notifications?.ShowWarning("Debe especificar tanto latitud como longitud, o dejar ambas vacías.", "Validación");
                return;
            }

            string? geom = null;
            if (lon.HasValue && lat.HasValue)
            {
                bool estaEnAlgunMunicipio = false;
                foreach (var cod in codigosMunicipios)
                {
                    if (await _municipioRepository.PuntoEstaEnMunicipioAsync(cod, lon.Value, lat.Value))
                    {
                        estaEnAlgunMunicipio = true;
                        break;
                    }
                }

                if (!estaEnAlgunMunicipio)
                {
                    var nombres = MunicipiosSeleccionados.Count > 0
                        ? string.Join(", ", MunicipiosSeleccionados.Select(m => m.EtiquetaCompleta))
                        : SelectedMunicipio?.Nombre ?? codigosConcatenados;
                    _notifications?.ShowError($"Las coordenadas indicadas ({lat.Value:F6}, {lon.Value:F6}) no pertenecen a ninguno de los municipios asociados al proyecto ({nombres}). Corrija la ubicación o agregue el municipio correspondiente antes de guardar.", "Error de Validación Espacial");
                    return;
                }

                geom = string.Format(CultureInfo.InvariantCulture, "POINT({0} {1})", lon.Value, lat.Value);
            }

            if (FechaInicio.HasValue && FechaFin.HasValue && FechaFin.Value < FechaInicio.Value)
            {
                _notifications?.ShowWarning("La fecha de finalización no puede ser anterior a la fecha de inicio.", "Validación");
                return;
            }

            // Gestión de carpetas sólo en modo creación
            if (!EsModoEdicion && !string.IsNullOrWhiteSpace(Ruta) && _archivosService != null)
            {
                try
                {
                    _archivosService.GestionarEstructuraCarpetas(Ruta, () => ConsultarDeseaCrearEstructura(Ruta));
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError(ex.Message, "Error creando carpetas");
                    return;
                }
            }

            try
            {
                string usuarioActual = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
                string equipoActual = Environment.MachineName;

                int nuevoId = 0;
                if (EsModoEdicion)
                {
                    if (_actualizarProyectoUseCase == null || !IdProyecto.HasValue)
                    {
                        _notifications?.ShowError("El caso de uso de actualización no está configurado.", "Error");
                        return;
                    }

                    await _actualizarProyectoUseCase.EjecutarAsync(
                        IdProyecto.Value,
                        Titulo,
                        Descripcion,
                        FechaInicio,
                        PalabraClave,
                        Ruta,
                        geom,
                        codigosConcatenados,
                        usuarioActual,
                        equipoActual,
                        FechaFin,
                        Entidades,
                        Representante,
                        SistemaReferencia,
                        FormatoDatos,
                        Linaje
                    );

                    _notifications?.ShowSuccess("Proyecto actualizado exitosamente.", "Proyecto Guardado");
                }
                else
                {
                    if (_crearProyectoUseCase == null)
                    {
                        _notifications?.ShowError("El caso de uso de creación no está configurado.", "Error");
                        return;
                    }

                    nuevoId = await _crearProyectoUseCase.EjecutarAsync(
                        Titulo,
                        Descripcion,
                        FechaInicio,
                        PalabraClave,
                        Ruta,
                        geom,
                        codigosConcatenados,
                        usuarioActual,
                        equipoActual,
                        FechaFin,
                        Entidades,
                        Representante,
                        SistemaReferencia,
                        FormatoDatos,
                        Linaje
                    );

                    _notifications?.ShowSuccess("Proyecto creado exitosamente.", "Proyecto Creado");
                }

                // Guardar y sincronizar subproyectos
                int idProyectoDestino = EsModoEdicion ? (IdProyecto ?? 0) : nuevoId;
                if (idProyectoDestino > 0)
                {
                    if (_eliminarSubproyectoUseCase != null && _subproyectosAEliminar.Count > 0)
                    {
                        foreach (var subId in _subproyectosAEliminar)
                        {
                            try { await _eliminarSubproyectoUseCase.EjecutarAsync(subId); }
                            catch (Exception exDel) { AppLogger.Error($"Error eliminando subproyecto {subId}", exDel); }
                        }
                        _subproyectosAEliminar.Clear();
                    }

                    if (_guardarSubproyectoUseCase != null)
                    {
                        // 1. Insertar subproyectos nuevos
                        foreach (var sub in Subproyectos.Where(s => s.EsNuevo))
                        {
                            try
                            {
                                int subId = await _guardarSubproyectoUseCase.EjecutarAsync(
                                    idProyectoDestino,
                                    sub.Nombre,
                                    sub.MpioCdpmp,
                                    sub.Subcarpeta,
                                    sub.Lon,
                                    sub.Lat,
                                    sub.Descripcion);
                                sub.IdSubproyecto = subId;
                                sub.EsModificado = false;
                            }
                            catch (Exception exSub)
                            {
                                AppLogger.Error($"Error guardando subproyecto nuevo '{sub.Nombre}'", exSub);
                            }
                        }

                        // 2. Actualizar subproyectos existentes modificados
                        foreach (var sub in Subproyectos.Where(s => !s.EsNuevo && s.EsModificado))
                        {
                            try
                            {
                                await _guardarSubproyectoUseCase.ActualizarAsync(
                                    sub.IdSubproyecto,
                                    sub.Nombre,
                                    sub.MpioCdpmp,
                                    sub.Subcarpeta,
                                    sub.Lon,
                                    sub.Lat,
                                    sub.Descripcion);
                                sub.EsModificado = false;
                            }
                            catch (Exception exSub)
                            {
                                AppLogger.Error($"Error actualizando subproyecto existente '{sub.Nombre}'", exSub);
                            }
                        }
                    }
                }

                _onGuardadoExitoso?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error guardando proyecto: {ex.Message}", "Error al Guardar");
            }
        }

        private async Task EliminarProyectoAsync()
        {
            if (!EsModoEdicion || _eliminarProyectoUseCase == null || !IdProyecto.HasValue)
            {
                _notifications?.ShowWarning("La operación de eliminación no está disponible.", "Operación no disponible");
                return;
            }

            if (!PuedeEliminar)
            {
                _notifications?.ShowError(TooltipEliminar, "Acceso Restringido");
                return;
            }

            var confirmResult = MessageBox.Show(
                $"¿Está seguro de que desea eliminar el proyecto '{Titulo}'?\n\nEsta acción eliminará el registro y sus metadatos del sistema (los archivos físicos en disco no serán borrados).",
                "Confirmar Eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            try
            {
                string usuarioActual = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
                string equipoActual = Environment.MachineName;

                await _eliminarProyectoUseCase.EjecutarAsync(IdProyecto.Value, usuarioActual, equipoActual);
                _notifications?.ShowSuccess($"El proyecto '{Titulo}' ha sido eliminado exitosamente.", "Proyecto Eliminado");
                _onGuardadoExitoso?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al eliminar el proyecto: {ex.Message}", "Error de Eliminación");
            }
        }

        public record DepartamentoItem(string Codigo, string Nombre);
        public record MunicipioItem(string Codigo, string Nombre);
        public record MunicipioSeleccionadoItem(string Codigo, string Nombre, string DepartamentoCodigo, string DepartamentoNombre)
        {
            public string EtiquetaCompleta => string.IsNullOrWhiteSpace(DepartamentoNombre)
                ? Nombre
                : $"{Nombre} ({DepartamentoNombre})";
        }
    }

    /// <summary>
    /// Modelo de datos para subproyectos / sitios de obra editables en el formulario.
    /// </summary>
    public class SubproyectoItemFormulario : ObservableObject
    {
        private int _idSubproyecto;
        private int _idProyecto;
        private string _nombre = string.Empty;
        private string? _mpioCdpmp;
        private string? _municipioNombre;
        private string _subcarpeta = string.Empty;
        private double _lon;
        private double _lat;
        private string? _descripcion;
        private bool _esModificado;

        public int IdSubproyecto
        {
            get => _idSubproyecto;
            set
            {
                if (SetProperty(ref _idSubproyecto, value))
                {
                    OnPropertyChanged(nameof(EsNuevo));
                }
            }
        }

        public int IdProyecto
        {
            get => _idProyecto;
            set => SetProperty(ref _idProyecto, value);
        }

        public string Nombre
        {
            get => _nombre;
            set => SetProperty(ref _nombre, value);
        }

        public string? MpioCdpmp
        {
            get => _mpioCdpmp;
            set
            {
                if (SetProperty(ref _mpioCdpmp, value))
                {
                    OnPropertyChanged(nameof(MunicipioTexto));
                }
            }
        }

        public string? MunicipioNombre
        {
            get => _municipioNombre;
            set
            {
                if (SetProperty(ref _municipioNombre, value))
                {
                    OnPropertyChanged(nameof(MunicipioTexto));
                }
            }
        }

        public string Subcarpeta
        {
            get => _subcarpeta;
            set => SetProperty(ref _subcarpeta, value);
        }

        public double Lon
        {
            get => _lon;
            set
            {
                if (SetProperty(ref _lon, value))
                {
                    OnPropertyChanged(nameof(CoordenadasTexto));
                    OnPropertyChanged(nameof(TieneCoordenadas));
                }
            }
        }

        public double Lat
        {
            get => _lat;
            set
            {
                if (SetProperty(ref _lat, value))
                {
                    OnPropertyChanged(nameof(CoordenadasTexto));
                    OnPropertyChanged(nameof(TieneCoordenadas));
                }
            }
        }

        public string? Descripcion
        {
            get => _descripcion;
            set
            {
                if (SetProperty(ref _descripcion, value))
                {
                    OnPropertyChanged(nameof(HasDescripcion));
                }
            }
        }

        public bool EsModificado
        {
            get => _esModificado;
            set => SetProperty(ref _esModificado, value);
        }

        public bool EsNuevo => IdSubproyecto <= 0;
        public bool EsModificadoExistente => !EsNuevo && EsModificado;
        public bool HasDescripcion => !string.IsNullOrWhiteSpace(Descripcion);
        public bool TieneCoordenadas => Lon != 0 || Lat != 0;

        public string CoordenadasTexto => (Lon != 0 || Lat != 0)
            ? $"{Lat.ToString("F4", CultureInfo.InvariantCulture)}°N, {Lon.ToString("F4", CultureInfo.InvariantCulture)}°W"
            : "Sin coordenadas";

        public string MunicipioTexto => !string.IsNullOrWhiteSpace(MunicipioNombre)
            ? MunicipioNombre
            : (!string.IsNullOrWhiteSpace(MpioCdpmp) ? MpioCdpmp : "Sin municipio");

        public void NotificarTodos()
        {
            OnPropertyChanged(nameof(Nombre));
            OnPropertyChanged(nameof(MpioCdpmp));
            OnPropertyChanged(nameof(MunicipioNombre));
            OnPropertyChanged(nameof(MunicipioTexto));
            OnPropertyChanged(nameof(Subcarpeta));
            OnPropertyChanged(nameof(Lon));
            OnPropertyChanged(nameof(Lat));
            OnPropertyChanged(nameof(TieneCoordenadas));
            OnPropertyChanged(nameof(CoordenadasTexto));
            OnPropertyChanged(nameof(Descripcion));
            OnPropertyChanged(nameof(HasDescripcion));
            OnPropertyChanged(nameof(EsModificado));
            OnPropertyChanged(nameof(EsModificadoExistente));
            OnPropertyChanged(nameof(EsNuevo));
        }
    }
}
