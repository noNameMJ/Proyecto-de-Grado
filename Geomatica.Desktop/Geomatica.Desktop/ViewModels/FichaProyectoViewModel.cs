using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Domain.Catalogos;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public sealed record MunicipioFichaItem(string Codigo, string Nombre, string Departamento, string DisplayCompleto)
    {
        public bool HasDepartamento => !string.IsNullOrWhiteSpace(Departamento);
        public string DepartamentoEtiqueta => HasDepartamento ? $" · {Departamento}" : string.Empty;
        public bool HasCodigo => !string.IsNullOrWhiteSpace(Codigo);
    }

    public sealed record DepartamentoFiltroItem(string Nombre, int Cantidad, bool EsTodos = false)
    {
        public string DisplayTexto => $"{Nombre} ({Cantidad})";
        public override string ToString() => DisplayTexto;
    }

    public partial class CategoriaTabItem : ObservableObject
    {
        public string Nombre { get; init; } = "";
        public string Icono { get; init; } = "";
        public int CantidadArchivos { get; init; }
        public bool EsTodos => string.Equals(Nombre, "Todos", StringComparison.OrdinalIgnoreCase);

        [ObservableProperty]
        private bool _isSelected;
    }

    public partial class CategoriaGrupoItem : ObservableObject
    {
        public string Categoria { get; init; } = "";
        public string Icono { get; init; } = "";
        public string BadgeBgHex { get; init; } = "#F1F5F9";
        public string BadgeBorderHex { get; init; } = "#CBD5E1";
        public string BadgeFgHex { get; init; } = "#475569";
        public int CantidadArchivos { get; init; }
        public long TamanoTotalBytes { get; init; }
        public string TamanoTexto => FormatearTamano(TamanoTotalBytes);

        public IReadOnlyList<FormatoExtensionItem> ExtensionesPrincipales { get; init; } = Array.Empty<FormatoExtensionItem>();
        public IReadOnlyList<FormatoExtensionItem> ExtensionesAuxiliares { get; init; } = Array.Empty<FormatoExtensionItem>();

        public bool TieneExtensionesPrincipales => ExtensionesPrincipales.Count > 0;
        public bool TieneExtensionesAuxiliares => ExtensionesAuxiliares.Count > 0;
        public int TotalAuxiliaresConteo => ExtensionesAuxiliares.Sum(e => e.CantidadArchivos);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoToggleAuxiliares))]
        private bool _mostrarAuxiliares;

        public string TextoToggleAuxiliares => MostrarAuxiliares
            ? $"▲ Ocultar {ExtensionesAuxiliares.Count} formatos auxiliares / sidecars"
            : $"📎 Ver {ExtensionesAuxiliares.Count} archivos auxiliares y sidecars ({TotalAuxiliaresConteo} archivos) ▼";

        public string ResumenAuxiliaresTexto =>
            $"{ExtensionesAuxiliares.Count} formatos auxiliares ({TotalAuxiliaresConteo} archivos): {string.Join(", ", ExtensionesAuxiliares.Select(e => $".{e.ExtensionMayus}"))}";

        [RelayCommand]
        private void ToggleAuxiliares()
        {
            MostrarAuxiliares = !MostrarAuxiliares;
        }

        public static string FormatearTamano(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public partial class FichaProyectoViewModel : ObservableObject
    {
        private readonly INotificationService? _notifications;
        private readonly EliminarProyectoUseCase? _eliminarProyectoUseCase;
        private readonly Action? _onProyectoEliminado;
        private readonly ProyectoArchivosService _archivosService;
        private readonly IProyectoRepository? _proyectoRepository;
        private readonly ObtenerHistorialProyectoUseCase? _historialUseCase;
        private readonly ISeguridadService _seguridadService;
        private readonly ObtenerSubproyectosUseCase? _obtenerSubproyectosUseCase;
        private readonly SemaphoreSlim _formatosLock = new(1, 1);

        public ProyectoDetalleDto Proyecto { get; }
        public EvaluacionPermisos PermisosCarpeta { get; private set; }

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

        public IReadOnlyList<MunicipioFichaItem> MunicipiosTodos { get; private set; } = Array.Empty<MunicipioFichaItem>();
        public IReadOnlyList<DepartamentoFiltroItem> DepartamentosFiltro { get; private set; } = Array.Empty<DepartamentoFiltroItem>();

        [ObservableProperty]
        private ObservableCollection<MunicipioFichaItem> _municipiosFiltrados = new();

        [ObservableProperty]
        private string _filtroMunicipioTexto = string.Empty;

        [ObservableProperty]
        private DepartamentoFiltroItem? _filtroDepartamentoSeleccionado;

        public int TotalMunicipios => MunicipiosTodos.Count;
        public int TotalDepartamentos { get; private set; }
        public bool EsPlurimunicipal => TotalMunicipios > 3;
        public bool HasPocosMunicipios => TotalMunicipios > 0 && TotalMunicipios <= 3;
        public bool HasMultiplesDepartamentos => TotalDepartamentos > 1;
        public string MunicipioChipTexto { get; private set; } = string.Empty;
        public string MunicipioChipTooltip { get; private set; } = string.Empty;

        public bool HasFiltroMunicipioTexto => !string.IsNullOrWhiteSpace(FiltroMunicipioTexto);
        public bool HasNoFiltroMunicipioTexto => string.IsNullOrWhiteSpace(FiltroMunicipioTexto);
        public string TextoConteoMunicipiosFiltrados => $"Mostrando {MunicipiosFiltrados.Count} de {TotalMunicipios} municipios";
        public bool TieneFiltroActivo => !string.IsNullOrWhiteSpace(FiltroMunicipioTexto) || (FiltroDepartamentoSeleccionado != null && !FiltroDepartamentoSeleccionado.EsTodos);
        public bool HasNoMunicipiosFiltrados => MunicipiosFiltrados.Count == 0 && TotalMunicipios > 0;

        public bool HasDescripcion => !string.IsNullOrWhiteSpace(Proyecto.Descripcion);
        public bool HasMunicipio => TotalMunicipios > 0 || !string.IsNullOrWhiteSpace(Proyecto.MunicipioNombre);
        public bool HasRutaArchivos => !string.IsNullOrWhiteSpace(Proyecto.RutaArchivos);
        public bool RutaExiste => PermisosCarpeta.PuedeLeer;
        public bool PuedeEditar => PermisosCarpeta.PuedeEscribir || _seguridadService.PuedeEditarProyectos();
        public bool PuedeEliminar => _seguridadService.PuedeEliminarProyectos();
        public bool PuedeLeerArchivos => PermisosCarpeta.PuedeLeer;
        public bool EsAccesoRestringido => PermisosCarpeta.Estado == EstadoPermisoCarpeta.AccesoRestringido;
        public bool EsSoloLectura => PermisosCarpeta.Estado == EstadoPermisoCarpeta.SoloLectura;

        public string EstadoRutaTexto => PermisosCarpeta.BadgeTexto;
        public string BadgeBackgroundHex => PermisosCarpeta.BadgeBackgroundHex;
        public string BadgeBorderHex => PermisosCarpeta.BadgeBorderHex;
        public string BadgeForegroundHex => PermisosCarpeta.BadgeForegroundHex;

        public string TooltipEdicion => PuedeEditar
            ? "Editar detalles del proyecto"
            : "Acceso restringido: Se requieren permisos de escritura en la carpeta del servidor o rol de Administrador para editar este proyecto (geomaticaad@uis.edu.co).";

        public string TooltipEliminacion => PuedeEliminar
            ? "Eliminar este proyecto del sistema"
            : "Acceso restringido: Solo los miembros del rol Administrador (grupo AD 'G4_GEOMATICA_ADM') pueden eliminar proyectos.";

        public string TooltipAbrirCarpeta => PuedeLeerArchivos
            ? "Abrir carpeta en el Explorador de Windows"
            : "Acceso denegado: Su usuario no tiene permisos en el servidor (geomaticaad@uis.edu.co) para abrir esta carpeta.";

        public string TooltipDescargarZip => PuedeLeerArchivos
            ? "Descargar y empaquetar todos los archivos del proyecto en un archivo comprimido .ZIP"
            : "Acceso denegado: Su usuario no tiene permisos en el servidor (geomaticaad@uis.edu.co) para acceder o descargar los archivos de este proyecto.";

        public bool PuedeDescargarZip => PuedeLeerArchivos && !string.IsNullOrWhiteSpace(RutaArchivos) && !IsDescargandoZip;

        public DateTime? FechaInicio => Proyecto.FechaInicio;
        public DateTime? FechaFin => Proyecto.FechaFin;
        public DateTime? Fecha => Proyecto.FechaInicio;
        public int? AnioFin => Proyecto.AnioFin;

        public string FechaFinTexto => Proyecto.FechaFin?.ToString("dd/MM/yyyy") ?? "Sin fecha de finalización";

        public bool HasFechaInicio => Proyecto.FechaInicio.HasValue;
        public bool HasFechaFin => Proyecto.FechaFin.HasValue;
        public bool HasAnioFin => Proyecto.AnioFin.HasValue;
        public string? Entidades => Proyecto.Entidades;
        public string? Representante => Proyecto.Representante;

        public bool HasEntidades => !string.IsNullOrWhiteSpace(Proyecto.Entidades);
        public bool HasRepresentante => !string.IsNullOrWhiteSpace(Proyecto.Representante);
        public bool HasActores => HasEntidades || HasRepresentante;

        // ISO 19115 Metadata
        public string? SistemaReferencia => Proyecto.SistemaReferencia;
        public string? FormatoDatos => Proyecto.FormatoDatos;
        public string? Linaje => Proyecto.Linaje;
        public DateTime? FechaActualizacion => Proyecto.FechaActualizacion;
        public string FechaActualizacionTexto => Proyecto.FechaActualizacion?.ToString("dd/MM/yyyy HH:mm") ?? "No registrada";
        public bool HasSistemaReferencia => !string.IsNullOrWhiteSpace(Proyecto.SistemaReferencia);
        public bool HasFormatoDatos => !string.IsNullOrWhiteSpace(Proyecto.FormatoDatos);
        public bool HasLinaje => !string.IsNullOrWhiteSpace(Proyecto.Linaje);
        public bool HasIsoMetadata => HasSistemaReferencia || HasFormatoDatos || HasLinaje;
        public bool HasNoIsoMetadata => !HasIsoMetadata;

        public string PeriodoTexto
        {
            get
            {
                if (Proyecto.FechaInicio.HasValue && Proyecto.FechaFin.HasValue)
                {
                    if (Proyecto.FechaInicio.Value.Date == Proyecto.FechaFin.Value.Date)
                    {
                        return Proyecto.FechaInicio.Value.ToString("dd/MM/yyyy");
                    }
                    return $"{Proyecto.FechaInicio.Value:dd/MM/yyyy} — {Proyecto.FechaFin.Value:dd/MM/yyyy}";
                }
                if (Proyecto.FechaFin.HasValue) return $"Finalizado: {Proyecto.FechaFin.Value:dd/MM/yyyy}";
                if (Proyecto.FechaInicio.HasValue) return $"{Proyecto.FechaInicio.Value:dd/MM/yyyy}";
                return "Sin fecha";
            }
        }

        [ObservableProperty]
        private ObservableCollection<CategoriaFormatoItem> _formatosDetectados = new();

        [ObservableProperty]
        private ObservableCollection<FormatoExtensionItem> _extensionesDetectadas = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowNoGruposFormatos))]
        [NotifyPropertyChangedFor(nameof(HasNoFormatosDetectados))]
        private bool _hasFormatosDetectados;

        [ObservableProperty]
        private bool _hasExtensionesDetectadas;

        [ObservableProperty]
        private string? _resumenInsumosTexto;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoFormatosDetectados))]
        private bool _cargandoFormatos;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PuedeDescargarZip))]
        private bool _isDescargandoZip;

        [ObservableProperty]
        private double _progresoDescargaZip;

        [ObservableProperty]
        private string? _estadoDescargaZipTexto;

        partial void OnIsDescargandoZipChanged(bool value)
        {
            DescargarZipCommand?.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Delegado para inyectar o personalizar el diálogo de selección de archivo de guardado (SaveFileDialog).
        /// Facilita pruebas unitarias sin dependencias de UI modal de Windows.
        /// </summary>
        public Func<string, string, string?>? SaveFileDialogCustomHandler { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TotalArchivosDetectadosTexto))]
        [NotifyPropertyChangedFor(nameof(HasNoFormatosDetectados))]
        private int _totalArchivosDetectados;

        public string TotalArchivosDetectadosTexto => TotalArchivosDetectados > 0
            ? $"({TotalArchivosDetectados:N0} archivos)"
            : "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TamanoTotalDetectadoTexto))]
        private long _tamanoTotalDetectadoBytes;

        public string TamanoTotalDetectadoTexto => CategoriaGrupoItem.FormatearTamano(TamanoTotalDetectadoBytes);
        public int TotalCategoriasDetectadas => FormatosDetectados.Count > 0 ? FormatosDetectados.Count : GruposFormatos.Count;
        public int TotalExtensionesPrincipales => ExtensionesDetectadas.Count(e => e != null && !e.EsAuxiliar);
        public int TotalExtensionesAuxiliares => ExtensionesDetectadas.Count(e => e != null && e.EsAuxiliar);

        [ObservableProperty]
        private string _filtroCategoriaSeleccionada = "Todos";

        [ObservableProperty]
        private string _busquedaFormatoTexto = "";

        public bool HasBusquedaFormatoTexto => !string.IsNullOrWhiteSpace(BusquedaFormatoTexto);
        public bool HasNoBusquedaFormatoTexto => string.IsNullOrWhiteSpace(BusquedaFormatoTexto);

        public ObservableCollection<CategoriaGrupoItem> GruposFormatos { get; } = new();
        public ObservableCollection<CategoriaGrupoItem> GruposFormatosFiltrados { get; } = new();
        public ObservableCollection<CategoriaTabItem> CategoriasTabs { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowNoGruposFormatos))]
        private bool _hasGruposFormatos;

        public bool ShowNoGruposFormatos => HasFormatosDetectados && !HasGruposFormatos;
        public bool HasNoFormatosDetectados => HasRutaArchivos && !CargandoFormatos && !HasFormatosDetectados;

        [ObservableProperty]
        private string _textoProgresoFormatos = "Analizando tipos de archivos en la carpeta...";

        public event EventHandler<string>? ExtensionSeleccionadaParaFiltrado;
        public event EventHandler<string>? CategoriaSeleccionadaParaFiltrado;
        public event EventHandler? ExtensionesActualizadas;
        private readonly object _extensionesSyncLock = new();

        public List<FormatoExtensionItem> ObtenerExtensionesDetectadasSnapshot()
        {
            lock (_extensionesSyncLock)
            {
                return ExtensionesDetectadas.Where(e => e != null).ToList();
            }
        }

        public void NotificarExtensionesActualizadas()
        {
            OnPropertyChanged(nameof(ExtensionesDetectadas));
            OnPropertyChanged(nameof(FormatosDetectados));
            OnPropertyChanged(nameof(TotalCategoriasDetectadas));
            OnPropertyChanged(nameof(TotalExtensionesPrincipales));
            OnPropertyChanged(nameof(TotalExtensionesAuxiliares));
            OnPropertyChanged(nameof(TamanoTotalDetectadoTexto));
            OnPropertyChanged(nameof(HasNoFormatosDetectados));
            OnPropertyChanged(nameof(ShowNoGruposFormatos));
            ExtensionesActualizadas?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void FiltrarPorExtension(string? extension)
        {
            if (!string.IsNullOrWhiteSpace(extension))
            {
                ExtensionSeleccionadaParaFiltrado?.Invoke(this, extension);
            }
        }

        [RelayCommand]
        public void SeleccionarTabCategoria(string? categoria)
        {
            var nombreCat = string.IsNullOrWhiteSpace(categoria) ? "Todos" : categoria;
            FiltroCategoriaSeleccionada = nombreCat;
            foreach (var tab in CategoriasTabs)
            {
                tab.IsSelected = string.Equals(tab.Nombre, nombreCat, StringComparison.OrdinalIgnoreCase);
            }
        }

        [RelayCommand]
        public void ExplorarCategoria(string? categoria)
        {
            if (!string.IsNullOrWhiteSpace(categoria))
            {
                CategoriaSeleccionadaParaFiltrado?.Invoke(this, categoria);
            }
        }

        [RelayCommand]
        public void LimpiarBusquedaFormato()
        {
            BusquedaFormatoTexto = string.Empty;
        }

        partial void OnBusquedaFormatoTextoChanged(string value)
        {
            OnPropertyChanged(nameof(HasBusquedaFormatoTexto));
            OnPropertyChanged(nameof(HasNoBusquedaFormatoTexto));
            AplicarFiltroGruposFormatos();
        }

        partial void OnFiltroCategoriaSeleccionadaChanged(string value)
        {
            AplicarFiltroGruposFormatos();
        }

        public IReadOnlyList<string> PalabrasClaveLista { get; }
        public bool HasPalabrasClave => PalabrasClaveLista.Count > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSubproyectos))]
        [NotifyPropertyChangedFor(nameof(TotalSubproyectos))]
        private ObservableCollection<SubproyectoDto> _subproyectos = new();

        public bool HasSubproyectos => Subproyectos.Count > 0;
        public int TotalSubproyectos => Subproyectos.Count;

        [ObservableProperty]
        private bool _cargandoSubproyectos;

        public event EventHandler<string>? NavegacionSubcarpetaSolicitada;
        public event EventHandler<(double lon, double lat, string nombre)>? UbicarEnMapaSubproyectoSolicitado;
        public event EventHandler<IReadOnlyList<SubproyectoDto>>? SubproyectosCargados;

        [RelayCommand]
        private void ExplorarArchivosSubproyecto(SubproyectoDto? subproyecto)
        {
            if (subproyecto != null && !string.IsNullOrWhiteSpace(subproyecto.Subcarpeta))
            {
                NavegacionSubcarpetaSolicitada?.Invoke(this, subproyecto.Subcarpeta);
            }
        }

        [RelayCommand]
        private void UbicarEnMapaSubproyecto(SubproyectoDto? subproyecto)
        {
            if (subproyecto != null)
            {
                UbicarEnMapaSubproyectoSolicitado?.Invoke(this, (subproyecto.Lon, subproyecto.Lat, subproyecto.Nombre));
            }
        }

        [ObservableProperty]
        private ObservableCollection<AuditoriaProyectoDto> _historialAuditoria = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoHistorial))]
        private bool _hasHistorial;

        [ObservableProperty]
        private ObservableCollection<AuditoriaArchivoDto> _historialArchivos = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoHistorialArchivos))]
        private bool _hasHistorialArchivos;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoHistorialArchivos))]
        private bool _cargandoHistorialArchivos;

        public bool HasNoHistorialArchivos => !CargandoHistorialArchivos && !HasHistorialArchivos;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EsAuditoriaProyectoSeleccionada))]
        [NotifyPropertyChangedFor(nameof(EsAuditoriaArchivosSeleccionada))]
        private bool _verAuditoriaArchivos;

        public bool EsAuditoriaProyectoSeleccionada => !VerAuditoriaArchivos;
        public bool EsAuditoriaArchivosSeleccionada => VerAuditoriaArchivos;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TextoVerMasArchivos))]
        private int _totalMovimientosArchivos;

        [ObservableProperty]
        private bool _tieneMasMovimientosArchivos;

        public string TextoVerMasArchivos => $"Ver todos los movimientos ({TotalMovimientosArchivos})...";

        public Action<HistorialArchivoViewModel>? AbrirModalHistorialAction { get; set; }

        [RelayCommand]
        public void SeleccionarAuditoriaProyecto()
        {
            VerAuditoriaArchivos = false;
        }

        [RelayCommand]
        public void SeleccionarAuditoriaArchivos()
        {
            VerAuditoriaArchivos = true;
            if (HistorialArchivos.Count == 0 && !CargandoHistorialArchivos)
            {
                _ = CargarHistorialArchivosAsync();
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCreadorInfo))]
        private string? _creadorInfo;

        public bool HasCreadorInfo => !string.IsNullOrWhiteSpace(CreadorInfo);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasUltimaModificacionInfo))]
        private string? _ultimaModificacionInfo;

        public bool HasUltimaModificacionInfo => !string.IsNullOrWhiteSpace(UltimaModificacionInfo);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion1))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion1Expandida;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion2))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion2Expandida;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion3))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion3Expandida;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion4))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion4Expandida;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion5))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion5Expandida;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChevronSeccion6))]
        [NotifyPropertyChangedFor(nameof(TotalSeccionesExpandidas))]
        [NotifyPropertyChangedFor(nameof(ResumenSeccionesExpandidasTexto))]
        [NotifyPropertyChangedFor(nameof(AlgunaSeccionExpandida))]
        [NotifyPropertyChangedFor(nameof(TodasSeccionesExpandidas))]
        private bool _seccion6Expandida;

        public string ChevronSeccion1 => Seccion1Expandida ? "▼" : "▶";
        public string ChevronSeccion2 => Seccion2Expandida ? "▼" : "▶";
        public string ChevronSeccion3 => Seccion3Expandida ? "▼" : "▶";
        public string ChevronSeccion4 => Seccion4Expandida ? "▼" : "▶";
        public string ChevronSeccion5 => Seccion5Expandida ? "▼" : "▶";
        public string ChevronSeccion6 => Seccion6Expandida ? "▼" : "▶";

        public int TotalSeccionesExpandidas =>
            (Seccion1Expandida ? 1 : 0) +
            (Seccion2Expandida ? 1 : 0) +
            (Seccion3Expandida ? 1 : 0) +
            (Seccion4Expandida ? 1 : 0) +
            (Seccion5Expandida ? 1 : 0) +
            (Seccion6Expandida ? 1 : 0);

        public bool AlgunaSeccionExpandida => TotalSeccionesExpandidas > 0;
        public bool TodasSeccionesExpandidas => TotalSeccionesExpandidas == 6;

        public string ResumenSeccionesExpandidasTexto => TotalSeccionesExpandidas switch
        {
            0 => "(todas contraídas)",
            6 => "(todas expandidas)",
            1 => "(1 de 6 expandida)",
            _ => $"({TotalSeccionesExpandidas} de 6 expandidas)"
        };

        [RelayCommand]
        public void ToggleSeccion1() => Seccion1Expandida = !Seccion1Expandida;

        [RelayCommand]
        public void ToggleSeccion2() => Seccion2Expandida = !Seccion2Expandida;

        [RelayCommand]
        public void ToggleSeccion3() => Seccion3Expandida = !Seccion3Expandida;

        [RelayCommand]
        public void ToggleSeccion4() => Seccion4Expandida = !Seccion4Expandida;

        [RelayCommand]
        public void ToggleSeccion5() => Seccion5Expandida = !Seccion5Expandida;

        [RelayCommand]
        public void ToggleSeccion6() => Seccion6Expandida = !Seccion6Expandida;

        [RelayCommand]
        public void ExpandirTodasSecciones()
        {
            Seccion1Expandida = true;
            Seccion2Expandida = true;
            Seccion3Expandida = true;
            Seccion4Expandida = true;
            Seccion5Expandida = true;
            Seccion6Expandida = true;
        }

        [RelayCommand]
        public void ContraerTodasSecciones()
        {
            Seccion1Expandida = false;
            Seccion2Expandida = false;
            Seccion3Expandida = false;
            Seccion4Expandida = false;
            Seccion5Expandida = false;
            Seccion6Expandida = false;
        }

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
            if (MostrarHistorial)
            {
                if (HistorialAuditoria.Count == 0 && !CargandoHistorial)
                {
                    _ = CargarHistorialAuditoriaAsync();
                }
                if (VerAuditoriaArchivos && HistorialArchivos.Count == 0 && !CargandoHistorialArchivos)
                {
                    _ = CargarHistorialArchivosAsync();
                }
            }
        }

        public IRelayCommand VolverCommand { get; }
        public IRelayCommand EditarCommand { get; }
        public IAsyncRelayCommand EliminarCommand { get; }
        public IRelayCommand AbrirCarpetaCommand { get; }
        public IAsyncRelayCommand DescargarZipCommand { get; }
        public IRelayCommand CopiarCoordenadasCommand { get; }
        public IRelayCommand CopiarRutaCommand { get; }
        public IRelayCommand CopiarMunicipiosCommand { get; }
        public IRelayCommand LimpiarFiltrosMunicipioCommand { get; }

        public event EventHandler<ProyectoDetalleDto>? EditarSolicitado;

        public FichaProyectoViewModel(
            ProyectoDetalleDto proyecto,
            Action volverAction,
            EliminarProyectoUseCase? eliminarProyectoUseCase = null,
            Action? onProyectoEliminado = null,
            ProyectoArchivosService? archivosService = null,
            INotificationService? notifications = null,
            IProyectoRepository? proyectoRepository = null,
            ObtenerHistorialProyectoUseCase? historialUseCase = null,
            ISeguridadService? seguridadService = null,
            EvaluacionPermisos? permisosPrecalculados = null,
            ObtenerSubproyectosUseCase? obtenerSubproyectosUseCase = null)
        {
            Proyecto = proyecto;
            _eliminarProyectoUseCase = eliminarProyectoUseCase;
            _onProyectoEliminado = onProyectoEliminado;
            _archivosService = archivosService ?? new ProyectoArchivosService();
            _notifications = notifications;
            _proyectoRepository = proyectoRepository;
            _historialUseCase = historialUseCase ?? (proyectoRepository != null ? new ObtenerHistorialProyectoUseCase(proyectoRepository) : null);
            _seguridadService = seguridadService ?? new WindowsSeguridadService();
            _obtenerSubproyectosUseCase = obtenerSubproyectosUseCase ?? (proyectoRepository != null ? new ObtenerSubproyectosUseCase(proyectoRepository) : null);

            PermisosCarpeta = permisosPrecalculados
                ?? (_archivosService.TryObtenerPermisosCacheados(proyecto.RutaArchivos, out var c) && c != null
                    ? c
                    : _archivosService.EvaluarPermisosCarpeta(proyecto.RutaArchivos));

            VolverCommand = new RelayCommand(volverAction);
            EditarCommand = new RelayCommand(() => EditarSolicitado?.Invoke(this, Proyecto), () => PuedeEditar);
            EliminarCommand = new AsyncRelayCommand(EliminarProyectoAsync, () => PuedeEliminar);
            AbrirCarpetaCommand = new RelayCommand(AbrirCarpeta, () => PuedeLeerArchivos && !string.IsNullOrWhiteSpace(RutaArchivos));
            DescargarZipCommand = new AsyncRelayCommand(DescargarZipAsync, () => PuedeDescargarZip);
            CopiarCoordenadasCommand = new RelayCommand(CopiarCoordenadas);
            CopiarRutaCommand = new RelayCommand(CopiarRuta, () => !string.IsNullOrWhiteSpace(RutaArchivos));
            CopiarMunicipiosCommand = new RelayCommand(CopiarMunicipios);
            LimpiarFiltrosMunicipioCommand = new RelayCommand(LimpiarFiltrosMunicipio);

            InicializarMunicipios(proyecto.MunicipioNombre, proyecto.MunicipioCodigo);

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

            if (_historialUseCase != null)
            {
                _ = CargarHistorialAuditoriaAsync();
            }

            if (_obtenerSubproyectosUseCase != null)
            {
                _ = CargarSubproyectosAsync();
            }

            if (PuedeLeerArchivos && !string.IsNullOrWhiteSpace(RutaArchivos))
            {
                _ = CargarFormatosArchivosAsync();
            }
        }

        public async Task CargarSubproyectosAsync()
        {
            if (_obtenerSubproyectosUseCase == null || Id <= 0) return;
            CargandoSubproyectos = true;
            try
            {
                var lista = await _obtenerSubproyectosUseCase.EjecutarAsync(Id);
                Subproyectos.Clear();
                foreach (var item in lista)
                {
                    Subproyectos.Add(item);
                }
                OnPropertyChanged(nameof(HasSubproyectos));
                OnPropertyChanged(nameof(TotalSubproyectos));
                SubproyectosCargados?.Invoke(this, lista);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyectoViewModel] Error al cargar subproyectos: {ex.Message}");
            }
            finally
            {
                CargandoSubproyectos = false;
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

        private async Task DescargarZipAsync()
        {
            if (!PuedeDescargarZip)
            {
                _notifications?.ShowWarning(TooltipDescargarZip, "Acceso Denegado");
                return;
            }

            if (!Directory.Exists(RutaArchivos))
            {
                _notifications?.ShowWarning("La carpeta configurada para este proyecto no existe o no se encuentra accesible en la red.", "Ruta no encontrada");
                return;
            }

            string nombreSugerido = $"{CodigoId}_{SanitizarNombreArchivo(Titulo)}.zip";
            string filtro = "Archivo Comprimido ZIP (*.zip)|*.zip|Todos los archivos (*.*)|*.*";

            string? rutaDestino = null;
            if (SaveFileDialogCustomHandler != null)
            {
                rutaDestino = SaveFileDialogCustomHandler(nombreSugerido, filtro);
            }
            else
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Guardar paquete ZIP del proyecto como...",
                    FileName = nombreSugerido,
                    Filter = filtro
                };

                if (sfd.ShowDialog() == true)
                {
                    rutaDestino = sfd.FileName;
                }
            }

            if (string.IsNullOrWhiteSpace(rutaDestino))
                return;

            await ExportarZipDirectoAsync(rutaDestino);
        }

        public async Task<bool> ExportarZipDirectoAsync(string rutaDestinoZip, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos) || !Directory.Exists(RutaArchivos))
                return false;

            try
            {
                IsDescargandoZip = true;
                ProgresoDescargaZip = 0;
                EstadoDescargaZipTexto = "Iniciando empaquetado...";

                var progress = new Progress<double>(p =>
                {
                    if (IsDescargandoZip)
                    {
                        ProgresoDescargaZip = p;
                        EstadoDescargaZipTexto = $"Empaquetando archivos... {p:F0}%";
                    }
                });

                bool exito = await _archivosService.EmpaquetarCarpetaZipAsync(RutaArchivos, rutaDestinoZip, progress, ct);

                if (exito)
                {
                    _notifications?.ShowSuccess($"El proyecto se ha empaquetado y descargado exitosamente en:\n{Path.GetFileName(rutaDestinoZip)}", "Empaquetado ZIP Exitoso");
                }
                return exito;
            }
            catch (OperationCanceledException)
            {
                _notifications?.ShowWarning("El empaquetado ZIP fue cancelado.", "Operación Cancelada");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error al empaquetar ZIP: {ex}");
                _notifications?.ShowError($"Error al generar el archivo ZIP: {ex.Message}", "Error de Exportación");
                return false;
            }
            finally
            {
                IsDescargandoZip = false;
                EstadoDescargaZipTexto = null;
                ProgresoDescargaZip = 0;
            }
        }

        private static string SanitizarNombreArchivo(string nombre)
        {
            var invalidos = Path.GetInvalidFileNameChars();
            var limpio = new string(nombre.Where(c => !invalidos.Contains(c)).ToArray()).Trim();
            limpio = limpio.Replace(' ', '_');
            return string.IsNullOrWhiteSpace(limpio) ? "Proyecto" : limpio;
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
            if (_historialUseCase == null) return;

            try
            {
                CargandoHistorial = true;
                var list = await _historialUseCase.EjecutarAsync(Id, ct);

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

        public async Task CargarHistorialArchivosAsync(CancellationToken ct = default)
        {
            if (_proyectoRepository == null || Id <= 0) return;

            try
            {
                CargandoHistorialArchivos = true;
                var list = await _proyectoRepository.ObtenerHistorialArchivosProyectoAsync(Id, limite: 50, ct);

                TotalMovimientosArchivos = list.Count;
                TieneMasMovimientosArchivos = list.Count > 5;

                HistorialArchivos.Clear();
                foreach (var item in list.Take(5))
                {
                    HistorialArchivos.Add(item);
                }

                HasHistorialArchivos = HistorialArchivos.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error al cargar historial de archivos: {ex.Message}");
            }
            finally
            {
                CargandoHistorialArchivos = false;
            }
        }

        [RelayCommand]
        public void VerMasActividadArchivos()
        {
            if (Id <= 0) return;

            var vm = new HistorialArchivoViewModel(
                idProyecto: Id,
                rutaRelativa: string.Empty,
                nombreArchivo: string.Empty,
                tituloProyecto: Titulo,
                proyectoRepository: _proyectoRepository,
                soloEliminados: false);

            if (AbrirModalHistorialAction != null)
            {
                AbrirModalHistorialAction(vm);
                return;
            }

            var win = new Views.HistorialArchivoWindow(vm)
            {
                Owner = Application.Current?.MainWindow
            };
            vm.RequestClose += () => win.Close();
            win.ShowDialog();
        }

        public async Task CargarFormatosArchivosAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos) || !PuedeLeerArchivos)
            {
                lock (_extensionesSyncLock)
                {
                    FormatosDetectados.Clear();
                    ExtensionesDetectadas.Clear();
                    GruposFormatos.Clear();
                    GruposFormatosFiltrados.Clear();
                    CategoriasTabs.Clear();
                    TotalArchivosDetectados = 0;
                    TamanoTotalDetectadoBytes = 0;
                    HasFormatosDetectados = false;
                    HasExtensionesDetectadas = false;
                    HasGruposFormatos = false;
                }
                return;
            }

            await _formatosLock.WaitAsync(ct);
            try
            {
                CargandoFormatos = true;
                TextoProgresoFormatos = "Analizando tipos de archivos en la carpeta...";

                var progreso = new Progress<ProgresoEscaneoFormatos>(p =>
                {
                    if (!CargandoFormatos) return;

                    TotalArchivosDetectados = p.TotalArchivos;
                    TextoProgresoFormatos = $"⏳ Analizando archivos en el repositorio... ({p.TotalArchivos:N0} encontrados)";

                    if (p.ExtensionesParciales.Count > 0)
                    {
                        var lista = p.ExtensionesParciales.Where(e => e != null).ToList();
                        lock (_extensionesSyncLock)
                        {
                            if (!CargandoFormatos) return;
                            ExtensionesDetectadas.Clear();
                            for (int i = 0; i < lista.Count; i++)
                            {
                                ExtensionesDetectadas.Add(lista[i]);
                            }
                            HasExtensionesDetectadas = true;

                            if (p.CategoriasParciales != null && p.CategoriasParciales.Count > 0)
                            {
                                FormatosDetectados.Clear();
                                foreach (var cat in p.CategoriasParciales)
                                {
                                    FormatosDetectados.Add(cat);
                                }
                                HasFormatosDetectados = true;
                            }

                            TamanoTotalDetectadoBytes = lista.Sum(e => e.TamanoTotalBytes);
                            GenerarGruposFormatos(ExtensionesDetectadas, FormatosDetectados, TamanoTotalDetectadoBytes);
                        }

                        var primarios = lista.Where(e => !e.EsAuxiliar).Select(e => $".{e.ExtensionMayus} ({e.CantidadArchivos})");
                        ResumenInsumosTexto = string.Join(", ", primarios);
                        NotificarExtensionesActualizadas();
                    }
                });

                var resumen = await _archivosService.EscanearFormatosArchivosAsync(RutaArchivos, progreso, ct);

                lock (_extensionesSyncLock)
                {
                    if (resumen.TotalArchivos == 0 && ExtensionesDetectadas.Count > 0)
                    {
                        return;
                    }

                    FormatosDetectados.Clear();
                    foreach (var cat in resumen.Categorias)
                    {
                        FormatosDetectados.Add(cat);
                    }

                    ExtensionesDetectadas.Clear();
                    foreach (var ext in resumen.ExtensionesLista)
                    {
                        if (ext != null)
                        {
                            ExtensionesDetectadas.Add(ext);
                        }
                    }

                    TotalArchivosDetectados = resumen.TotalArchivos;
                    TamanoTotalDetectadoBytes = resumen.TamanoTotalBytes;
                    HasFormatosDetectados = FormatosDetectados.Count > 0;
                    HasExtensionesDetectadas = ExtensionesDetectadas.Count > 0;

                    GenerarGruposFormatos(ExtensionesDetectadas, FormatosDetectados, TamanoTotalDetectadoBytes);
                }

                if (HasExtensionesDetectadas)
                {
                    var primarios = resumen.ExtensionesLista.Where(e => e != null && !e.EsAuxiliar).Select(e => $".{e.ExtensionMayus} ({e.CantidadArchivos})");
                    ResumenInsumosTexto = string.Join(", ", primarios);
                }
                else
                {
                    ResumenInsumosTexto = null;
                }
                NotificarExtensionesActualizadas();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error cargando formatos de archivo: {ex.Message}");
            }
            finally
            {
                CargandoFormatos = false;
                OnPropertyChanged(nameof(HasNoFormatosDetectados));
                OnPropertyChanged(nameof(ShowNoGruposFormatos));
                _formatosLock.Release();
            }
        }

        private static string ObtenerCategoriaEfectiva(FormatoExtensionItem ext, bool tieneVectorial, bool tieneRaster)
        {
            if (!ext.EsAuxiliar && !ext.Categoria.Equals(CatalogoFormatosGis.CategoriaAuxiliar, StringComparison.OrdinalIgnoreCase))
            {
                return ext.Categoria;
            }

            var extNorm = ext.Extension.Trim().ToLowerInvariant();
            if (extNorm == ".dbf" || extNorm == ".shx" || extNorm == ".prj" || extNorm == ".cpg" || extNorm == ".sbn" || extNorm == ".sbx" || extNorm == ".qmd")
            {
                return tieneVectorial ? CatalogoFormatosGis.CategoriaVectorial : CatalogoFormatosGis.CategoriaAuxiliar;
            }
            if (extNorm == ".tfw" || extNorm == ".ovr" || extNorm == ".rrd" || extNorm == ".j2w" || extNorm == ".wld")
            {
                return tieneRaster ? CatalogoFormatosGis.CategoriaRaster : CatalogoFormatosGis.CategoriaAuxiliar;
            }
            if (extNorm == ".aux.xml" || extNorm == ".aux")
            {
                if (tieneRaster) return CatalogoFormatosGis.CategoriaRaster;
                if (tieneVectorial) return CatalogoFormatosGis.CategoriaVectorial;
            }
            return ext.Categoria;
        }

        private void GenerarGruposFormatos(IEnumerable<FormatoExtensionItem> extensiones, IEnumerable<CategoriaFormatoItem> categorias, long tamanoTotal)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => GenerarGruposFormatos(extensiones, categorias, tamanoTotal));
                return;
            }

            var catMetaDict = categorias.Where(c => c != null).ToDictionary(c => c.Categoria, StringComparer.OrdinalIgnoreCase);

            var listaExt = extensiones.Where(e => e != null).ToList();
            bool tieneVectorial = listaExt.Any(e => !e.EsAuxiliar && e.Categoria.Equals(CatalogoFormatosGis.CategoriaVectorial, StringComparison.OrdinalIgnoreCase));
            bool tieneRaster = listaExt.Any(e => !e.EsAuxiliar && e.Categoria.Equals(CatalogoFormatosGis.CategoriaRaster, StringComparison.OrdinalIgnoreCase));

            var agrupados = listaExt
                .GroupBy(e => ObtenerCategoriaEfectiva(e, tieneVectorial, tieneRaster), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var grupos = new List<CategoriaGrupoItem>();

            foreach (var grp in agrupados)
            {
                string catNombre = grp.Key;
                catMetaDict.TryGetValue(catNombre, out var meta);

                var principales = grp.Where(e => !e.EsAuxiliar).OrderByDescending(e => e.CantidadArchivos).ToList();
                var auxiliares = grp.Where(e => e.EsAuxiliar).OrderByDescending(e => e.CantidadArchivos).ToList();

                var primerItem = grp.FirstOrDefault(e => !e.EsAuxiliar) ?? grp.FirstOrDefault();

                grupos.Add(new CategoriaGrupoItem
                {
                    Categoria = catNombre,
                    Icono = meta?.Icono ?? (primerItem?.Icono ?? "📁"),
                    BadgeBgHex = meta?.BadgeBgHex ?? (primerItem?.BadgeBgHex ?? "#F1F5F9"),
                    BadgeBorderHex = meta?.BadgeBorderHex ?? (primerItem?.BadgeBorderHex ?? "#CBD5E1"),
                    BadgeFgHex = meta?.BadgeFgHex ?? (primerItem?.BadgeFgHex ?? "#334155"),
                    CantidadArchivos = grp.Sum(e => e.CantidadArchivos),
                    TamanoTotalBytes = grp.Sum(e => e.TamanoTotalBytes),
                    ExtensionesPrincipales = principales,
                    ExtensionesAuxiliares = auxiliares
                });
            }

            grupos = grupos.OrderByDescending(g => g.CantidadArchivos).ToList();

            GruposFormatos.Clear();
            foreach (var g in grupos)
            {
                GruposFormatos.Add(g);
            }

            CategoriasTabs.Clear();
            CategoriasTabs.Add(new CategoriaTabItem
            {
                Nombre = "Todos",
                Icono = "📁",
                CantidadArchivos = TotalArchivosDetectados,
                IsSelected = string.IsNullOrWhiteSpace(FiltroCategoriaSeleccionada) || string.Equals(FiltroCategoriaSeleccionada, "Todos", StringComparison.OrdinalIgnoreCase)
            });

            foreach (var g in grupos)
            {
                CategoriasTabs.Add(new CategoriaTabItem
                {
                    Nombre = g.Categoria,
                    Icono = g.Icono,
                    CantidadArchivos = g.CantidadArchivos,
                    IsSelected = string.Equals(FiltroCategoriaSeleccionada, g.Categoria, StringComparison.OrdinalIgnoreCase)
                });
            }

            AplicarFiltroGruposFormatos();
        }

        public void AplicarFiltroGruposFormatos()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(AplicarFiltroGruposFormatos);
                return;
            }

            if (GruposFormatos.Count == 0)
            {
                GruposFormatosFiltrados.Clear();
                HasGruposFormatos = false;
                OnPropertyChanged(nameof(ShowNoGruposFormatos));
                return;
            }

            var query = GruposFormatos.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(FiltroCategoriaSeleccionada) &&
                !FiltroCategoriaSeleccionada.Equals("Todos", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(g => string.Equals(g.Categoria, FiltroCategoriaSeleccionada, StringComparison.OrdinalIgnoreCase));
            }

            var busqueda = BusquedaFormatoTexto?.Trim();
            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                var term = busqueda.TrimStart('.').ToLowerInvariant();
                var resultadoGrupos = new List<CategoriaGrupoItem>();

                foreach (var g in query)
                {
                    bool coincideCategoria = g.Categoria.Contains(busqueda, StringComparison.OrdinalIgnoreCase);

                    var princ = g.ExtensionesPrincipales
                        .Where(e => e.ExtensionMayus.ToLowerInvariant().Contains(term) ||
                                    e.Extension.ToLowerInvariant().Contains(term) ||
                                    e.Categoria.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var aux = g.ExtensionesAuxiliares
                        .Where(e => e.ExtensionMayus.ToLowerInvariant().Contains(term) ||
                                    e.Extension.ToLowerInvariant().Contains(term) ||
                                    e.Categoria.Contains(busqueda, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (coincideCategoria || princ.Count > 0 || aux.Count > 0)
                    {
                        var mostrarPrinc = coincideCategoria ? g.ExtensionesPrincipales : princ;
                        var mostrarAux = coincideCategoria ? g.ExtensionesAuxiliares : aux;

                        resultadoGrupos.Add(new CategoriaGrupoItem
                        {
                            Categoria = g.Categoria,
                            Icono = g.Icono,
                            BadgeBgHex = g.BadgeBgHex,
                            BadgeBorderHex = g.BadgeBorderHex,
                            BadgeFgHex = g.BadgeFgHex,
                            CantidadArchivos = g.CantidadArchivos,
                            TamanoTotalBytes = g.TamanoTotalBytes,
                            ExtensionesPrincipales = mostrarPrinc,
                            ExtensionesAuxiliares = mostrarAux,
                            MostrarAuxiliares = g.MostrarAuxiliares || aux.Count > 0
                        });
                    }
                }

                GruposFormatosFiltrados.Clear();
                foreach (var g in resultadoGrupos)
                {
                    GruposFormatosFiltrados.Add(g);
                }
            }
            else
            {
                GruposFormatosFiltrados.Clear();
                foreach (var g in query)
                {
                    GruposFormatosFiltrados.Add(g);
                }
            }

            HasGruposFormatos = GruposFormatosFiltrados.Count > 0;
            OnPropertyChanged(nameof(ShowNoGruposFormatos));
        }

        partial void OnFiltroMunicipioTextoChanged(string value)
        {
            OnPropertyChanged(nameof(HasFiltroMunicipioTexto));
            OnPropertyChanged(nameof(HasNoFiltroMunicipioTexto));
            AplicarFiltroMunicipios();
        }

        partial void OnFiltroDepartamentoSeleccionadoChanged(DepartamentoFiltroItem? value)
        {
            AplicarFiltroMunicipios();
        }

        private void AplicarFiltroMunicipios()
        {
            if (MunicipiosTodos == null || MunicipiosTodos.Count == 0)
            {
                MunicipiosFiltrados.Clear();
                NotificarPropiedadesFiltro();
                return;
            }

            var query = MunicipiosTodos.AsEnumerable();

            if (FiltroDepartamentoSeleccionado != null && !FiltroDepartamentoSeleccionado.EsTodos)
            {
                query = query.Where(m => string.Equals(m.Departamento, FiltroDepartamentoSeleccionado.Nombre, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(FiltroMunicipioTexto))
            {
                var busqueda = FiltroMunicipioTexto.Trim();
                query = query.Where(m =>
                    m.Nombre.Contains(busqueda, StringComparison.OrdinalIgnoreCase) ||
                    m.Departamento.Contains(busqueda, StringComparison.OrdinalIgnoreCase) ||
                    m.Codigo.Contains(busqueda, StringComparison.OrdinalIgnoreCase));
            }

            var resultados = query.ToList();
            MunicipiosFiltrados.Clear();
            foreach (var item in resultados)
            {
                MunicipiosFiltrados.Add(item);
            }

            NotificarPropiedadesFiltro();
        }

        private void NotificarPropiedadesFiltro()
        {
            OnPropertyChanged(nameof(TextoConteoMunicipiosFiltrados));
            OnPropertyChanged(nameof(TieneFiltroActivo));
            OnPropertyChanged(nameof(HasNoMunicipiosFiltrados));
        }

        private void LimpiarFiltrosMunicipio()
        {
            FiltroMunicipioTexto = string.Empty;
            FiltroDepartamentoSeleccionado = DepartamentosFiltro.FirstOrDefault(d => d.EsTodos) ?? DepartamentosFiltro.FirstOrDefault();
        }

        private void CopiarMunicipios()
        {
            var itemsACopiar = TieneFiltroActivo ? (IReadOnlyCollection<MunicipioFichaItem>)MunicipiosFiltrados : MunicipiosTodos;
            if (itemsACopiar.Count == 0) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Código DIVIPOLA\tMunicipio\tDepartamento");
                foreach (var m in itemsACopiar)
                {
                    sb.AppendLine($"{m.Codigo}\t{m.Nombre}\t{m.Departamento}");
                }
                Clipboard.SetText(sb.ToString());
                string mensaje = TieneFiltroActivo
                    ? $"Se copiaron {itemsACopiar.Count} municipios filtrados al portapapeles."
                    : $"Se copiaron {itemsACopiar.Count} municipios al portapapeles.";
                _notifications?.ShowSuccess(mensaje, "Copiado");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error copiando listado de municipios: {ex}");
            }
        }

        private void InicializarMunicipios(string? nombresRaw, string? codigosRaw)
        {
            if (string.IsNullOrWhiteSpace(nombresRaw))
            {
                MunicipiosTodos = Array.Empty<MunicipioFichaItem>();
                DepartamentosFiltro = Array.Empty<DepartamentoFiltroItem>();
                MunicipiosFiltrados.Clear();
                TotalDepartamentos = 0;
                MunicipioChipTexto = string.Empty;
                MunicipioChipTooltip = string.Empty;
                return;
            }

            var codigos = string.IsNullOrWhiteSpace(codigosRaw)
                ? Array.Empty<string>()
                : codigosRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            List<string> partes;
            if (nombresRaw.Contains(')'))
            {
                partes = System.Text.RegularExpressions.Regex.Split(nombresRaw.Trim(), @"(?<=\))\s*,\s*")
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList();
            }
            else
            {
                partes = nombresRaw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            }

            var items = new List<MunicipioFichaItem>(partes.Count);
            for (int i = 0; i < partes.Count; i++)
            {
                var parte = partes[i];
                var match = System.Text.RegularExpressions.Regex.Match(parte, @"^(?<nombre>.+?)\s*\((?<dpto>[^)]+)\)$");
                string nombre;
                string departamento;
                if (match.Success)
                {
                    nombre = match.Groups["nombre"].Value.Trim();
                    departamento = match.Groups["dpto"].Value.Trim();
                }
                else
                {
                    nombre = parte.Trim();
                    departamento = string.Empty;
                }

                string codigo = (i < codigos.Length) ? codigos[i].Trim() : string.Empty;
                string display = !string.IsNullOrEmpty(departamento) ? $"{nombre} ({departamento})" : nombre;
                items.Add(new MunicipioFichaItem(codigo, nombre, departamento, display));
            }

            MunicipiosTodos = items;

            var dptosConteo = items
                .Where(m => !string.IsNullOrWhiteSpace(m.Departamento))
                .GroupBy(m => m.Departamento, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DepartamentoFiltroItem(g.Key, g.Count()))
                .ToList();

            TotalDepartamentos = dptosConteo.Count;

            var listaCombo = new List<DepartamentoFiltroItem>();
            if (dptosConteo.Count > 1)
            {
                listaCombo.Add(new DepartamentoFiltroItem("Todos los departamentos", items.Count, EsTodos: true));
            }
            listaCombo.AddRange(dptosConteo);
            DepartamentosFiltro = listaCombo;
            FiltroDepartamentoSeleccionado = listaCombo.FirstOrDefault();

            if (items.Count == 1)
            {
                MunicipioChipTexto = items[0].DisplayCompleto;
                MunicipioChipTooltip = items[0].DisplayCompleto;
            }
            else if (items.Count <= 3)
            {
                MunicipioChipTexto = string.Join(", ", items.Select(m => m.Nombre));
                MunicipioChipTooltip = string.Join(", ", items.Select(m => m.DisplayCompleto));
            }
            else
            {
                MunicipioChipTexto = TotalDepartamentos > 0
                    ? $"{items.Count} municipios ({TotalDepartamentos} dptos.)"
                    : $"{items.Count} municipios";

                var dptosPrincipales = dptosConteo.Take(3).Select(d => $"{d.Nombre} ({d.Cantidad})");
                string resumenDptos = string.Join(", ", dptosPrincipales);
                if (dptosConteo.Count > 3) resumenDptos += "...";

                MunicipioChipTooltip = $"{items.Count} municipios en {TotalDepartamentos} departamento(s).\n" +
                                       $"Principales: {resumenDptos}\n" +
                                       "Consulte la Sección 2 para explorar o filtrar la lista completa.";
            }

            MunicipiosFiltrados.Clear();
            foreach (var item in items)
            {
                MunicipiosFiltrados.Add(item);
            }
        }
    }
}
