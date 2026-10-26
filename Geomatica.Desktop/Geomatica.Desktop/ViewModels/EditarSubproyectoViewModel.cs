using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Geomatica.Desktop.ViewModels
{
    /// <summary>
    /// ViewModel para la ventana emergente de edición o creación individual de un subproyecto / frente de trabajo.
    /// Proporciona validación en tiempo real de campos obligatorios, catálogo DIVIPOLA y existencia física de subcarpeta en disco.
    /// </summary>
    public class EditarSubproyectoViewModel : ObservableObject
    {
        private readonly SubproyectoItemFormulario? _subproyectoOriginal;
        private readonly string? _rutaProyecto;
        private readonly IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? _municipiosDisponibles;
        private readonly IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? _todosMunicipios;
        private readonly string? _latProyecto;
        private readonly string? _lonProyecto;
        private readonly string _nombreOriginal;

        public event Action<bool>? RequestClose;

        /// <summary>
        /// Gancho delegado para pruebas unitarias del selector de carpeta sin invocar diálogos WPF.
        /// </summary>
        public Func<string?>? SelectorCarpetaHandler { get; set; }

        public int IdSubproyecto { get; }
        public int IdProyecto { get; }
        public bool EsNuevo { get; }
        public string Titulo { get; }
        public string Subtitulo { get; }

        private string _nombre = string.Empty;
        public string Nombre
        {
            get => _nombre;
            set
            {
                if (SetProperty(ref _nombre, value))
                {
                    ValidarCampos();
                }
            }
        }

        private string _subcarpeta = string.Empty;
        public string Subcarpeta
        {
            get => _subcarpeta;
            set
            {
                if (SetProperty(ref _subcarpeta, value))
                {
                    ValidarCampos();
                }
            }
        }

        private string _municipioTexto = string.Empty;
        public string MunicipioTexto
        {
            get => _municipioTexto;
            set
            {
                if (SetProperty(ref _municipioTexto, value))
                {
                    ValidarCampos();
                }
            }
        }

        private FormularioProyectoViewModel.MunicipioItem? _municipioSeleccionado;
        public FormularioProyectoViewModel.MunicipioItem? MunicipioSeleccionado
        {
            get => _municipioSeleccionado;
            set
            {
                if (SetProperty(ref _municipioSeleccionado, value))
                {
                    if (value != null)
                    {
                        _municipioTexto = value.Nombre;
                        OnPropertyChanged(nameof(MunicipioTexto));
                    }
                    ValidarCampos();
                }
            }
        }

        private string _latStr = string.Empty;
        public string LatStr
        {
            get => _latStr;
            set
            {
                if (SetProperty(ref _latStr, value))
                {
                    ValidarCampos();
                }
            }
        }

        private string _lonStr = string.Empty;
        public string LonStr
        {
            get => _lonStr;
            set
            {
                if (SetProperty(ref _lonStr, value))
                {
                    ValidarCampos();
                }
            }
        }

        private string? _descripcion;
        public string? Descripcion
        {
            get => _descripcion;
            set => SetProperty(ref _descripcion, value);
        }

        public ObservableCollection<FormularioProyectoViewModel.MunicipioItem> MunicipiosSugeridos { get; } = new();
        public ObservableCollection<string> SubcarpetasDisponibles { get; } = new();

        public bool PuedeCopiarCoordenadas => !string.IsNullOrWhiteSpace(_latProyecto) && !string.IsNullOrWhiteSpace(_lonProyecto);

        // ═══════════════════════════════════════════════════════════
        // ESTADOS DE VALIDACIÓN REACTIVA
        // ═══════════════════════════════════════════════════════════

        private bool _nombreEsValido;
        public bool NombreEsValido
        {
            get => _nombreEsValido;
            private set => SetProperty(ref _nombreEsValido, value);
        }

        private string? _nombreMensaje;
        public string? NombreMensaje
        {
            get => _nombreMensaje;
            private set => SetProperty(ref _nombreMensaje, value);
        }

        private bool _subcarpetaSintaxisValida;
        public bool SubcarpetaSintaxisValida
        {
            get => _subcarpetaSintaxisValida;
            private set => SetProperty(ref _subcarpetaSintaxisValida, value);
        }

        private bool _subcarpetaExisteEnDisco;
        public bool SubcarpetaExisteEnDisco
        {
            get => _subcarpetaExisteEnDisco;
            private set => SetProperty(ref _subcarpetaExisteEnDisco, value);
        }

        private string _subcarpetaEstadoIcono = "ℹ️";
        public string SubcarpetaEstadoIcono
        {
            get => _subcarpetaEstadoIcono;
            private set => SetProperty(ref _subcarpetaEstadoIcono, value);
        }

        private string _subcarpetaEstadoTexto = string.Empty;
        public string SubcarpetaEstadoTexto
        {
            get => _subcarpetaEstadoTexto;
            private set => SetProperty(ref _subcarpetaEstadoTexto, value);
        }

        private bool _municipioEsValido = true;
        public bool MunicipioEsValido
        {
            get => _municipioEsValido;
            private set => SetProperty(ref _municipioEsValido, value);
        }

        private string _municipioEstadoIcono = "ℹ️";
        public string MunicipioEstadoIcono
        {
            get => _municipioEstadoIcono;
            private set => SetProperty(ref _municipioEstadoIcono, value);
        }

        private string _municipioEstadoTexto = string.Empty;
        public string MunicipioEstadoTexto
        {
            get => _municipioEstadoTexto;
            private set => SetProperty(ref _municipioEstadoTexto, value);
        }

        public string? MpioCdpmpDetectado { get; private set; }
        public string? MunicipioNombreDetectado { get; private set; }

        private bool _coordenadasValidas = true;
        public bool CoordenadasValidas
        {
            get => _coordenadasValidas;
            private set => SetProperty(ref _coordenadasValidas, value);
        }

        private string _coordenadasEstadoIcono = "ℹ️";
        public string CoordenadasEstadoIcono
        {
            get => _coordenadasEstadoIcono;
            private set => SetProperty(ref _coordenadasEstadoIcono, value);
        }

        private string _coordenadasEstadoTexto = string.Empty;
        public string CoordenadasEstadoTexto
        {
            get => _coordenadasEstadoTexto;
            private set => SetProperty(ref _coordenadasEstadoTexto, value);
        }

        public double LatitudParsed { get; private set; }
        public double LongitudParsed { get; private set; }

        private bool _puedeGuardar;
        public bool PuedeGuardar
        {
            get => _puedeGuardar;
            private set => SetProperty(ref _puedeGuardar, value);
        }

        private string? _mensajeResumenError;
        public string? MensajeResumenError
        {
            get => _mensajeResumenError;
            private set => SetProperty(ref _mensajeResumenError, value);
        }

        // ═══════════════════════════════════════════════════════════
        // COMANDOS
        // ═══════════════════════════════════════════════════════════

        public IRelayCommand SeleccionarSubcarpetaCommand { get; }
        public IRelayCommand CopiarCoordenadasProyectoCommand { get; }
        public IRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }

        public EditarSubproyectoViewModel(
            SubproyectoItemFormulario? subproyecto,
            string? rutaProyecto,
            IEnumerable<string>? subcarpetasDisponibles,
            IEnumerable<FormularioProyectoViewModel.MunicipioItem>? municipiosDisponibles,
            IEnumerable<FormularioProyectoViewModel.MunicipioItem>? todosMunicipiosCatalogo = null,
            string? latProyectoStr = null,
            string? lonProyectoStr = null)
        {
            _subproyectoOriginal = subproyecto;
            _rutaProyecto = rutaProyecto;
            _municipiosDisponibles = municipiosDisponibles?.ToList();
            _todosMunicipios = todosMunicipiosCatalogo?.ToList();
            _latProyecto = latProyectoStr;
            _lonProyecto = lonProyectoStr;

            EsNuevo = subproyecto == null || subproyecto.IdSubproyecto <= 0;
            IdSubproyecto = subproyecto?.IdSubproyecto ?? 0;
            IdProyecto = subproyecto?.IdProyecto ?? 0;
            _nombreOriginal = subproyecto?.Nombre ?? string.Empty;

            Titulo = EsNuevo ? "➕ Nuevo Subproyecto / Frente de Trabajo" : $"✏️ Editar Subproyecto: {_nombreOriginal}";
            Subtitulo = EsNuevo
                ? "Defina los datos, municipio territorial, subcarpeta y coordenadas del nuevo frente de obra."
                : "Modifique los atributos, municipio territorial, subcarpeta y coordenadas geográficas del subproyecto.";

            // Poblar carpetas disponibles
            if (subcarpetasDisponibles != null)
            {
                foreach (var sc in subcarpetasDisponibles)
                {
                    if (!string.IsNullOrWhiteSpace(sc) && !SubcarpetasDisponibles.Contains(sc))
                    {
                        SubcarpetasDisponibles.Add(sc);
                    }
                }
            }

            // Poblar municipios sugeridos
            if (_municipiosDisponibles != null)
            {
                foreach (var m in _municipiosDisponibles)
                {
                    if (!MunicipiosSugeridos.Any(x => x.Codigo == m.Codigo))
                    {
                        MunicipiosSugeridos.Add(m);
                    }
                }
            }

            if (_todosMunicipios != null && MunicipiosSugeridos.Count == 0)
            {
                foreach (var m in _todosMunicipios)
                {
                    MunicipiosSugeridos.Add(m);
                }
            }

            // Cargar valores iniciales desde el subproyecto existente
            if (subproyecto != null)
            {
                _nombre = subproyecto.Nombre;
                _subcarpeta = subproyecto.Subcarpeta;
                _latStr = subproyecto.Lat != 0 ? subproyecto.Lat.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
                _lonStr = subproyecto.Lon != 0 ? subproyecto.Lon.ToString("F6", CultureInfo.InvariantCulture) : string.Empty;
                _descripcion = subproyecto.Descripcion;

                MpioCdpmpDetectado = subproyecto.MpioCdpmp;
                MunicipioNombreDetectado = subproyecto.MunicipioNombre;

                if (!string.IsNullOrWhiteSpace(subproyecto.MunicipioNombre))
                {
                    _municipioTexto = subproyecto.MunicipioNombre;
                }
                else if (!string.IsNullOrWhiteSpace(subproyecto.MpioCdpmp))
                {
                    _municipioTexto = subproyecto.MpioCdpmp;
                }

                _municipioSeleccionado = MunicipiosSugeridos.FirstOrDefault(m => m.Codigo == subproyecto.MpioCdpmp);
            }
            else
            {
                // Sugerir municipio del proyecto por defecto
                _municipioSeleccionado = MunicipiosSugeridos.FirstOrDefault();
                if (_municipioSeleccionado != null)
                {
                    _municipioTexto = _municipioSeleccionado.Nombre;
                }

                // Sugerir coordenadas del proyecto si existen
                if (!string.IsNullOrWhiteSpace(_latProyecto)) _latStr = _latProyecto;
                if (!string.IsNullOrWhiteSpace(_lonProyecto)) _lonStr = _lonProyecto;
            }

            SeleccionarSubcarpetaCommand = new RelayCommand(SeleccionarSubcarpeta);
            CopiarCoordenadasProyectoCommand = new RelayCommand(CopiarCoordenadasProyecto, () => PuedeCopiarCoordenadas);
            GuardarCommand = new RelayCommand(EjecutarGuardar, () => PuedeGuardar);
            CancelarCommand = new RelayCommand(() => RequestClose?.Invoke(false));

            ValidarCampos();
        }

        private void CopiarCoordenadasProyecto()
        {
            if (!string.IsNullOrWhiteSpace(_latProyecto)) LatStr = _latProyecto;
            if (!string.IsNullOrWhiteSpace(_lonProyecto)) LonStr = _lonProyecto;
        }

        private void SeleccionarSubcarpeta()
        {
            string? carpetaSeleccionada = null;
            if (SelectorCarpetaHandler != null)
            {
                carpetaSeleccionada = SelectorCarpetaHandler();
            }
            else
            {
                try
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "Seleccionar subcarpeta del subproyecto / frente de trabajo",
                        Multiselect = false
                    };

                    if (!string.IsNullOrWhiteSpace(_rutaProyecto) && Directory.Exists(_rutaProyecto))
                    {
                        dialog.InitialDirectory = _rutaProyecto;
                    }

                    if (dialog.ShowDialog() == true)
                    {
                        carpetaSeleccionada = dialog.FolderName;
                    }
                }
                catch
                {
                    // Fallback silencioso si no hay soporte de diálogo
                }
            }

            if (!string.IsNullOrWhiteSpace(carpetaSeleccionada))
            {
                string subRel;
                if (!string.IsNullOrWhiteSpace(_rutaProyecto) &&
                    carpetaSeleccionada.StartsWith(_rutaProyecto, StringComparison.OrdinalIgnoreCase))
                {
                    subRel = Path.GetRelativePath(_rutaProyecto, carpetaSeleccionada);
                    if (subRel == ".") subRel = "";
                }
                else
                {
                    subRel = Path.GetFileName(carpetaSeleccionada) ?? carpetaSeleccionada;
                }

                Subcarpeta = subRel.Replace('/', '\\').Trim('\\');
            }
        }

        public void ValidarCampos()
        {
            // 1. Validar Nombre
            if (string.IsNullOrWhiteSpace(Nombre))
            {
                NombreEsValido = false;
                NombreMensaje = "El nombre del subproyecto es obligatorio.";
            }
            else
            {
                NombreEsValido = true;
                NombreMensaje = null;
            }

            // 2. Validar Subcarpeta
            char[] caracteresInvalidos = new[] { '*', '?', '"', '<', '>', '|', ':' };
            if (string.IsNullOrWhiteSpace(Subcarpeta))
            {
                SubcarpetaSintaxisValida = false;
                SubcarpetaExisteEnDisco = false;
                SubcarpetaEstadoIcono = "❌";
                SubcarpetaEstadoTexto = "Debe indicar la subcarpeta relativa del subproyecto.";
            }
            else if (Subcarpeta.IndexOfAny(caracteresInvalidos) >= 0)
            {
                SubcarpetaSintaxisValida = false;
                SubcarpetaExisteEnDisco = false;
                SubcarpetaEstadoIcono = "❌";
                SubcarpetaEstadoTexto = "La ruta contiene caracteres no permitidos (*, ?, \", <, >, |, :)";
            }
            else
            {
                SubcarpetaSintaxisValida = true;
                string subNorm = Subcarpeta.Trim().Replace('/', '\\').TrimStart('\\');

                if (!string.IsNullOrWhiteSpace(_rutaProyecto))
                {
                    try
                    {
                        string rutaAbs = Path.Combine(_rutaProyecto, subNorm);
                        if (Directory.Exists(rutaAbs))
                        {
                            SubcarpetaExisteEnDisco = true;
                            SubcarpetaEstadoIcono = "✅";
                            SubcarpetaEstadoTexto = $"Carpeta existente en disco:\n{rutaAbs}";
                        }
                        else
                        {
                            SubcarpetaExisteEnDisco = false;
                            SubcarpetaEstadoIcono = "⚠️";
                            SubcarpetaEstadoTexto = $"La carpeta física no existe aún en disco:\n{rutaAbs}\n(Se creará o copiará luego)";
                        }
                    }
                    catch (Exception ex)
                    {
                        SubcarpetaExisteEnDisco = false;
                        SubcarpetaEstadoIcono = "⚠️";
                        SubcarpetaEstadoTexto = $"Ruta relativa válida: {subNorm} ({ex.Message})";
                    }
                }
                else if (SubcarpetasDisponibles.Any(s => string.Equals(s.Replace('/', '\\').Trim('\\'), subNorm, StringComparison.OrdinalIgnoreCase)))
                {
                    SubcarpetaExisteEnDisco = true;
                    SubcarpetaEstadoIcono = "✅";
                    SubcarpetaEstadoTexto = "Subcarpeta reconocida en el proyecto";
                }
                else
                {
                    SubcarpetaExisteEnDisco = false;
                    SubcarpetaEstadoIcono = "ℹ️";
                    SubcarpetaEstadoTexto = $"Ruta relativa en el proyecto: /{subNorm}";
                }
            }

            // 3. Validar Municipio
            if (string.IsNullOrWhiteSpace(MunicipioTexto))
            {
                MunicipioEsValido = true;
                MunicipioEstadoIcono = "ℹ️";
                MunicipioEstadoTexto = "Sin municipio territorial asignado (opcional)";
                MpioCdpmpDetectado = null;
                MunicipioNombreDetectado = null;
            }
            else
            {
                var match = BuscarCoincidenciaMunicipio(MunicipioTexto.Trim());
                if (match != null)
                {
                    MunicipioEsValido = true;
                    MunicipioEstadoIcono = "✅";
                    MunicipioEstadoTexto = $"Municipio válido: {match.Nombre} (DANE {match.Codigo})";
                    MpioCdpmpDetectado = match.Codigo;
                    MunicipioNombreDetectado = match.Nombre;
                }
                else if ((_municipiosDisponibles == null || _municipiosDisponibles.Count == 0) &&
                         (_todosMunicipios == null || _todosMunicipios.Count == 0))
                {
                    // Sin catálogo cargado, no bloquear
                    MunicipioEsValido = true;
                    MunicipioEstadoIcono = "ℹ️";
                    MunicipioEstadoTexto = $"Municipio ingresado: {MunicipioTexto.Trim()}";
                    MpioCdpmpDetectado = null;
                    MunicipioNombreDetectado = MunicipioTexto.Trim();
                }
                else
                {
                    MunicipioEsValido = false;
                    MunicipioEstadoIcono = "❌";
                    MunicipioEstadoTexto = $"El texto '{MunicipioTexto.Trim()}' no coincide con ningún municipio válido en DIVIPOLA";
                    MpioCdpmpDetectado = null;
                    MunicipioNombreDetectado = MunicipioTexto.Trim();
                }
            }

            // 4. Validar Coordenadas
            string latClean = LatStr?.Trim().Replace(',', '.') ?? string.Empty;
            string lonClean = LonStr?.Trim().Replace(',', '.') ?? string.Empty;

            if (string.IsNullOrWhiteSpace(latClean) && string.IsNullOrWhiteSpace(lonClean))
            {
                CoordenadasValidas = true;
                LatitudParsed = 0;
                LongitudParsed = 0;
                CoordenadasEstadoIcono = "ℹ️";
                CoordenadasEstadoTexto = "Sin coordenadas asignadas (opcional)";
            }
            else
            {
                bool latOk = double.TryParse(latClean, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLat);
                bool lonOk = double.TryParse(lonClean, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLon);

                if (latOk && lonOk && parsedLat >= -90 && parsedLat <= 90 && parsedLon >= -180 && parsedLon <= 180)
                {
                    CoordenadasValidas = true;
                    LatitudParsed = parsedLat;
                    LongitudParsed = parsedLon;
                    CoordenadasEstadoIcono = "📍";
                    CoordenadasEstadoTexto = $"{parsedLat.ToString("F4", CultureInfo.InvariantCulture)}°N, {parsedLon.ToString("F4", CultureInfo.InvariantCulture)}°W";
                }
                else
                {
                    CoordenadasValidas = false;
                    LatitudParsed = 0;
                    LongitudParsed = 0;
                    CoordenadasEstadoIcono = "❌";
                    CoordenadasEstadoTexto = "Latitud (-90 a 90) o longitud (-180 a 180) decimal no válida";
                }
            }

            // 5. Decisión general de guardado
            PuedeGuardar = NombreEsValido && SubcarpetaSintaxisValida && MunicipioEsValido && CoordenadasValidas;

            if (!NombreEsValido)
            {
                MensajeResumenError = "⚠️ Ingrese un nombre para el subproyecto.";
            }
            else if (!SubcarpetaSintaxisValida)
            {
                MensajeResumenError = "⚠️ Indique una subcarpeta válida sin caracteres prohibidos.";
            }
            else if (!MunicipioEsValido)
            {
                MensajeResumenError = "⚠️ El municipio territorial no es válido en el catálogo DIVIPOLA.";
            }
            else if (!CoordenadasValidas)
            {
                MensajeResumenError = "⚠️ Las coordenadas geográficas ingresadas no son válidas.";
            }
            else
            {
                MensajeResumenError = null;
            }

            GuardarCommand.NotifyCanExecuteChanged();
        }

        private FormularioProyectoViewModel.MunicipioItem? BuscarCoincidenciaMunicipio(string texto)
        {
            var coleccion = new List<FormularioProyectoViewModel.MunicipioItem>();
            if (_municipiosDisponibles != null) coleccion.AddRange(_municipiosDisponibles);
            if (_todosMunicipios != null)
            {
                foreach (var m in _todosMunicipios)
                {
                    if (!coleccion.Any(x => x.Codigo == m.Codigo))
                        coleccion.Add(m);
                }
            }

            if (coleccion.Count == 0) return null;

            var porCod = coleccion.FirstOrDefault(m => string.Equals(m.Codigo, texto, StringComparison.OrdinalIgnoreCase));
            if (porCod != null) return porCod;

            var porNombre = coleccion.FirstOrDefault(m => string.Equals(m.Nombre, texto, StringComparison.OrdinalIgnoreCase));
            if (porNombre != null) return porNombre;

            string slug = ImportarSubproyectosCsvViewModel.GenerarSlug(texto);
            var porSlug = coleccion.FirstOrDefault(m => ImportarSubproyectosCsvViewModel.GenerarSlug(m.Nombre) == slug);
            if (porSlug != null) return porSlug;

            var porContiene = coleccion.FirstOrDefault(m =>
                m.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                texto.Contains(m.Nombre, StringComparison.OrdinalIgnoreCase));
            if (porContiene != null) return porContiene;

            return null;
        }

        public void AplicarCambiosASubproyecto(SubproyectoItemFormulario destino)
        {
            destino.Nombre = Nombre.Trim();
            destino.Subcarpeta = Subcarpeta.Trim().Replace('/', '\\').Trim('\\');
            destino.MpioCdpmp = MpioCdpmpDetectado;
            destino.MunicipioNombre = MunicipioNombreDetectado;
            destino.Lat = LatitudParsed;
            destino.Lon = LongitudParsed;
            destino.Descripcion = Descripcion?.Trim();
            destino.EsModificado = true;
            destino.NotificarTodos();
        }

        public SubproyectoItemFormulario CrearNuevoSubproyecto()
        {
            var nuevo = new SubproyectoItemFormulario
            {
                IdSubproyecto = 0,
                IdProyecto = IdProyecto,
                Nombre = Nombre.Trim(),
                Subcarpeta = Subcarpeta.Trim().Replace('/', '\\').Trim('\\'),
                MpioCdpmp = MpioCdpmpDetectado,
                MunicipioNombre = MunicipioNombreDetectado,
                Lat = LatitudParsed,
                Lon = LongitudParsed,
                Descripcion = Descripcion?.Trim(),
                EsModificado = true
            };
            return nuevo;
        }

        private void EjecutarGuardar()
        {
            ValidarCampos();
            if (!PuedeGuardar) return;

            if (_subproyectoOriginal != null)
            {
                AplicarCambiosASubproyecto(_subproyectoOriginal);
            }

            RequestClose?.Invoke(true);
        }
    }
}
