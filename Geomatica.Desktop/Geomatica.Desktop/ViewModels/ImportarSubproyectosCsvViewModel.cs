using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Geomatica.Desktop.ViewModels
{
    /// <summary>
    /// Fila individual analizada del archivo CSV/TXT para revisión antes de importar.
    /// </summary>
    public class SubproyectoCsvPreviewItem : ObservableObject
    {
        private int _numeroFila;
        private int _idSubproyecto;
        private bool _incluir = true;
        private string _nombre = string.Empty;
        private string _subcarpeta = string.Empty;
        private string _latStr = string.Empty;
        private string _lonStr = string.Empty;
        private string _municipio = string.Empty;
        private string? _mpioCodigo;
        private string? _descripcion;

        public int NumeroFila
        {
            get => _numeroFila;
            set => SetProperty(ref _numeroFila, value);
        }

        public int IdSubproyecto
        {
            get => _idSubproyecto;
            set => SetProperty(ref _idSubproyecto, value);
        }

        public bool Incluir
        {
            get => _incluir;
            set => SetProperty(ref _incluir, value);
        }

        public string Nombre
        {
            get => _nombre;
            set
            {
                if (SetProperty(ref _nombre, value))
                    Validar();
            }
        }

        public string Subcarpeta
        {
            get => _subcarpeta;
            set
            {
                if (SetProperty(ref _subcarpeta, value))
                    Validar();
            }
        }

        public string LatStr
        {
            get => _latStr;
            set
            {
                if (SetProperty(ref _latStr, value))
                    Validar();
            }
        }

        public string LonStr
        {
            get => _lonStr;
            set
            {
                if (SetProperty(ref _lonStr, value))
                    Validar();
            }
        }

        public string Municipio
        {
            get => _municipio;
            set
            {
                if (SetProperty(ref _municipio, value))
                    Validar();
            }
        }

        public string? MpioCodigo
        {
            get => _mpioCodigo;
            set => SetProperty(ref _mpioCodigo, value);
        }

        public string? Descripcion
        {
            get => _descripcion;
            set => SetProperty(ref _descripcion, value);
        }

        public double Lat { get; private set; }
        public double Lon { get; private set; }
        public bool TieneCoordenadasValidas { get; private set; }
        public bool EsValidoParaImportar { get; private set; }
        public string EstadoIcono { get; private set; } = "✅";
        public string EstadoTexto { get; private set; } = "Listo";

        // Verificación de Municipio
        public bool MunicipioEsValido { get; private set; } = true;
        public string MunicipioEstadoIcono { get; private set; } = "✅";
        public string MunicipioEstadoTexto { get; private set; } = "";

        // Verificación de Subcarpeta
        public bool SubcarpetaSintaxisValida { get; private set; } = true;
        public bool SubcarpetaExisteEnDisco { get; private set; } = true;
        public string SubcarpetaEstadoIcono { get; private set; } = "✅";
        public string SubcarpetaEstadoTexto { get; private set; } = "";

        // Delegados de validación inyectados por el ViewModel
        public Func<string?, (bool Valido, string? Codigo, string? NombreOficial, string? Mensaje)>? ValidadorMunicipio { get; set; }
        public Func<string?, (bool SintaxisValida, bool ExisteEnDisco, string? Mensaje)>? ValidadorSubcarpeta { get; set; }

        public string CoordenadasTexto => TieneCoordenadasValidas
            ? $"{Lat.ToString("F4", CultureInfo.InvariantCulture)}°N, {Lon.ToString("F4", CultureInfo.InvariantCulture)}°W"
            : "Sin coordenadas";

        public void Validar()
        {
            bool tieneNombre = !string.IsNullOrWhiteSpace(Nombre);
            bool latOk = TryParseCoordenada(LatStr, out double lat);
            bool lonOk = TryParseCoordenada(LonStr, out double lon);

            TieneCoordenadasValidas = latOk && lonOk && (lat != 0 || lon != 0);
            Lat = latOk ? lat : 0;
            Lon = lonOk ? lon : 0;

            // 1. Verificación de Municipio
            if (ValidadorMunicipio != null)
            {
                var resMpio = ValidadorMunicipio(Municipio);
                MunicipioEsValido = resMpio.Valido;
                if (!string.IsNullOrWhiteSpace(resMpio.Codigo))
                {
                    _mpioCodigo = resMpio.Codigo;
                }
                else if (string.IsNullOrWhiteSpace(Municipio))
                {
                    _mpioCodigo = null;
                }
                MunicipioEstadoTexto = resMpio.Mensaje ?? "";
                MunicipioEstadoIcono = !MunicipioEsValido ? "❌" : (string.IsNullOrWhiteSpace(Municipio) ? "ℹ️" : "✅");
            }
            else
            {
                MunicipioEsValido = true;
                MunicipioEstadoIcono = string.IsNullOrWhiteSpace(Municipio) ? "ℹ️" : "✅";
                MunicipioEstadoTexto = string.IsNullOrWhiteSpace(Municipio) ? "Sin municipio asignado (opcional)" : Municipio;
            }

            // 2. Verificación de Subcarpeta
            if (ValidadorSubcarpeta != null)
            {
                var resSub = ValidadorSubcarpeta(Subcarpeta);
                SubcarpetaSintaxisValida = resSub.SintaxisValida;
                SubcarpetaExisteEnDisco = resSub.ExisteEnDisco;
                SubcarpetaEstadoTexto = resSub.Mensaje ?? "";
                SubcarpetaEstadoIcono = !SubcarpetaSintaxisValida ? "❌" : (string.IsNullOrWhiteSpace(Subcarpeta) ? "ℹ️" : (!SubcarpetaExisteEnDisco ? "⚠️" : "✅"));
            }
            else
            {
                char[] caracteresInvalidos = new[] { '*', '?', '"', '<', '>', '|', ':' };
                SubcarpetaSintaxisValida = string.IsNullOrWhiteSpace(Subcarpeta) || Subcarpeta.IndexOfAny(caracteresInvalidos) < 0;
                SubcarpetaExisteEnDisco = true;
                SubcarpetaEstadoIcono = !SubcarpetaSintaxisValida ? "❌" : (string.IsNullOrWhiteSpace(Subcarpeta) ? "ℹ️" : "✅");
                SubcarpetaEstadoTexto = !SubcarpetaSintaxisValida ? "Caracteres inválidos en subcarpeta" : (string.IsNullOrWhiteSpace(Subcarpeta) ? "Subcarpeta por defecto" : "Subcarpeta definida");
            }

            // 3. Determinar Estado General
            if (!tieneNombre)
            {
                EsValidoParaImportar = false;
                EstadoIcono = "❌";
                EstadoTexto = "Falta nombre del subproyecto";
            }
            else if (!SubcarpetaSintaxisValida)
            {
                EsValidoParaImportar = false;
                EstadoIcono = "❌";
                EstadoTexto = SubcarpetaEstadoTexto;
            }
            else if (!MunicipioEsValido)
            {
                EsValidoParaImportar = false;
                EstadoIcono = "❌";
                EstadoTexto = MunicipioEstadoTexto;
            }
            else if (!TieneCoordenadasValidas && (!string.IsNullOrWhiteSpace(LatStr) || !string.IsNullOrWhiteSpace(LonStr)))
            {
                EsValidoParaImportar = true;
                EstadoIcono = "⚠️";
                EstadoTexto = "Coordenadas no válidas (se guardará sin ubicación)";
            }
            else if (!TieneCoordenadasValidas)
            {
                EsValidoParaImportar = true;
                EstadoIcono = "⚠️";
                EstadoTexto = "Sin coordenadas";
            }
            else if (!SubcarpetaExisteEnDisco)
            {
                EsValidoParaImportar = true;
                EstadoIcono = "⚠️";
                EstadoTexto = "Carpeta no encontrada físicamente en el proyecto (verifique la ruta)";
            }
            else
            {
                EsValidoParaImportar = true;
                EstadoIcono = "✅";
                EstadoTexto = "Listo";
            }

            OnPropertyChanged(nameof(MunicipioEsValido));
            OnPropertyChanged(nameof(MunicipioEstadoIcono));
            OnPropertyChanged(nameof(MunicipioEstadoTexto));
            OnPropertyChanged(nameof(SubcarpetaSintaxisValida));
            OnPropertyChanged(nameof(SubcarpetaExisteEnDisco));
            OnPropertyChanged(nameof(SubcarpetaEstadoIcono));
            OnPropertyChanged(nameof(SubcarpetaEstadoTexto));
            OnPropertyChanged(nameof(TieneCoordenadasValidas));
            OnPropertyChanged(nameof(EsValidoParaImportar));
            OnPropertyChanged(nameof(EstadoIcono));
            OnPropertyChanged(nameof(EstadoTexto));
            OnPropertyChanged(nameof(CoordenadasTexto));
        }

        public static bool TryParseCoordenada(string? input, out double valor)
        {
            valor = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var cleaned = input.Trim()
                .Replace("°", "")
                .Replace("'", "")
                .Replace("\"", "")
                .Trim();

            bool isNegative = cleaned.EndsWith("W", StringComparison.OrdinalIgnoreCase) ||
                              cleaned.EndsWith("O", StringComparison.OrdinalIgnoreCase) ||
                              cleaned.EndsWith("S", StringComparison.OrdinalIgnoreCase) ||
                              cleaned.StartsWith("-");

            cleaned = cleaned
                .Replace("N", "", StringComparison.OrdinalIgnoreCase)
                .Replace("S", "", StringComparison.OrdinalIgnoreCase)
                .Replace("E", "", StringComparison.OrdinalIgnoreCase)
                .Replace("W", "", StringComparison.OrdinalIgnoreCase)
                .Replace("O", "", StringComparison.OrdinalIgnoreCase)
                .Replace(',', '.')
                .Trim();

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                valor = isNegative && parsed > 0 ? -parsed : parsed;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// ViewModel para la ventana de revisión y mapeo de campos antes de aceptar la importación CSV/TXT.
    /// </summary>
    public class ImportarSubproyectosCsvViewModel : ObservableObject
    {
        private const string NINGUNA_COLUMNA = "(Ninguno)";

        private readonly int _idProyecto;
        private readonly IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? _municipiosDisponibles;
        private readonly IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? _todosMunicipios;

        private string _rutaArchivo = string.Empty;
        private string _delimitador = ";";
        private List<string[]> _filasRaw = new();
        private List<string> _encabezadosRaw = new();
        private bool _mostrarMapeoCsv = false;

        private string _columnaNombre = NINGUNA_COLUMNA;
        private string _columnaSubcarpeta = NINGUNA_COLUMNA;
        private string _columnaLatitud = NINGUNA_COLUMNA;
        private string _columnaLongitud = NINGUNA_COLUMNA;
        private string _columnaMunicipio = NINGUNA_COLUMNA;
        private string _columnaDescripcion = NINGUNA_COLUMNA;

        public string RutaArchivo
        {
            get => _rutaArchivo;
            set
            {
                if (SetProperty(ref _rutaArchivo, value))
                {
                    OnPropertyChanged(nameof(NombreArchivo));
                }
            }
        }

        public string NombreArchivo => string.IsNullOrWhiteSpace(RutaArchivo) ? string.Empty : Path.GetFileName(RutaArchivo);

        public bool MostrarMapeoCsv
        {
            get => _mostrarMapeoCsv;
            set => SetProperty(ref _mostrarMapeoCsv, value);
        }

        public string Delimitador
        {
            get => _delimitador;
            set
            {
                if (SetProperty(ref _delimitador, value))
                {
                    ReanalizarArchivo();
                }
            }
        }

        public ObservableCollection<string> DelimitadoresDisponibles { get; } = new() { ";", "," };

        public ObservableCollection<string> ColumnasDisponibles { get; } = new();

        public ObservableCollection<SubproyectoCsvPreviewItem> FilasPreview { get; } = new();

        public ObservableCollection<FormularioProyectoViewModel.MunicipioItem> MunicipiosSugeridos { get; } = new();

        public string ColumnaNombre
        {
            get => _columnaNombre;
            set
            {
                if (SetProperty(ref _columnaNombre, value))
                    ActualizarMapeoYFilas();
            }
        }

        public string ColumnaSubcarpeta
        {
            get => _columnaSubcarpeta;
            set
            {
                if (SetProperty(ref _columnaSubcarpeta, value))
                    ActualizarMapeoYFilas();
            }
        }

        public string ColumnaLatitud
        {
            get => _columnaLatitud;
            set
            {
                if (SetProperty(ref _columnaLatitud, value))
                    ActualizarMapeoYFilas();
            }
        }

        public string ColumnaLongitud
        {
            get => _columnaLongitud;
            set
            {
                if (SetProperty(ref _columnaLongitud, value))
                    ActualizarMapeoYFilas();
            }
        }

        public string ColumnaMunicipio
        {
            get => _columnaMunicipio;
            set
            {
                if (SetProperty(ref _columnaMunicipio, value))
                    ActualizarMapeoYFilas();
            }
        }

        public string ColumnaDescripcion
        {
            get => _columnaDescripcion;
            set
            {
                if (SetProperty(ref _columnaDescripcion, value))
                    ActualizarMapeoYFilas();
            }
        }

        public int TotalFilas => FilasPreview.Count;

        public int TotalValidas => FilasPreview.Count(f => f.Incluir && f.EsValidoParaImportar);

        public bool PuedeAceptar => TotalValidas > 0;

        public string ResumenImportacionTexto => TotalValidas == 1
            ? "1 subproyecto listo para guardar"
            : $"{TotalValidas} de {TotalFilas} subproyectos listos para guardar";

        public string TextoBotonAceptar => TotalValidas > 0
            ? $"💾 Guardar Cambios ({TotalValidas})"
            : "💾 Guardar Cambios";

        public IRelayCommand SeleccionarTodasCommand { get; }
        public IRelayCommand DeseleccionarTodasCommand { get; }
        public IRelayCommand SeleccionarCarpetaBaseCommand { get; }
        public IRelayCommand<SubproyectoCsvPreviewItem> SeleccionarCarpetaFilaCommand { get; }
        public IRelayCommand AgregarFilaCommand { get; }
        public IRelayCommand<SubproyectoCsvPreviewItem> EliminarFilaCommand { get; }
        public IRelayCommand CargarArchivoCsvCommand { get; }
        public IRelayCommand AlternarMapeoCsvCommand { get; }

        public Func<string?>? SelectorCarpetaDialogHandler { get; set; }
        public Func<string?>? SelectorArchivoCsvDialogHandler { get; set; }

        private string? _rutaProyecto;
        public string? RutaProyecto
        {
            get => _rutaProyecto;
            set => SetProperty(ref _rutaProyecto, value);
        }

        public ObservableCollection<string> SubcarpetasDisponibles { get; } = new();

        private string _subcarpetaPredeterminada = string.Empty;
        public string SubcarpetaPredeterminada
        {
            get => _subcarpetaPredeterminada;
            set
            {
                if (SetProperty(ref _subcarpetaPredeterminada, value))
                {
                    ActualizarSubcarpetasFilas();
                }
            }
        }

        private bool _crearSubcarpetaPorNombre = true;
        public bool CrearSubcarpetaPorNombre
        {
            get => _crearSubcarpetaPorNombre;
            set
            {
                if (SetProperty(ref _crearSubcarpetaPorNombre, value))
                {
                    ActualizarSubcarpetasFilas();
                }
            }
        }

        public ImportarSubproyectosCsvViewModel(
            int idProyecto,
            IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? municipiosDisponibles = null,
            string? rutaProyecto = null,
            IEnumerable<string>? subcarpetasDisponibles = null,
            IReadOnlyList<FormularioProyectoViewModel.MunicipioItem>? todosMunicipios = null)
        {
            _idProyecto = idProyecto;
            _municipiosDisponibles = municipiosDisponibles;
            _todosMunicipios = todosMunicipios;
            _rutaProyecto = rutaProyecto;

            if (subcarpetasDisponibles != null)
            {
                foreach (var sc in subcarpetasDisponibles)
                {
                    if (!string.IsNullOrWhiteSpace(sc) && !SubcarpetasDisponibles.Contains(sc))
                        SubcarpetasDisponibles.Add(sc);
                }
            }

            // Poblar sugerencias de municipios (primero los del proyecto, luego el catálogo general si existe)
            if (_municipiosDisponibles != null)
            {
                foreach (var m in _municipiosDisponibles)
                {
                    if (!MunicipiosSugeridos.Any(x => x.Codigo == m.Codigo))
                        MunicipiosSugeridos.Add(m);
                }
            }
            if (_todosMunicipios != null && MunicipiosSugeridos.Count == 0)
            {
                foreach (var m in _todosMunicipios)
                {
                    MunicipiosSugeridos.Add(m);
                }
            }

            SeleccionarTodasCommand = new RelayCommand(() => SetTodasFilas(true));
            DeseleccionarTodasCommand = new RelayCommand(() => SetTodasFilas(false));
            SeleccionarCarpetaBaseCommand = new RelayCommand(SeleccionarCarpetaBase);
            SeleccionarCarpetaFilaCommand = new RelayCommand<SubproyectoCsvPreviewItem>(SeleccionarCarpetaFila);
            AgregarFilaCommand = new RelayCommand(AgregarNuevaFila);
            EliminarFilaCommand = new RelayCommand<SubproyectoCsvPreviewItem>(EliminarFila);
            CargarArchivoCsvCommand = new RelayCommand(CargarArchivoConDialogo);
            AlternarMapeoCsvCommand = new RelayCommand(() => MostrarMapeoCsv = !MostrarMapeoCsv);
        }

        public (bool Valido, string? Codigo, string? NombreOficial, string? Mensaje) ValidarMunicipioTexto(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return (true, null, null, "Sin municipio territorial asignado (opcional)");
            }

            var trimmed = texto.Trim();

            // 1. Buscar en municipios del proyecto
            if (_municipiosDisponibles != null && _municipiosDisponibles.Count > 0)
            {
                var matchProy = BuscarCoincidenciaMunicipio(_municipiosDisponibles, trimmed);
                if (matchProy != null)
                {
                    return (true, matchProy.Codigo, matchProy.Nombre, $"Municipio asociado al proyecto: {matchProy.Nombre} (DANE {matchProy.Codigo})");
                }
            }

            // 2. Buscar en catálogo general DIVIPOLA si está disponible
            if (_todosMunicipios != null && _todosMunicipios.Count > 0)
            {
                var matchDivipola = BuscarCoincidenciaMunicipio(_todosMunicipios, trimmed);
                if (matchDivipola != null)
                {
                    return (true, matchDivipola.Codigo, matchDivipola.Nombre, $"Municipio DIVIPOLA reconocido: {matchDivipola.Nombre} (DANE {matchDivipola.Codigo})");
                }
            }

            // 3. Si no hay catálogo provisto (ej. tests sin dependencias de municipios), no bloquear
            if ((_municipiosDisponibles == null || _municipiosDisponibles.Count == 0) &&
                (_todosMunicipios == null || _todosMunicipios.Count == 0))
            {
                return (true, null, trimmed, $"Municipio ingresado: {trimmed}");
            }

            // 4. Si hay catálogo pero no coincidió con ningún municipio válido
            return (false, null, trimmed, $"El texto '{trimmed}' no coincide con ningún municipio válido en DIVIPOLA");
        }

        private static FormularioProyectoViewModel.MunicipioItem? BuscarCoincidenciaMunicipio(
            IEnumerable<FormularioProyectoViewModel.MunicipioItem> coleccion, string texto)
        {
            var porCod = coleccion.FirstOrDefault(m => string.Equals(m.Codigo, texto, StringComparison.OrdinalIgnoreCase));
            if (porCod != null) return porCod;

            var porNombre = coleccion.FirstOrDefault(m => string.Equals(m.Nombre, texto, StringComparison.OrdinalIgnoreCase));
            if (porNombre != null) return porNombre;

            string textoSlug = GenerarSlug(texto);
            var porSlug = coleccion.FirstOrDefault(m => GenerarSlug(m.Nombre) == textoSlug);
            if (porSlug != null) return porSlug;

            var porContiene = coleccion.FirstOrDefault(m =>
                m.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                texto.Contains(m.Nombre, StringComparison.OrdinalIgnoreCase));
            if (porContiene != null) return porContiene;

            return null;
        }

        public (bool SintaxisValida, bool ExisteEnDisco, string? Mensaje) ValidarSubcarpetaTexto(string? subcarpeta)
        {
            if (string.IsNullOrWhiteSpace(subcarpeta))
            {
                return (true, true, "Se generará automáticamente a partir del nombre del subproyecto.");
            }

            var trimmed = subcarpeta.Trim().Replace('\\', '/');
            char[] caracteresInvalidos = new[] { '*', '?', '"', '<', '>', '|', ':' };
            if (trimmed.IndexOfAny(caracteresInvalidos) >= 0)
            {
                return (false, false, "La subcarpeta contiene caracteres no permitidos (*, ?, \", <, >, |, :)");
            }

            string rel = trimmed.TrimStart('/');
            if (string.IsNullOrWhiteSpace(rel))
            {
                return (false, false, "Ruta de subcarpeta no válida.");
            }

            if (!string.IsNullOrWhiteSpace(RutaProyecto))
            {
                try
                {
                    string rutaFisica = Path.Combine(RutaProyecto, rel.Replace('/', Path.DirectorySeparatorChar));
                    bool existeFisicamente = Directory.Exists(rutaFisica);
                    bool existeEnLista = SubcarpetasDisponibles.Any(s => string.Equals(s.Replace('\\', '/'), rel, StringComparison.OrdinalIgnoreCase));

                    if (existeFisicamente || existeEnLista)
                    {
                        return (true, true, $"Carpeta verificada en disco: /{rel}");
                    }
                    else
                    {
                        return (true, false, $"La carpeta física no existe en: {rutaFisica}");
                    }
                }
                catch (Exception ex)
                {
                    return (false, false, $"Error evaluando ruta: {ex.Message}");
                }
            }

            return (true, true, $"Ruta relativa: /{rel}");
        }

        public void CargarSubproyectosExistentes(IEnumerable<SubproyectoItemFormulario> subproyectos)
        {
            FilasPreview.Clear();
            _filasRaw.Clear();
            _encabezadosRaw.Clear();
            ColumnasDisponibles.Clear();

            MostrarMapeoCsv = false;
            RutaArchivo = string.Empty;

            int num = 1;
            foreach (var sub in subproyectos)
            {
                var item = new SubproyectoCsvPreviewItem
                {
                    IdSubproyecto = sub.IdSubproyecto,
                    NumeroFila = num++,
                    Incluir = true,
                    Nombre = sub.Nombre,
                    Subcarpeta = sub.Subcarpeta,
                    LatStr = sub.Lat != 0 ? sub.Lat.ToString("F6", CultureInfo.InvariantCulture) : "",
                    LonStr = sub.Lon != 0 ? sub.Lon.ToString("F6", CultureInfo.InvariantCulture) : "",
                    Municipio = sub.MunicipioNombre ?? sub.MpioCdpmp ?? "",
                    MpioCodigo = sub.MpioCdpmp,
                    Descripcion = sub.Descripcion,
                    ValidadorMunicipio = ValidarMunicipioTexto,
                    ValidadorSubcarpeta = ValidarSubcarpetaTexto
                };

                item.PropertyChanged += Item_PropertyChanged;
                item.Validar();
                FilasPreview.Add(item);
            }

            NotificarTotales();
        }

        public void AgregarNuevaFila()
        {
            int nuevoNum = FilasPreview.Count + 1;
            string nombreDefecto = $"Sitio {nuevoNum}";
            string slug = GenerarSlug(nombreDefecto);
            string subcarpetaDefecto = !string.IsNullOrWhiteSpace(SubcarpetaPredeterminada)
                ? (CrearSubcarpetaPorNombre ? $"{SubcarpetaPredeterminada}/{slug}".Trim('/') : SubcarpetaPredeterminada)
                : slug;

            var primerMpio = MunicipiosSugeridos.FirstOrDefault();

            var item = new SubproyectoCsvPreviewItem
            {
                IdSubproyecto = 0,
                NumeroFila = nuevoNum,
                Incluir = true,
                Nombre = nombreDefecto,
                Subcarpeta = subcarpetaDefecto,
                Municipio = primerMpio?.Nombre ?? "",
                MpioCodigo = primerMpio?.Codigo,
                ValidadorMunicipio = ValidarMunicipioTexto,
                ValidadorSubcarpeta = ValidarSubcarpetaTexto
            };

            item.PropertyChanged += Item_PropertyChanged;
            item.Validar();
            FilasPreview.Add(item);
            NotificarTotales();
        }

        public void EliminarFila(SubproyectoCsvPreviewItem? item)
        {
            if (item == null) return;
            FilasPreview.Remove(item);
            for (int i = 0; i < FilasPreview.Count; i++)
            {
                FilasPreview[i].NumeroFila = i + 1;
            }
            NotificarTotales();
        }

        public void CargarArchivoConDialogo()
        {
            string? ruta = null;
            if (SelectorArchivoCsvDialogHandler != null)
            {
                ruta = SelectorArchivoCsvDialogHandler();
            }
            else
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Seleccionar archivo CSV o TXT de subproyectos",
                    Filter = "Archivos delimitados (*.csv;*.txt)|*.csv;*.txt|Todos los archivos (*.*)|*.*",
                    Multiselect = false
                };
                if (ofd.ShowDialog() == true)
                {
                    ruta = ofd.FileName;
                }
            }

            if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
            {
                MostrarMapeoCsv = true;
                CargarArchivo(ruta);
            }
        }

        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SubproyectoCsvPreviewItem.Incluir) ||
                e.PropertyName == nameof(SubproyectoCsvPreviewItem.EsValidoParaImportar))
            {
                NotificarTotales();
            }
        }

        private void ActualizarSubcarpetasFilas()
        {
            if (ColumnaSubcarpeta != NINGUNA_COLUMNA) return;

            foreach (var fila in FilasPreview)
            {
                string slug = GenerarSlug(fila.Nombre);
                if (string.IsNullOrWhiteSpace(slug)) slug = $"sitio_{fila.NumeroFila}";

                if (!string.IsNullOrWhiteSpace(SubcarpetaPredeterminada))
                {
                    fila.Subcarpeta = CrearSubcarpetaPorNombre
                        ? $"{SubcarpetaPredeterminada}/{slug}".Trim('/')
                        : SubcarpetaPredeterminada;
                }
                else
                {
                    fila.Subcarpeta = slug;
                }
            }
        }

        private void SeleccionarCarpetaBase()
        {
            try
            {
                string? seleccion = null;
                if (SelectorCarpetaDialogHandler != null)
                {
                    seleccion = SelectorCarpetaDialogHandler();
                }
                else
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "Seleccionar carpeta base del proyecto para los subproyectos",
                        Multiselect = false
                    };

                    if (!string.IsNullOrWhiteSpace(RutaProyecto) && Directory.Exists(RutaProyecto))
                    {
                        dialog.InitialDirectory = RutaProyecto;
                    }

                    if (dialog.ShowDialog() == true)
                    {
                        seleccion = dialog.FolderName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(seleccion))
                {
                    string subcarpetaRel;
                    if (!string.IsNullOrWhiteSpace(RutaProyecto) && seleccion.StartsWith(RutaProyecto, StringComparison.OrdinalIgnoreCase))
                    {
                        subcarpetaRel = Path.GetRelativePath(RutaProyecto, seleccion);
                        if (subcarpetaRel == ".") subcarpetaRel = "";
                    }
                    else
                    {
                        subcarpetaRel = Path.GetFileName(seleccion) ?? seleccion;
                    }

                    subcarpetaRel = subcarpetaRel.Trim().Replace('\\', '/').Trim('/');
                    SubcarpetaPredeterminada = subcarpetaRel;

                    if (!string.IsNullOrWhiteSpace(subcarpetaRel) && !SubcarpetasDisponibles.Contains(subcarpetaRel))
                    {
                        SubcarpetasDisponibles.Insert(0, subcarpetaRel);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportarSubproyectosCsvViewModel] Error seleccionando carpeta base: {ex.Message}");
            }
        }

        private void SeleccionarCarpetaFila(SubproyectoCsvPreviewItem? item)
        {
            if (item == null) return;
            try
            {
                string? seleccion = null;
                if (SelectorCarpetaDialogHandler != null)
                {
                    seleccion = SelectorCarpetaDialogHandler();
                }
                else
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = $"Seleccionar subcarpeta para '{item.Nombre}'",
                        Multiselect = false
                    };

                    if (!string.IsNullOrWhiteSpace(RutaProyecto) && Directory.Exists(RutaProyecto))
                    {
                        dialog.InitialDirectory = RutaProyecto;
                    }

                    if (dialog.ShowDialog() == true)
                    {
                        seleccion = dialog.FolderName;
                    }
                }

                if (!string.IsNullOrWhiteSpace(seleccion))
                {
                    string subcarpetaRel;
                    if (!string.IsNullOrWhiteSpace(RutaProyecto) && seleccion.StartsWith(RutaProyecto, StringComparison.OrdinalIgnoreCase))
                    {
                        subcarpetaRel = Path.GetRelativePath(RutaProyecto, seleccion);
                        if (subcarpetaRel == ".") subcarpetaRel = "";
                    }
                    else
                    {
                        subcarpetaRel = Path.GetFileName(seleccion) ?? seleccion;
                    }

                    item.Subcarpeta = subcarpetaRel.Trim().Replace('\\', '/').Trim('/');
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportarSubproyectosCsvViewModel] Error seleccionando carpeta de fila: {ex.Message}");
            }
        }

        private void SetTodasFilas(bool incluir)
        {
            foreach (var fila in FilasPreview)
            {
                fila.Incluir = incluir;
            }
            NotificarTotales();
        }

        public void NotificarTotales()
        {
            OnPropertyChanged(nameof(TotalFilas));
            OnPropertyChanged(nameof(TotalValidas));
            OnPropertyChanged(nameof(PuedeAceptar));
            OnPropertyChanged(nameof(ResumenImportacionTexto));
            OnPropertyChanged(nameof(TextoBotonAceptar));
        }

        public void CargarArchivo(string ruta)
        {
            RutaArchivo = ruta;
            MostrarMapeoCsv = true;
            if (!File.Exists(ruta)) return;

            // 1. Detección automática de delimitador (; o ,)
            _delimitador = DetectarDelimitador(ruta);
            OnPropertyChanged(nameof(Delimitador));

            ReanalizarArchivo();
        }

        public static string DetectarDelimitador(string ruta)
        {
            try
            {
                var lineas = File.ReadLines(ruta, Encoding.UTF8).Take(5).ToList();
                if (lineas.Count == 0) return ";";

                int puntosYComa = 0;
                int comas = 0;

                foreach (var linea in lineas)
                {
                    puntosYComa += linea.Count(c => c == ';');
                    comas += linea.Count(c => c == ',');
                }

                return puntosYComa >= comas ? ";" : ",";
            }
            catch
            {
                return ";";
            }
        }

        public void ReanalizarArchivo()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivo) || !File.Exists(RutaArchivo)) return;

            try
            {
                string contenido;
                try
                {
                    contenido = File.ReadAllText(RutaArchivo, Encoding.UTF8);
                    if (contenido.Contains('\uFFFD'))
                    {
                        contenido = File.ReadAllText(RutaArchivo, Encoding.Latin1);
                    }
                }
                catch
                {
                    contenido = File.ReadAllText(RutaArchivo, Encoding.Default);
                }

                // Limpiar Byte Order Mark (BOM) si existe
                contenido = contenido.TrimStart('\uFEFF');

                var lineas = contenido.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                if (lineas.Count == 0) return;

                char sep = _delimitador.Length > 0 ? _delimitador[0] : ';';

                var primeraFilaParsed = ParseCsvLine(lineas[0], sep);
                bool tieneEncabezados = true;

                // Heurística de detección de encabezados vs datos:
                // Si la primera o segunda columna es un número o coordenada válida (ej. "4,778882" o "-74,764139"),
                // significa que el archivo no tiene fila de encabezados y la primera línea ya contiene datos.
                if (primeraFilaParsed.Count > 0)
                {
                    bool c0EsCoord = SubproyectoCsvPreviewItem.TryParseCoordenada(primeraFilaParsed[0], out double val0) && val0 != 0;
                    bool c1EsCoord = primeraFilaParsed.Count > 1 && SubproyectoCsvPreviewItem.TryParseCoordenada(primeraFilaParsed[1], out double val1) && val1 != 0;
                    if (c0EsCoord || c1EsCoord)
                    {
                        tieneEncabezados = false;
                    }
                }

                if (tieneEncabezados)
                {
                    _encabezadosRaw = primeraFilaParsed;
                    _filasRaw = lineas.Skip(1)
                        .Select(l => ParseCsvLine(l, sep).ToArray())
                        .Where(arr => arr.Length > 0 && arr.Any(val => !string.IsNullOrWhiteSpace(val)))
                        .ToList();
                }
                else
                {
                    _encabezadosRaw = Enumerable.Range(1, primeraFilaParsed.Count)
                        .Select(i => $"Columna {i}")
                        .ToList();
                    _filasRaw = lineas
                        .Select(l => ParseCsvLine(l, sep).ToArray())
                        .Where(arr => arr.Length > 0 && arr.Any(val => !string.IsNullOrWhiteSpace(val)))
                        .ToList();
                }

                // Poblar opciones de mapeo
                ColumnasDisponibles.Clear();
                ColumnasDisponibles.Add(NINGUNA_COLUMNA);
                foreach (var h in _encabezadosRaw)
                {
                    ColumnasDisponibles.Add(h);
                }

                // Mapeo automático inteligente por similitud de nombres de columna
                AutoMapearColumnas();

                // Construir vista previa
                ActualizarMapeoYFilas();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImportarSubproyectosCsvViewModel] Error analizando CSV: {ex.Message}");
            }
        }

        private void AutoMapearColumnas()
        {
            _columnaNombre = BuscarMejorColumna(_encabezadosRaw, "nombre", "subproyecto", "frente", "sitio", "titulo", "name", "tramo", "obra") ?? NINGUNA_COLUMNA;
            _columnaSubcarpeta = BuscarMejorColumna(_encabezadosRaw, "subcarpeta", "carpeta", "folder", "ruta", "path", "directorio") ?? NINGUNA_COLUMNA;
            _columnaLatitud = BuscarMejorColumna(_encabezadosRaw, "latitud", "lat", "coord_y", "y", "norte", "latitude") ?? NINGUNA_COLUMNA;
            _columnaLongitud = BuscarMejorColumna(_encabezadosRaw, "longitud", "lon", "lng", "coord_x", "x", "este", "longitude") ?? NINGUNA_COLUMNA;
            _columnaMunicipio = BuscarMejorColumna(_encabezadosRaw, "municipio", "mpio", "ciudad", "cdpmp", "mpio_cdpmp", "dane") ?? NINGUNA_COLUMNA;
            _columnaDescripcion = BuscarMejorColumna(_encabezadosRaw, "descripcion", "descripción", "detalle", "observaciones", "obs", "description", "notas") ?? NINGUNA_COLUMNA;

            // Heurística de fallback cuando el archivo NO tiene encabezados o no coincidieron
            if (_filasRaw.Count > 0)
            {
                var primeraFila = _filasRaw[0];

                if (_columnaLatitud == NINGUNA_COLUMNA)
                {
                    for (int i = 0; i < primeraFila.Length && i < _encabezadosRaw.Count; i++)
                    {
                        if (SubproyectoCsvPreviewItem.TryParseCoordenada(primeraFila[i], out double val) &&
                            val >= -90 && val <= 90 && Math.Abs(val) > 0.001)
                        {
                            if (val > -60 && val < 60)
                            {
                                _columnaLatitud = _encabezadosRaw[i];
                                break;
                            }
                        }
                    }
                }

                if (_columnaLongitud == NINGUNA_COLUMNA)
                {
                    for (int i = 0; i < primeraFila.Length && i < _encabezadosRaw.Count; i++)
                    {
                        if (_encabezadosRaw[i] == _columnaLatitud) continue;

                        if (SubproyectoCsvPreviewItem.TryParseCoordenada(primeraFila[i], out double val) &&
                            val >= -180 && val <= 180 && Math.Abs(val) > 0.001)
                        {
                            _columnaLongitud = _encabezadosRaw[i];
                            break;
                        }
                    }
                }

                if (_columnaMunicipio == NINGUNA_COLUMNA)
                {
                    for (int i = 0; i < primeraFila.Length && i < _encabezadosRaw.Count; i++)
                    {
                        if (_encabezadosRaw[i] == _columnaLatitud || _encabezadosRaw[i] == _columnaLongitud) continue;

                        string texto = primeraFila[i].Trim();
                        if (!string.IsNullOrWhiteSpace(texto) && _municipiosDisponibles != null &&
                            _municipiosDisponibles.Any(m => string.Equals(m.Nombre, texto, StringComparison.OrdinalIgnoreCase) ||
                                                           m.Nombre.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            _columnaMunicipio = _encabezadosRaw[i];
                            break;
                        }
                    }
                }

                if (_columnaNombre == NINGUNA_COLUMNA)
                {
                    if (_columnaMunicipio != NINGUNA_COLUMNA)
                    {
                        _columnaNombre = _columnaMunicipio;
                    }
                    else
                    {
                        for (int i = 0; i < primeraFila.Length && i < _encabezadosRaw.Count; i++)
                        {
                            if (_encabezadosRaw[i] == _columnaLatitud || _encabezadosRaw[i] == _columnaLongitud) continue;
                            if (!string.IsNullOrWhiteSpace(primeraFila[i]))
                            {
                                _columnaNombre = _encabezadosRaw[i];
                                break;
                            }
                        }
                    }
                }
            }

            OnPropertyChanged(nameof(ColumnaNombre));
            OnPropertyChanged(nameof(ColumnaSubcarpeta));
            OnPropertyChanged(nameof(ColumnaLatitud));
            OnPropertyChanged(nameof(ColumnaLongitud));
            OnPropertyChanged(nameof(ColumnaMunicipio));
            OnPropertyChanged(nameof(ColumnaDescripcion));
        }

        private static string? BuscarMejorColumna(IEnumerable<string> encabezados, params string[] terminos)
        {
            foreach (var t in terminos)
            {
                var exacto = encabezados.FirstOrDefault(h => string.Equals(h.Trim(), t, StringComparison.OrdinalIgnoreCase));
                if (exacto != null) return exacto;
            }

            foreach (var t in terminos)
            {
                var parcial = encabezados.FirstOrDefault(h => h.Trim().IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (parcial != null) return parcial;
            }

            return null;
        }

        private void ActualizarMapeoYFilas()
        {
            FilasPreview.Clear();

            int idxNombre = _encabezadosRaw.IndexOf(ColumnaNombre);
            int idxSubcarpeta = _encabezadosRaw.IndexOf(ColumnaSubcarpeta);
            int idxLat = _encabezadosRaw.IndexOf(ColumnaLatitud);
            int idxLon = _encabezadosRaw.IndexOf(ColumnaLongitud);
            int idxMpio = _encabezadosRaw.IndexOf(ColumnaMunicipio);
            int idxDesc = _encabezadosRaw.IndexOf(ColumnaDescripcion);

            for (int i = 0; i < _filasRaw.Count; i++)
            {
                var arr = _filasRaw[i];
                string nombre = ObtenerValor(arr, idxNombre);
                string subcarpeta = ObtenerValor(arr, idxSubcarpeta);
                string latStr = ObtenerValor(arr, idxLat);
                string lonStr = ObtenerValor(arr, idxLon);
                string mpioStr = ObtenerValor(arr, idxMpio);
                string descStr = ObtenerValor(arr, idxDesc);

                // Si no se asignó columna de nombre (idxNombre < 0), usar Municipio como nombre por defecto, o "Sitio {i + 1}"
                if (idxNombre < 0 && string.IsNullOrWhiteSpace(nombre))
                {
                    if (!string.IsNullOrWhiteSpace(mpioStr))
                        nombre = mpioStr;
                    else
                        nombre = $"Sitio {i + 1}";
                }

                // Si no se asignó subcarpeta o está vacía, sugerir a partir de la carpeta base y el nombre
                if (string.IsNullOrWhiteSpace(subcarpeta))
                {
                    string slug = GenerarSlug(nombre);
                    if (string.IsNullOrWhiteSpace(slug)) slug = $"sitio_{i + 1}";

                    if (!string.IsNullOrWhiteSpace(SubcarpetaPredeterminada))
                    {
                        subcarpeta = CrearSubcarpetaPorNombre
                            ? $"{SubcarpetaPredeterminada}/{slug}".Trim('/')
                            : SubcarpetaPredeterminada;
                    }
                    else
                    {
                        subcarpeta = slug;
                    }
                }

                // Resolver código de municipio si está disponible en el catálogo
                string? mpioCodigo = null;
                string mpioNombre = mpioStr;

                if (!string.IsNullOrWhiteSpace(mpioStr) && _municipiosDisponibles != null)
                {
                    var match = _municipiosDisponibles.FirstOrDefault(m =>
                        string.Equals(m.Codigo, mpioStr.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        m.Nombre.IndexOf(mpioStr.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

                    if (match != null)
                    {
                        mpioCodigo = match.Codigo;
                        mpioNombre = match.Nombre;
                    }
                }

                var item = new SubproyectoCsvPreviewItem
                {
                    NumeroFila = i + 1,
                    Incluir = true,
                    Nombre = nombre,
                    Subcarpeta = subcarpeta,
                    LatStr = latStr,
                    LonStr = lonStr,
                    Municipio = mpioNombre,
                    MpioCodigo = mpioCodigo,
                    Descripcion = string.IsNullOrWhiteSpace(descStr) ? null : descStr,
                    ValidadorMunicipio = ValidarMunicipioTexto,
                    ValidadorSubcarpeta = ValidarSubcarpetaTexto
                };

                item.PropertyChanged += Item_PropertyChanged;
                item.Validar();
                FilasPreview.Add(item);
            }

            NotificarTotales();
        }

        private static string ObtenerValor(string[] arr, int idx)
        {
            if (idx >= 0 && idx < arr.Length)
            {
                return arr[idx]?.Trim() ?? string.Empty;
            }
            return string.Empty;
        }

        public static string GenerarSlug(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            var normalizado = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalizado)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        sb.Append(char.ToLowerInvariant(c));
                    }
                    else if (c == ' ' || c == '-' || c == '_')
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                        {
                            sb.Append('_');
                        }
                    }
                }
            }

            return sb.ToString().Trim('_');
        }

        public static List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            if (line == null) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString().Trim());
            return result;
        }

        /// <summary>
        /// Genera los items de subproyecto listos para ser incorporados al formulario de proyecto.
        /// </summary>
        public IReadOnlyList<SubproyectoItemFormulario> ObtenerSubproyectosAprobados()
        {
            var items = new List<SubproyectoItemFormulario>();

            foreach (var fila in FilasPreview.Where(f => f.Incluir && f.EsValidoParaImportar))
            {
                string nombreFinal = string.IsNullOrWhiteSpace(fila.Nombre) ? $"Sitio {fila.NumeroFila}" : fila.Nombre.Trim();
                string subcarpetaFinal = string.IsNullOrWhiteSpace(fila.Subcarpeta) ? GenerarSlug(nombreFinal) : fila.Subcarpeta.Trim();
                if (string.IsNullOrWhiteSpace(subcarpetaFinal)) subcarpetaFinal = $"sitio_{fila.NumeroFila}";

                items.Add(new SubproyectoItemFormulario
                {
                    IdSubproyecto = fila.IdSubproyecto,
                    IdProyecto = _idProyecto,
                    Nombre = nombreFinal,
                    Subcarpeta = subcarpetaFinal,
                    Lon = fila.Lon,
                    Lat = fila.Lat,
                    MpioCdpmp = fila.MpioCodigo,
                    MunicipioNombre = string.IsNullOrWhiteSpace(fila.Municipio) ? null : fila.Municipio.Trim(),
                    Descripcion = string.IsNullOrWhiteSpace(fila.Descripcion) ? null : fila.Descripcion.Trim(),
                    EsModificado = true
                });
            }

            return items;
        }
    }
}
