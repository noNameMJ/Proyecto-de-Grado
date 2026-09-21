using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FichaProyectoViewModel : ObservableObject
    {
        private readonly INotificationService? _notifications;
        private readonly EliminarProyectoUseCase? _eliminarProyectoUseCase;
        private readonly Action? _onProyectoEliminado;
        private readonly ProyectoArchivosService _archivosService;
        private readonly IProyectoRepository? _proyectoRepository;
        private readonly SemaphoreSlim _formatosLock = new(1, 1);

        public ProyectoDetalleDto Proyecto { get; }
        public EvaluacionPermisos PermisosCarpeta { get; }

        public int Id => Proyecto.Id;
        public string CodigoId => $"#PROY-{Proyecto.Id:D4}";
        public string Titulo => Proyecto.Titulo;
        public string? Descripcion => Proyecto.Descripcion;
        public string FechaTexto => Proyecto.Fecha?.ToString("dd/MM/yyyy") ?? "Sin fecha";
        public string? PalabraClave => Proyecto.PalabraClave;
        public string? RutaArchivos => Proyecto.RutaArchivos;
        public string Coordenadas => $"{Proyecto.Lat:F6}°N, {Proyecto.Lon:F6}°W";
        public string CoordenadasFormato => $"{Proyecto.Lat:F6}, {Proyecto.Lon:F6}";
        public string LatitudTexto => $"{Math.Abs(Proyecto.Lat):F6}° {(Proyecto.Lat >= 0 ? "N" : "S")}";
        public string LongitudTexto => $"{Math.Abs(Proyecto.Lon):F6}° {(Proyecto.Lon >= 0 ? "E" : "W")}";
        public string? MunicipioNombre => Proyecto.MunicipioNombre;
        public string? MunicipioCodigo => Proyecto.MunicipioCodigo;

        public bool HasDescripcion => !string.IsNullOrWhiteSpace(Proyecto.Descripcion);
        public bool HasMunicipio => !string.IsNullOrWhiteSpace(Proyecto.MunicipioNombre);
        public bool HasRutaArchivos => !string.IsNullOrWhiteSpace(Proyecto.RutaArchivos);
        public bool RutaExiste => PermisosCarpeta.PuedeLeer;
        public bool PuedeEditar => PermisosCarpeta.PuedeEscribir;
        public bool PuedeEliminar => PermisosCarpeta.PuedeEscribir;
        public bool PuedeLeerArchivos => PermisosCarpeta.PuedeLeer;
        public bool EsAccesoRestringido => PermisosCarpeta.Estado == EstadoPermisoCarpeta.AccesoRestringido;
        public bool EsSoloLectura => PermisosCarpeta.Estado == EstadoPermisoCarpeta.SoloLectura;

        public string EstadoRutaTexto => PermisosCarpeta.BadgeTexto;
        public string BadgeBackgroundHex => PermisosCarpeta.BadgeBackgroundHex;
        public string BadgeBorderHex => PermisosCarpeta.BadgeBorderHex;
        public string BadgeForegroundHex => PermisosCarpeta.BadgeForegroundHex;

        public string TooltipEdicion => PuedeEditar
            ? "Editar detalles del proyecto"
            : "Acceso restringido: Se requieren permisos de escritura en la carpeta del servidor para editar este proyecto (geomaticaad@uis.edu.co).";

        public string TooltipEliminacion => PuedeEliminar
            ? "Eliminar este proyecto del sistema"
            : "Acceso restringido: Se requieren permisos de escritura en la carpeta del servidor para eliminar este proyecto (geomaticaad@uis.edu.co).";

        public string TooltipAbrirCarpeta => PuedeLeerArchivos
            ? "Abrir carpeta en el Explorador de Windows"
            : "Acceso denegado: Su usuario no tiene permisos en el servidor (geomaticaad@uis.edu.co) para abrir esta carpeta.";

        public int? AnioFin => Proyecto.AnioFin;
        public string? Entidades => Proyecto.Entidades;
        public string? Representante => Proyecto.Representante;

        public bool HasAnioFin => Proyecto.AnioFin.HasValue;
        public bool HasEntidades => !string.IsNullOrWhiteSpace(Proyecto.Entidades);
        public bool HasRepresentante => !string.IsNullOrWhiteSpace(Proyecto.Representante);
        public bool HasActores => HasEntidades || HasRepresentante;

        public string PeriodoTexto
        {
            get
            {
                if (Proyecto.Fecha.HasValue && Proyecto.AnioFin.HasValue)
                {
                    return Proyecto.Fecha.Value.Year == Proyecto.AnioFin.Value
                        ? $"{Proyecto.AnioFin.Value}"
                        : $"{Proyecto.Fecha.Value.Year} — {Proyecto.AnioFin.Value}";
                }
                if (Proyecto.AnioFin.HasValue) return $"Finalizado en {Proyecto.AnioFin.Value}";
                if (Proyecto.Fecha.HasValue) return $"{Proyecto.Fecha.Value:dd/MM/yyyy}";
                return "Sin fecha";
            }
        }

        [ObservableProperty]
        private ObservableCollection<CategoriaFormatoItem> _formatosDetectados = new();

        [ObservableProperty]
        private ObservableCollection<FormatoExtensionItem> _extensionesDetectadas = new();

        [ObservableProperty]
        private bool _hasFormatosDetectados;

        [ObservableProperty]
        private bool _hasExtensionesDetectadas;

        [ObservableProperty]
        private string? _resumenInsumosTexto;

        [ObservableProperty]
        private bool _cargandoFormatos;

        [ObservableProperty]
        private int _totalArchivosDetectados;

        public event EventHandler<string>? ExtensionSeleccionadaParaFiltrado;

        [RelayCommand]
        private void FiltrarPorExtension(string? extension)
        {
            if (!string.IsNullOrWhiteSpace(extension))
            {
                ExtensionSeleccionadaParaFiltrado?.Invoke(this, extension);
            }
        }

        public IReadOnlyList<string> PalabrasClaveLista { get; }
        public bool HasPalabrasClave => PalabrasClaveLista.Count > 0;

        [ObservableProperty]
        private ObservableCollection<AuditoriaProyectoDto> _historialAuditoria = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoHistorial))]
        private bool _hasHistorial;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCreadorInfo))]
        private string? _creadorInfo;

        public bool HasCreadorInfo => !string.IsNullOrWhiteSpace(CreadorInfo);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUltimaModificacionInfo))]
        private string? _ultimaModificacionInfo;

        public bool HasUltimaModificacionInfo => !string.IsNullOrWhiteSpace(UltimaModificacionInfo);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoBotonHistorial))]
        private bool _mostrarHistorial;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoHistorial))]
        private bool _cargandoHistorial;

        public bool HasNoHistorial => !CargandoHistorial && !HasHistorial;

        public string TextoBotonHistorial => MostrarHistorial
            ? "▲ Ocultar historial"
            : (HistorialAuditoria.Count > 0 ? $"▼ Ver historial ({HistorialAuditoria.Count})" : "▼ Ver historial");

        [RelayCommand]
        private void ToggleHistorial()
        {
            MostrarHistorial = !MostrarHistorial;
        }

        public IRelayCommand VolverCommand { get; }
        public IRelayCommand EditarCommand { get; }
        public IAsyncRelayCommand EliminarCommand { get; }
        public IRelayCommand AbrirCarpetaCommand { get; }
        public IRelayCommand CopiarCoordenadasCommand { get; }
        public IRelayCommand CopiarRutaCommand { get; }

        public event EventHandler<ProyectoDetalleDto>? EditarSolicitado;

        public FichaProyectoViewModel(
            ProyectoDetalleDto proyecto,
            Action volverAction,
            EliminarProyectoUseCase? eliminarProyectoUseCase = null,
            Action? onProyectoEliminado = null,
            ProyectoArchivosService? archivosService = null,
            INotificationService? notifications = null,
            IProyectoRepository? proyectoRepository = null)
        {
            Proyecto = proyecto;
            _eliminarProyectoUseCase = eliminarProyectoUseCase;
            _onProyectoEliminado = onProyectoEliminado;
            _archivosService = archivosService ?? new ProyectoArchivosService();
            _notifications = notifications;
            _proyectoRepository = proyectoRepository;

            PermisosCarpeta = _archivosService.EvaluarPermisosCarpeta(proyecto.RutaArchivos);

            VolverCommand = new RelayCommand(volverAction);
            EditarCommand = new RelayCommand(() => EditarSolicitado?.Invoke(this, Proyecto), () => PuedeEditar);
            EliminarCommand = new AsyncRelayCommand(EliminarProyectoAsync, () => PuedeEliminar);
            AbrirCarpetaCommand = new RelayCommand(AbrirCarpeta, () => PuedeLeerArchivos && !string.IsNullOrWhiteSpace(RutaArchivos));
            CopiarCoordenadasCommand = new RelayCommand(CopiarCoordenadas);
            CopiarRutaCommand = new RelayCommand(CopiarRuta, () => !string.IsNullOrWhiteSpace(RutaArchivos));

            if (!string.IsNullOrWhiteSpace(proyecto.PalabraClave))
            {
                PalabrasClaveLista = proyecto.PalabraClave
                    .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                PalabrasClaveLista = Array.Empty<string>();
            }

            if (_proyectoRepository != null)
            {
                _ = CargarHistorialAuditoriaAsync();
            }

            if (PuedeLeerArchivos && !string.IsNullOrWhiteSpace(RutaArchivos))
            {
                _ = CargarFormatosArchivosAsync();
            }
        }

        private void CopiarCoordenadas()
        {
            try
            {
                Clipboard.SetText(CoordenadasFormato);
                _notifications?.ShowSuccess($"Coordenadas copiadas: {CoordenadasFormato}", "Copiado");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error copiando coordenadas: {ex}");
            }
        }

        private void CopiarRuta()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos)) return;
            try
            {
                Clipboard.SetText(RutaArchivos);
                _notifications?.ShowSuccess("Ruta de carpeta copiada al portapapeles.", "Copiado");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error copiando ruta: {ex}");
            }
        }

        private void AbrirCarpeta()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos)) return;
            try
            {
                if (!PuedeLeerArchivos || !Directory.Exists(RutaArchivos))
                {
                    _notifications?.ShowWarning(TooltipAbrirCarpeta, "Acceso Denegado");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = RutaArchivos,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error abriendo carpeta: {ex}");
                _notifications?.ShowError($"No se pudo abrir la carpeta: {ex.Message}", "Error");
            }
        }

        private async Task EliminarProyectoAsync()
        {
            if (!PuedeEliminar)
            {
                _notifications?.ShowWarning(TooltipEliminacion, "Acceso Denegado");
                return;
            }

            if (_eliminarProyectoUseCase == null)
            {
                _notifications?.ShowWarning("El servicio de eliminación no está disponible.", "Operación no disponible");
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
                var usuarioActual = System.Security.Principal.WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;
                var equipoActual = Environment.MachineName;
                await _eliminarProyectoUseCase.EjecutarAsync(Id, usuarioActual, equipoActual);
                _notifications?.ShowSuccess($"El proyecto '{Titulo}' ha sido eliminado exitosamente.", "Proyecto Eliminado");
                _onProyectoEliminado?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error al eliminar proyecto {Id}: {ex}");
                _notifications?.ShowError($"Error al eliminar el proyecto: {ex.Message}", "Error de Eliminación");
            }
        }

        public async Task CargarHistorialAuditoriaAsync(CancellationToken ct = default)
        {
            if (_proyectoRepository == null) return;

            try
            {
                CargandoHistorial = true;
                var list = await _proyectoRepository.ObtenerHistorialProyectoAsync(Id, ct);

                HistorialAuditoria.Clear();
                foreach (var item in list)
                {
                    HistorialAuditoria.Add(item);
                }

                HasHistorial = HistorialAuditoria.Count > 0;
                OnPropertyChanged(nameof(TextoBotonHistorial));

                // Buscar evento de creación (el más antiguo o con acción CREACION)
                var creacion = list.LastOrDefault(a => a.Accion.Equals("CREACION", StringComparison.OrdinalIgnoreCase)) ?? list.LastOrDefault();
                if (creacion != null)
                {
                    CreadorInfo = $"Creado por {creacion.Usuario} ({creacion.FechaHora:dd/MM/yyyy})";
                }
                else
                {
                    CreadorInfo = null;
                }

                // Buscar evento de última modificación (el más reciente con acción MODIFICACION)
                var ultimaMod = list.FirstOrDefault(a => a.Accion.Equals("MODIFICACION", StringComparison.OrdinalIgnoreCase));
                if (ultimaMod != null)
                {
                    UltimaModificacionInfo = $"Modificado por {ultimaMod.Usuario} ({ultimaMod.FechaHora:dd/MM/yyyy HH:mm})";
                }
                else
                {
                    UltimaModificacionInfo = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error al cargar historial de auditoría: {ex.Message}");
            }
            finally
            {
                CargandoHistorial = false;
            }
        }

        public async Task CargarFormatosArchivosAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos) || !PuedeLeerArchivos)
                return;

            await _formatosLock.WaitAsync(ct);
            try
            {
                CargandoFormatos = true;
                var resumen = await _archivosService.EscanearFormatosArchivosAsync(RutaArchivos, ct);

                FormatosDetectados.Clear();
                foreach (var cat in resumen.Categorias)
                {
                    FormatosDetectados.Add(cat);
                }

                ExtensionesDetectadas.Clear();
                foreach (var ext in resumen.ExtensionesLista)
                {
                    ExtensionesDetectadas.Add(ext);
                }

                TotalArchivosDetectados = resumen.TotalArchivos;
                HasFormatosDetectados = FormatosDetectados.Count > 0;
                HasExtensionesDetectadas = ExtensionesDetectadas.Count > 0;

                if (HasExtensionesDetectadas)
                {
                    var primarios = ExtensionesDetectadas.Where(e => !e.EsAuxiliar).Select(e => $".{e.ExtensionMayus} ({e.CantidadArchivos})");
                    ResumenInsumosTexto = string.Join(", ", primarios);
                }
                else
                {
                    ResumenInsumosTexto = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error cargando formatos de archivo: {ex.Message}");
            }
            finally
            {
                CargandoFormatos = false;
                _formatosLock.Release();
            }
        }
    }
}
