using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    /// <summary>
    /// Índice compacto en memoria de todos los archivos de un proyecto.
    /// Se construye una sola vez mediante un recorrido paralelo del árbol de carpetas
    /// utilizando <see cref="FileSystemEnumerable{TResult}"/> (sin instanciar <see cref="FileInfo"/>)
    /// y sin descender dentro de geodatabases (<c>*.gdb</c>), repositorios (<c>.git</c>) ni carpetas de compilación.
    /// </summary>
    public sealed class IndiceArchivosProyecto
    {
        public readonly record struct EntradaArchivo(
            string Nombre,
            string UbicacionRelativa,
            string RutaRelativaVirtual,
            string Extension,
            long TamanoBytes,
            DateTime FechaModificacion)
        {
            public ArchivoVirtual ToArchivoVirtual() => new()
            {
                Nombre = Nombre,
                UbicacionRelativa = UbicacionRelativa,
                RutaRelativaVirtual = RutaRelativaVirtual,
                Extension = Extension,
                TamanoBytes = TamanoBytes,
                FechaModificacion = FechaModificacion
            };
        }

        private readonly Dictionary<string, List<EntradaArchivo>> _porExtension;
        private readonly List<EntradaArchivo> _todas;

        public string RutaRaiz { get; }
        public int TotalArchivos => _todas.Count;
        public int TotalCarpetas { get; }
        public TimeSpan TiempoConstruccion { get; }
        public IReadOnlyDictionary<string, List<EntradaArchivo>> PorExtension => _porExtension;
        public IReadOnlyList<EntradaArchivo> Todas => _todas;

        private IndiceArchivosProyecto(
            string rutaRaiz,
            List<EntradaArchivo> todas,
            Dictionary<string, List<EntradaArchivo>> porExtension,
            int totalCarpetas,
            TimeSpan tiempoConstruccion)
        {
            RutaRaiz = rutaRaiz;
            _todas = todas;
            _porExtension = porExtension;
            TotalCarpetas = totalCarpetas;
            TiempoConstruccion = tiempoConstruccion;
        }

        private static bool EsCarpetaOmitida(ReadOnlySpan<char> nombre)
        {
            if (nombre.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                nombre.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                nombre.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static bool EsArchivoSondaOTemporal(ReadOnlySpan<char> nombre)
        {
            return nombre.StartsWith(".probe_", StringComparison.OrdinalIgnoreCase) ||
                   nombre.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Construye el índice recorriendo las subcarpetas con paralelismo acotado (ideal para ocultar latencia SMB).
        /// Las carpetas <c>*.gdb</c> se registran como un único dataset de extensión <c>.gdb</c> y no se recorren por dentro.
        /// </summary>
        public static IndiceArchivosProyecto Construir(
            string rutaRaiz,
            int gradoParalelismo = 8,
            IProgress<ProgresoEscaneoFormatos>? progresoFormatos = null,
            IProgress<int>? progresoConteo = null,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(rutaRaiz) || !Directory.Exists(rutaRaiz))
            {
                progresoFormatos?.Report(new ProgresoEscaneoFormatos(0, 0, Array.Empty<FormatoExtensionItem>(), Array.Empty<CategoriaFormatoItem>()));
                progresoConteo?.Report(0);
                return new IndiceArchivosProyecto(
                    rutaRaiz ?? string.Empty,
                    new List<EntradaArchivo>(),
                    new Dictionary<string, List<EntradaArchivo>>(StringComparer.OrdinalIgnoreCase),
                    0,
                    sw.Elapsed);
            }

            string raizNormalizada = Path.GetFullPath(rutaRaiz)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            int paralelismo = Math.Clamp(gradoParalelismo, 1, 32);
            var bolsasArchivos = new ConcurrentBag<List<EntradaArchivo>>();
            var colaCarpetas = new ConcurrentQueue<string>();
            colaCarpetas.Enqueue(raizNormalizada);

            int carpetasEnVuelo = 1;
            int totalCarpetas = 0;
            int totalArchivos = 0;
            var extensionesAcumuladas = new ConcurrentDictionary<string, (int Conteo, long Tamano)>(StringComparer.OrdinalIgnoreCase);
            long ultimoReporteTicks = Stopwatch.GetTimestamp();
            int reportando = 0;

            void NotificarProgresoSiAplica(int archivosActuales, bool forzar = false)
            {
                if (progresoFormatos == null) return;

                long ahora = Stopwatch.GetTimestamp();
                if (forzar || (ahora - Interlocked.Read(ref ultimoReporteTicks)) >= (Stopwatch.Frequency / 5))
                {
                    if (Interlocked.CompareExchange(ref reportando, 1, 0) == 0)
                    {
                        try
                        {
                            Interlocked.Exchange(ref ultimoReporteTicks, ahora);
                            var snap = new Dictionary<string, (int Conteo, long Tamano)>(extensionesAcumuladas, StringComparer.OrdinalIgnoreCase);
                            int carpetas = Volatile.Read(ref totalCarpetas);
                            var parcial = ConstruirResumenFormatos(archivosActuales, snap);
                            progresoFormatos.Report(new ProgresoEscaneoFormatos(
                                archivosActuales,
                                carpetas,
                                parcial.ExtensionesLista,
                                parcial.Categorias));
                        }
                        finally
                        {
                            Interlocked.Exchange(ref reportando, 0);
                        }
                    }
                }
            }

            using var finalizado = new ManualResetEventSlim(false);

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
            };

            void ProcesarCarpeta(string dirActual)
            {
                var locales = new List<EntradaArchivo>();
                var subcarpetas = new List<string>();

                string ubicacionRelativa;
                if (dirActual.Length <= raizNormalizada.Length)
                {
                    ubicacionRelativa = "(raíz)";
                }
                else
                {
                    ubicacionRelativa = dirActual
                        .Substring(raizNormalizada.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(ubicacionRelativa))
                        ubicacionRelativa = "(raíz)";
                }

                try
                {
                    var enumerator = new FileSystemEnumerable<byte>(
                        dirActual,
                        (ref FileSystemEntry entry) =>
                        {
                            ReadOnlySpan<char> spanNombre = entry.FileName;
                            if (entry.IsDirectory)
                            {
                                if (EsCarpetaOmitida(spanNombre))
                                    return 0;

                                string nombreDir = spanNombre.ToString();
                                string rutaSub = Path.Combine(dirActual, nombreDir);

                                // Las File Geodatabases (*.gdb) se indexan como un único dataset .gdb y no se desciende a sus miles de .gdbtable
                                if (spanNombre.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                                {
                                    string rutaRelVirtual = ubicacionRelativa == "(raíz)"
                                        ? nombreDir
                                        : $"{ubicacionRelativa}/{nombreDir}";

                                    long tamanoGdb = 0;
                                    try
                                    {
                                        tamanoGdb = new DirectoryInfo(rutaSub).EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                                    }
                                    catch { }

                                    locales.Add(new EntradaArchivo(
                                        Nombre: nombreDir,
                                        UbicacionRelativa: ubicacionRelativa,
                                        RutaRelativaVirtual: rutaRelVirtual,
                                        Extension: ".gdb",
                                        TamanoBytes: tamanoGdb,
                                        FechaModificacion: entry.LastWriteTimeUtc.LocalDateTime));
                                }
                                else
                                {
                                    subcarpetas.Add(rutaSub);
                                }
                            }
                            else
                            {
                                if (EsArchivoSondaOTemporal(spanNombre))
                                    return 0;

                                string nombreArchivo = spanNombre.ToString();
                                string ext = Path.GetExtension(nombreArchivo);
                                string extNorm = string.IsNullOrEmpty(ext) ? string.Empty : ext.ToLowerInvariant();
                                string rutaRelVirtual = ubicacionRelativa == "(raíz)"
                                    ? nombreArchivo
                                    : $"{ubicacionRelativa}/{nombreArchivo}";

                                locales.Add(new EntradaArchivo(
                                    Nombre: nombreArchivo,
                                    UbicacionRelativa: ubicacionRelativa,
                                    RutaRelativaVirtual: rutaRelVirtual,
                                    Extension: extNorm,
                                    TamanoBytes: entry.Length,
                                    FechaModificacion: entry.LastWriteTimeUtc.LocalDateTime));
                            }
                            return 0;
                        },
                        enumOptions);

                    foreach (var _ in enumerator)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                }
                catch
                {
                    // Carpetas inaccesibles o errores de red puntuales se omiten sin abortar el índice
                }

                Interlocked.Increment(ref totalCarpetas);

                if (locales.Count > 0)
                {
                    bolsasArchivos.Add(locales);
                    int acumulado = Interlocked.Add(ref totalArchivos, locales.Count);

                    for (int i = 0; i < locales.Count; i++)
                    {
                        var e = locales[i];
                        if (!string.IsNullOrEmpty(e.Extension))
                        {
                            extensionesAcumuladas.AddOrUpdate(
                                e.Extension,
                                (1, e.TamanoBytes),
                                (_, prev) => (prev.Conteo + 1, prev.Tamano + e.TamanoBytes));
                        }
                    }

                    progresoConteo?.Report(acumulado);
                    NotificarProgresoSiAplica(acumulado);
                }

                if (!ct.IsCancellationRequested && subcarpetas.Count > 0)
                {
                    Interlocked.Add(ref carpetasEnVuelo, subcarpetas.Count);
                    foreach (var sub in subcarpetas)
                    {
                        colaCarpetas.Enqueue(sub);
                    }
                }

                if (Interlocked.Decrement(ref carpetasEnVuelo) == 0)
                {
                    finalizado.Set();
                }
            }

            if (paralelismo <= 1)
            {
                while (!ct.IsCancellationRequested && colaCarpetas.TryDequeue(out var dir))
                {
                    ProcesarCarpeta(dir);
                }
            }
            else
            {
                var tareas = new Task[paralelismo];
                for (int i = 0; i < paralelismo; i++)
                {
                    tareas[i] = Task.Run(() =>
                    {
                        var spin = new SpinWait();
                        while (!ct.IsCancellationRequested && !finalizado.IsSet)
                        {
                            if (colaCarpetas.TryDequeue(out var dir))
                            {
                                ProcesarCarpeta(dir);
                                spin.Reset();
                            }
                            else
                            {
                                spin.SpinOnce();
                            }
                        }
                    });
                }

                Task.WaitAll(tareas);
            }

            ct.ThrowIfCancellationRequested();

            var todas = new List<EntradaArchivo>(totalArchivos);
            var porExtension = new Dictionary<string, List<EntradaArchivo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var lote in bolsasArchivos)
            {
                foreach (var entrada in lote)
                {
                    todas.Add(entrada);
                    if (!string.IsNullOrEmpty(entrada.Extension))
                    {
                        if (!porExtension.TryGetValue(entrada.Extension, out var listaExt))
                        {
                            listaExt = new List<EntradaArchivo>();
                            porExtension[entrada.Extension] = listaExt;
                        }
                        listaExt.Add(entrada);
                    }
                }
            }

            // Notificación final al 100%
            NotificarProgresoSiAplica(todas.Count, forzar: true);

            sw.Stop();
            return new IndiceArchivosProyecto(raizNormalizada, todas, porExtension, totalCarpetas, sw.Elapsed);
        }

        public static IndiceArchivosProyecto Construir(
            string rutaRaiz,
            int gradoParalelismo,
            IProgress<int> progresoConteo,
            CancellationToken ct = default)
            => Construir(rutaRaiz, gradoParalelismo, null, progresoConteo, ct);

        private static HashSet<string> NormalizarExtensiones(IEnumerable<string>? extensiones)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (extensiones == null) return set;

            foreach (var ext in extensiones)
            {
                if (string.IsNullOrWhiteSpace(ext)) continue;
                string limpia = ext.Trim();
                if (!limpia.StartsWith('.')) limpia = "." + limpia;
                set.Add(limpia.ToLowerInvariant());
            }
            return set;
        }

        private static bool CoincideTexto(in EntradaArchivo entrada, string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return true;
            return entrada.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                   entrada.RutaRelativaVirtual.Contains(texto, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cuenta el total de archivos del índice que cumplen con las extensiones y el filtro de texto opcional.
        /// </summary>
        public int ContarCoincidencias(IEnumerable<string> extensiones, string? texto = null)
        {
            var extSet = NormalizarExtensiones(extensiones);
            if (extSet.Count == 0) return 0;

            bool sinTexto = string.IsNullOrWhiteSpace(texto);
            int total = 0;

            foreach (var ext in extSet)
            {
                if (_porExtension.TryGetValue(ext, out var lista))
                {
                    if (sinTexto)
                    {
                        total += lista.Count;
                    }
                    else
                    {
                        for (int i = 0; i < lista.Count; i++)
                        {
                            if (CoincideTexto(lista[i], texto))
                                total++;
                        }
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Filtra en memoria los archivos que coinciden con una o varias extensiones y el texto opcional,
        /// devolviendo como máximo <paramref name="maxResultados"/> elementos.
        /// </summary>
        public List<ArchivoVirtual> Filtrar(
            IEnumerable<string> extensiones,
            string? texto = null,
            int maxResultados = int.MaxValue)
        {
            var extSet = NormalizarExtensiones(extensiones);
            if (extSet.Count == 0 || maxResultados <= 0)
                return new List<ArchivoVirtual>();

            bool sinTexto = string.IsNullOrWhiteSpace(texto);
            var resultado = new List<ArchivoVirtual>();

            foreach (var ext in extSet)
            {
                if (!_porExtension.TryGetValue(ext, out var lista))
                    continue;

                for (int i = 0; i < lista.Count; i++)
                {
                    var entrada = lista[i];
                    if (sinTexto || CoincideTexto(in entrada, texto))
                    {
                        resultado.Add(entrada.ToArchivoVirtual());
                        if (resultado.Count >= maxResultados)
                            return resultado;
                    }
                }
            }

            return resultado;
        }

        private static readonly (string Nombre, string Icono, string Bg, string Border, string Fg, HashSet<string> Exts)[] _categoriasConfig = new[]
        {
            (
                Nombre: "Vectorial",
                Icono: "🗺️",
                Bg: "#E8F4EC",
                Border: "#B2DFBF",
                Fg: "#1A5C34",
                Exts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".shp", ".gpkg", ".geojson", ".kml", ".kmz", ".tab", ".mif", ".gml", ".gdb", ".geodatabase" }
            ),
            (
                Nombre: "Raster / Ortofoto",
                Icono: "🛰️",
                Bg: "#E3F2FD",
                Border: "#BBDEFB",
                Fg: "#1565C0",
                Exts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tif", ".tiff", ".img", ".dem", ".asc", ".dt2", ".ecw", ".jp2", ".sid" }
            ),
            (
                Nombre: "LiDAR / Nubes",
                Icono: "☁️",
                Bg: "#F3E5F5",
                Border: "#E1BEE7",
                Fg: "#6A1B9A",
                Exts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".las", ".laz", ".xyz", ".pts", ".ply" }
            ),
            (
                Nombre: "CAD / Planos",
                Icono: "📐",
                Bg: "#FFF8E1",
                Border: "#FFE082",
                Fg: "#B78103",
                Exts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dwg", ".dxf", ".dgn" }
            ),
            (
                Nombre: "Documentación",
                Icono: "📄",
                Bg: "#F5F5F5",
                Border: "#E0E0E0",
                Fg: "#424242",
                Exts: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".csv", ".txt", ".md" }
            )
        };

        private static readonly HashSet<string> _sidecarsConfig = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dbf", ".shx", ".prj", ".cpg", ".sbn", ".sbx", ".tfw", ".ovr", ".aux.xml", ".rrd", ".aux", ".j2w", ".wld", ".qmd", ".xml"
        };

        /// <summary>
        /// Genera el resumen consolidado de formatos espaciales y documentales directamente desde el índice en memoria en O(1),
        /// sin necesidad de volver a recorrer el disco físico o el servidor de red.
        /// </summary>
        public static ResumenFormatosArchivos ConstruirResumenFormatos(
            int totalArchivos,
            IReadOnlyDictionary<string, (int Conteo, long Tamano)> extensionesConteo)
        {
            if (totalArchivos == 0 || extensionesConteo.Count == 0)
            {
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());
            }

            long totalTamano = 0;
            foreach (var kvp in extensionesConteo)
            {
                totalTamano += kvp.Value.Tamano;
            }

            var categoriasResultado = new List<CategoriaFormatoItem>();
            foreach (var cat in _categoriasConfig)
            {
                int sumaArchivos = 0;
                var extsEncontradas = new List<string>();

                foreach (var kvp in extensionesConteo)
                {
                    if (cat.Exts.Contains(kvp.Key))
                    {
                        sumaArchivos += kvp.Value.Conteo;
                        extsEncontradas.Add($"{kvp.Key.ToUpperInvariant()} ({kvp.Value.Conteo})");
                    }
                }

                if (sumaArchivos > 0)
                {
                    categoriasResultado.Add(new CategoriaFormatoItem(
                        Categoria: cat.Nombre,
                        Icono: cat.Icono,
                        BadgeBgHex: cat.Bg,
                        BadgeBorderHex: cat.Border,
                        BadgeFgHex: cat.Fg,
                        CantidadArchivos: sumaArchivos,
                        ExtensionesEncontradas: extsEncontradas
                    ));
                }
            }

            var extensionesResultado = new List<FormatoExtensionItem>();
            foreach (var kvp in extensionesConteo)
            {
                string ext = kvp.Key;
                int conteo = kvp.Value.Conteo;
                long tamano = kvp.Value.Tamano;

                bool esSidecar = _sidecarsConfig.Contains(ext);
                var catEncontrada = _categoriasConfig.FirstOrDefault(c => c.Exts.Contains(ext));

                string categoriaNombre = catEncontrada.Nombre != null ? catEncontrada.Nombre : (esSidecar ? "Auxiliar / Sidecar" : "Otros");
                string icono = catEncontrada.Icono != null ? catEncontrada.Icono : (esSidecar ? "📎" : "📁");
                string bg = catEncontrada.Bg != null ? catEncontrada.Bg : (esSidecar ? "#ECEFF1" : "#FAFAFA");
                string border = catEncontrada.Border != null ? catEncontrada.Border : (esSidecar ? "#CFD8DC" : "#E0E0E0");
                string fg = catEncontrada.Fg != null ? catEncontrada.Fg : (esSidecar ? "#546E7A" : "#616161");

                extensionesResultado.Add(new FormatoExtensionItem(
                    Extension: ext,
                    Categoria: categoriaNombre,
                    Icono: icono,
                    BadgeBgHex: bg,
                    BadgeBorderHex: border,
                    BadgeFgHex: fg,
                    CantidadArchivos: conteo,
                    TamanoTotalBytes: tamano,
                    EsAuxiliar: esSidecar
                ));
            }

            var extensionesOrdenadas = extensionesResultado
                .OrderBy(e => e.EsAuxiliar)
                .ThenByDescending(e => e.CantidadArchivos)
                .ThenBy(e => e.Extension)
                .ToList();

            return new ResumenFormatosArchivos(totalArchivos, totalTamano, categoriasResultado, extensionesOrdenadas);
        }

        /// <summary>
        /// Genera el resumen consolidado de formatos espaciales y documentales directamente desde el índice en memoria en O(1),
        /// sin necesidad de volver a recorrer el disco físico o el servidor de red.
        /// </summary>
        public ResumenFormatosArchivos GenerarResumenFormatos()
        {
            if (_todas.Count == 0)
            {
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());
            }

            var extensionesConteo = new Dictionary<string, (int Conteo, long Tamano)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _porExtension)
            {
                long tamano = 0;
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    tamano += kvp.Value[i].TamanoBytes;
                }
                extensionesConteo[kvp.Key] = (kvp.Value.Count, tamano);
            }

            return ConstruirResumenFormatos(TotalArchivos, extensionesConteo);
        }
    }
}
