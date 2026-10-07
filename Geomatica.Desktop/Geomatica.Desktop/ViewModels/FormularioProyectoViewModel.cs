using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Interfaces.Repositories;
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
        private bool _isUpdatingProgrammatically;

        public bool EsModoEdicion { get; }
        public int? IdProyecto => _proyectoOriginal?.Id;

        public string TituloVista => EsModoEdicion ? "Editar Proyecto" : "Crear Nuevo Proyecto";
        public string TextoBotonGuardar => EsModoEdicion ? "Guardar Cambios" : "Guardar Proyecto";
        public string TooltipGuardar => EsModoEdicion ? "Guardar las modificaciones realizadas al proyecto" : "Registrar y guardar el nuevo proyecto en el sistema";

        public EvaluacionPermisos? PermisosCarpeta { get; }
        public bool PuedeEscribir => PermisosCarpeta?.PuedeEscribir ?? true;
        public bool EsSoloLecturaOAccesoRestringido => EsModoEdicion && PermisosCarpeta != null && !PermisosCarpeta.PuedeEscribir;
        public string MensajeRestriccion => PermisosCarpeta?.Mensaje ?? string.Empty;

        // ═══════════════════════════════════════════════════════════════
        // PROPIEDADES OBSERVABLES DEL FORMULARIO
        // ═══════════════════════════════════════════════════════════════

        // Sección 1: Ruta y Metadatos Técnicos ISO 19115
        [ObservableProperty] private string? ruta;
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
        public event Action<double?, double?>? CoordenadasPinChanged;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();

        // Comandos del formulario
        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }
        public IAsyncRelayCommand EliminarCommand { get; }
        public IRelayCommand SeleccionarCarpetaCommand { get; }
        public IRelayCommand LimpiarFechaFinCommand { get; }
        public IAsyncRelayCommand ExtraerMetadatosCommand { get; }

        /// <summary>
        /// Constructor para MODO CREACIÓN de nuevo proyecto.
        /// </summary>
        public FormularioProyectoViewModel(
            CrearProyectoUseCase crearProyectoUseCase,
            IMunicipioRepository municipioRepository,
            ProyectoArchivosService proyectoArchivosService,
            Action navigateBack,
            Action? onProyectoCreado = null,
            INotificationService? notifications = null)
        {
            EsModoEdicion = false;
            _crearProyectoUseCase = crearProyectoUseCase;
            _municipioRepository = municipioRepository;
            _archivosService = proyectoArchivosService;
            _navigateBack = navigateBack;
            _onGuardadoExitoso = onProyectoCreado;
            _notifications = notifications;

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);
            EliminarCommand = new AsyncRelayCommand(() => Task.CompletedTask);
            SeleccionarCarpetaCommand = new RelayCommand(SeleccionarCarpeta);
            LimpiarFechaFinCommand = new RelayCommand(() => FechaFin = null);
            ExtraerMetadatosCommand = new AsyncRelayCommand(ExtraerMetadatosAsync);

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
            ProyectoArchivosService? archivosService = null)
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

            _ = CargarDatosInicialesAsync(proyecto.MunicipioCodigo);
        }

        // ═══════════════════════════════════════════════════════════════
        // CARGA Y CASCADA DE MUNICIPIOS (DIVIPOLA)
        // ═══════════════════════════════════════════════════════════════

        private async Task CargarDatosInicialesAsync(string? municipioCodigo)
        {
            _isUpdatingProgrammatically = true;
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }

                if (!string.IsNullOrWhiteSpace(municipioCodigo))
                {
                    var dptoCodigo = municipioCodigo.Length >= 2 ? municipioCodigo.Substring(0, 2) : "";
                    var dep = Departamentos.FirstOrDefault(d => d.Codigo == dptoCodigo);
                    if (dep != null)
                    {
                        SelectedDepartamento = dep;
                        var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(dep.Codigo);
                        Municipios.Clear();
                        foreach (var m in muns)
                        {
                            var item = new MunicipioItem(m.Codigo, m.Nombre);
                            Municipios.Add(item);
                            if (m.Codigo == municipioCodigo)
                            {
                                SelectedMunicipio = item;
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

            if (SelectedMunicipio != null)
            {
                try
                {
                    var results = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { SelectedMunicipio.Codigo });
                    MunicipioGeoJsonChanged?.Invoke(results.FirstOrDefault()?.GeoJson);
                }
                catch { }
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            if (_isUpdatingProgrammatically) return;

            Municipios.Clear();
            SelectedMunicipio = null;
            MunicipioGeoJsonChanged?.Invoke(null);
            LatStr = null;
            LonStr = null;
            CoordenadasPinChanged?.Invoke(null, null);

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
            if (_isUpdatingProgrammatically) return;

            if (value == null || string.IsNullOrEmpty(value.Codigo))
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                return;
            }

            try
            {
                var results = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { value.Codigo });
                MunicipioGeoJsonChanged?.Invoke(results.FirstOrDefault()?.GeoJson);

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
            catch
            {
                MunicipioGeoJsonChanged?.Invoke(null);
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

                if (SelectedMunicipio == null)
                {
                    await AsignarUbicacionDetectadaAsync(ubicacion);
                    SetCoordenadas(lat, lon);
                    _notifications?.ShowInfo($"Municipio detectado: {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}).", "Ubicación Asignada");
                    return true;
                }

                if (SelectedMunicipio.Codigo == ubicacion.MunicipioCodigo)
                {
                    SetCoordenadas(lat, lon);
                    return true;
                }

                var msg = $"El punto seleccionado pertenece a {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}), pero actualmente tiene seleccionado {SelectedMunicipio.Nombre}.\n\n¿Desea cambiar el municipio del proyecto a {ubicacion.MunicipioNombre}?";
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
                if (SelectedMunicipio != null)
                {
                    var results = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { SelectedMunicipio.Codigo });
                    MunicipioGeoJsonChanged?.Invoke(results.FirstOrDefault()?.GeoJson);
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
                var result = await extractor.ExtraerDesdeRutaAsync(Ruta);

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
            TituloInvalido = string.IsNullOrWhiteSpace(Titulo);
            if (TituloInvalido)
            {
                _notifications?.ShowWarning("El título es obligatorio.", "Validación");
                return;
            }

            if (SelectedMunicipio == null)
            {
                _notifications?.ShowWarning("Debe seleccionar un municipio.", "Validación");
                return;
            }

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
                var estaEnMunicipio = await _municipioRepository.PuntoEstaEnMunicipioAsync(SelectedMunicipio.Codigo, lon.Value, lat.Value);
                if (!estaEnMunicipio)
                {
                    _notifications?.ShowError($"Las coordenadas indicadas ({lat.Value:F6}, {lon.Value:F6}) no pertenecen al municipio seleccionado ({SelectedMunicipio.Nombre}). Corrija la ubicación antes de guardar.", "Error de Validación Espacial");
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
                        SelectedMunicipio.Codigo,
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

                    await _crearProyectoUseCase.EjecutarAsync(
                        Titulo,
                        Descripcion,
                        FechaInicio,
                        PalabraClave,
                        Ruta,
                        geom,
                        SelectedMunicipio.Codigo,
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

            if (_archivosService != null && !string.IsNullOrWhiteSpace(_proyectoOriginal?.RutaArchivos))
            {
                var eval = _archivosService.EvaluarPermisosCarpeta(_proyectoOriginal.RutaArchivos);
                if (!eval.PuedeEscribir)
                {
                    _notifications?.ShowError("No tiene permisos de escritura en la carpeta del servidor para eliminar este proyecto (geomaticaad@uis.edu.co).", "Acceso Restringido");
                    return;
                }
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
    }
}
