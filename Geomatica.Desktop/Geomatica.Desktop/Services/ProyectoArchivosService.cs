using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    public enum EstadoPermisoCarpeta
    {
        SinRuta,
        RutaNoEncontrada,
        AccesoRestringido,
        SoloLectura,
        LecturaEscritura
    }

    public record EvaluacionPermisos(
        EstadoPermisoCarpeta Estado,
        bool PuedeLeer,
        bool PuedeEscribir,
        string Mensaje,
        string BadgeTexto,
        string BadgeBackgroundHex,
        string BadgeBorderHex,
        string BadgeForegroundHex);

    public record CategoriaFormatoItem(
        string Categoria,
        string Icono,
        string BadgeBgHex,
        string BadgeBorderHex,
        string BadgeFgHex,
        int CantidadArchivos,
        IReadOnlyList<string> ExtensionesEncontradas)
    {
        public string TextoBadge => $"{Icono} {Categoria} ({CantidadArchivos})";
        public string TooltipTexto => $"{Categoria}: {CantidadArchivos} archivo(s) [{string.Join(", ", ExtensionesEncontradas)}]";
    }

    public record FormatoExtensionItem(
        string Extension,
        string Categoria,
        string Icono,
        string BadgeBgHex,
        string BadgeBorderHex,
        string BadgeFgHex,
        int CantidadArchivos,
        long TamanoTotalBytes,
        bool EsAuxiliar = false)
    {
        public string ExtensionMayus => Extension.TrimStart('.').ToUpperInvariant();
        public string TextoBadge => $"{Icono} .{ExtensionMayus} ({CantidadArchivos})";
        public string TamanoTexto => TamanoTotalBytes switch
        {
            < 1024 => $"{TamanoTotalBytes} B",
            < 1024 * 1024 => $"{TamanoTotalBytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{TamanoTotalBytes / (1024.0 * 1024):F1} MB",
            _ => $"{TamanoTotalBytes / (1024.0 * 1024 * 1024):F2} GB"
        };
        public string TooltipTexto => $"{Icono} Archivos .{ExtensionMayus} ({Categoria}): {CantidadArchivos} archivo(s), {TamanoTexto}.\nHaga clic para listar y explorar estos archivos.";
    }

    public record ResumenFormatosArchivos(
        int TotalArchivos,
        long TamanoTotalBytes,
        IReadOnlyList<CategoriaFormatoItem> Categorias,
        IReadOnlyList<FormatoExtensionItem>? Extensiones = null)
    {
        public IReadOnlyList<FormatoExtensionItem> ExtensionesLista => Extensiones ?? Array.Empty<FormatoExtensionItem>();
    }

    public class ProyectoArchivosService
    {
        // Las carpetas inmutables que siempre deben existir
        private readonly string[] _carpetasBase = { 
            "Datos_Espaciales", 
            "Documentos", 
            "Entregables", 
            "Otros" 
        };

        /// <summary>
        /// Evalúa los permisos efectivos del usuario actual de Windows (Active Directory UIS)
        /// sobre la carpeta del proyecto en el servidor o disco local.
        /// </summary>
        public virtual EvaluacionPermisos EvaluarPermisosCarpeta(string? rutaRaizProyecto)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
            {
                return new EvaluacionPermisos(
                    EstadoPermisoCarpeta.SinRuta,
                    PuedeLeer: false,
                    PuedeEscribir: true, // Sin carpeta física asignada; se permite editar metadatos o asignar una ruta
                    Mensaje: "El proyecto no tiene una carpeta física asignada.",
                    BadgeTexto: "Sin carpeta",
                    BadgeBackgroundHex: "#F5F5F5",
                    BadgeBorderHex: "#E0E0E0",
                    BadgeForegroundHex: "#616161"
                );
            }

            // 1. Probar existencia y lectura (enumeración)
            try
            {
                if (!Directory.Exists(rutaRaizProyecto))
                {
                    return new EvaluacionPermisos(
                        EstadoPermisoCarpeta.RutaNoEncontrada,
                        PuedeLeer: false,
                        PuedeEscribir: false,
                        Mensaje: "La carpeta configurada no existe o no se encuentra accesible en la red.",
                        BadgeTexto: "Ruta inaccesible",
                        BadgeBackgroundHex: "#FFF8E1",
                        BadgeBorderHex: "#FFE082",
                        BadgeForegroundHex: "#B78103"
                    );
                }

                // Intentar enumerar elementos para comprobar lectura real
                _ = Directory.EnumerateFileSystemEntries(rutaRaizProyecto).FirstOrDefault();
            }
            catch (UnauthorizedAccessException)
            {
                return new EvaluacionPermisos(
                    EstadoPermisoCarpeta.AccesoRestringido,
                    PuedeLeer: false,
                    PuedeEscribir: false,
                    Mensaje: "Acceso restringido: Su usuario en geomaticaad@uis.edu.co no cuenta con permisos en el servidor para esta carpeta.",
                    BadgeTexto: "🔒 Acceso restringido (UIS)",
                    BadgeBackgroundHex: "#FFEBEE",
                    BadgeBorderHex: "#FFCDD2",
                    BadgeForegroundHex: "#C62828"
                );
            }
            catch (Exception ex)
            {
                return new EvaluacionPermisos(
                    EstadoPermisoCarpeta.RutaNoEncontrada,
                    PuedeLeer: false,
                    PuedeEscribir: false,
                    Mensaje: $"Error al verificar la carpeta: {ex.Message}",
                    BadgeTexto: "Ruta inaccesible",
                    BadgeBackgroundHex: "#FFF8E1",
                    BadgeBorderHex: "#FFE082",
                    BadgeForegroundHex: "#B78103"
                );
            }

            // 2. Probar permisos de escritura/modificación mediante sonda atómica
            bool puedeEscribir = false;
            try
            {
                string probeFile = Path.Combine(rutaRaizProyecto, $".probe_{Guid.NewGuid():N}.tmp");
                using (var fs = new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose))
                {
                    // Si se crea con éxito, el usuario tiene permisos efectivos de escritura en el servidor de archivos
                    puedeEscribir = true;
                }
            }
            catch
            {
                puedeEscribir = false;
            }

            if (puedeEscribir)
            {
                return new EvaluacionPermisos(
                    EstadoPermisoCarpeta.LecturaEscritura,
                    PuedeLeer: true,
                    PuedeEscribir: true,
                    Mensaje: "Cuenta con permisos completos de lectura y escritura en la carpeta del servidor.",
                    BadgeTexto: "📂 Vinculada (Escritura)",
                    BadgeBackgroundHex: "#E8F4EC",
                    BadgeBorderHex: "#B2DFBF",
                    BadgeForegroundHex: "#1A5C34"
                );
            }
            else
            {
                return new EvaluacionPermisos(
                    EstadoPermisoCarpeta.SoloLectura,
                    PuedeLeer: true,
                    PuedeEscribir: false,
                    Mensaje: "Cuenta con permisos de solo lectura. No puede modificar ni eliminar datos en esta carpeta.",
                    BadgeTexto: "👁️ Solo Lectura",
                    BadgeBackgroundHex: "#E8F0FE",
                    BadgeBorderHex: "#B8D4FE",
                    BadgeForegroundHex: "#174EA6"
                );
            }
        }

        /// <summary>
        /// Comprueba si la ruta especificada existe físicamente y contiene al menos una subcarpeta.
        /// </summary>
        public virtual bool TieneCarpetas(string? rutaRaizProyecto)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return false;

            try
            {
                if (!Directory.Exists(rutaRaizProyecto))
                    return false;

                return Directory.EnumerateDirectories(rutaRaizProyecto).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Asegura que el directorio raíz del proyecto exista físicamente en disco o servidor,
        /// sin crear subcarpetas automáticas.
        /// </summary>
        public virtual void AsegurarCarpetaRaiz(string rutaRaizProyecto)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return;

            try
            {
                if (!Directory.Exists(rutaRaizProyecto))
                {
                    Directory.CreateDirectory(rutaRaizProyecto);
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("Su usuario no tiene permisos para crear carpetas en el servidor o disco local.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al estructurar carpetas: {ex.Message}");
            }
        }

        /// <summary>
        /// Crea la estructura inicial en el servidor para un nuevo proyecto (Datos_Espaciales, Documentos, Entregables, Otros).
        /// </summary>
        public virtual void CrearEstructuraProyecto(string rutaRaizProyecto)
        {
            try
            {
                AsegurarCarpetaRaiz(rutaRaizProyecto);

                foreach (var carpeta in _carpetasBase)
                {
                    string rutaCompleta = Path.Combine(rutaRaizProyecto, carpeta);
                    if (!Directory.Exists(rutaCompleta))
                    {
                        Directory.CreateDirectory(rutaCompleta);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // El usuario de AD actual no tiene permisos de escritura en la red
                throw new Exception("Su usuario no tiene permisos para crear carpetas en el servidor del proyecto.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al estructurar carpetas: {ex.Message}");
            }
        }

        /// <summary>
        /// Gestiona la estructura de carpetas según las reglas de negocio:
        /// - En caso de existir subcarpetas en la ruta seleccionada, NO crearlas.
        /// - En caso de no existir subcarpetas, consulta mediante el delegado si se desean crear o no.
        /// </summary>
        /// <returns>True si se crearon las subcarpetas estándar; False si no se crearon (o si ya existían).</returns>
        public virtual bool GestionarEstructuraCarpetas(string rutaRaizProyecto, Func<bool> solicitarConfirmacion)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return false;

            // 1. En caso de existir carpetas en la ruta seleccionada, NO crearlas
            if (TieneCarpetas(rutaRaizProyecto))
            {
                return false;
            }

            // 2. En caso de no existir carpetas, preguntar si se desean o no
            bool deseaCrear = solicitarConfirmacion != null && solicitarConfirmacion();
            if (deseaCrear)
            {
                CrearEstructuraProyecto(rutaRaizProyecto);
                return true;
            }
            else
            {
                AsegurarCarpetaRaiz(rutaRaizProyecto);
                return false;
            }
        }

        /// <summary>
        /// Lista el contenido de una ruta virtual dada para mostrar en la interfaz. 
        /// </summary>
        public List<NodoArchivoVirtual> ListarContenidoVirtual(string rutaRaizProyecto, string rutaRelativa = "")
        {
            var nodos = new List<NodoArchivoVirtual>();
            string rutaFisica = Path.Combine(rutaRaizProyecto, rutaRelativa.TrimStart('/', '\\'));

            try
            {
                if (!Directory.Exists(rutaFisica))
                    return nodos;

                // Si la ruta solicitada es en sí una Geodatabase, no exponemos sus tablas binarias internas
                if (rutaFisica.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    return nodos;

                var dirInfo = new DirectoryInfo(rutaFisica);

                string ubicacionActual = string.IsNullOrWhiteSpace(rutaRelativa) ? "(raíz)" : rutaRelativa.Trim('/', '\\');

                // Listar Carpetas
                foreach (var dir in dirInfo.GetDirectories())
                {
                    // Una File Geodatabase (.gdb) se trata como un dataset atómico, no como una carpeta navegable
                    if (dir.Name.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase))
                    {
                        long tamanoGdb = 0;
                        try
                        {
                            tamanoGdb = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                        }
                        catch
                        {
                            // En caso de permisos parciales en archivos binarios internos
                        }

                        nodos.Add(new ArchivoVirtual
                        {
                            Nombre = dir.Name,
                            RutaRelativaVirtual = Path.Combine(rutaRelativa, dir.Name).Replace('\\', '/'),
                            UbicacionRelativa = ubicacionActual,
                            TamanoBytes = tamanoGdb,
                            FechaModificacion = dir.LastWriteTime,
                            Extension = ".gdb"
                        });
                        continue;
                    }

                    nodos.Add(new CarpetaVirtual
                    {
                        Nombre = dir.Name,
                        RutaRelativaVirtual = Path.Combine(rutaRelativa, dir.Name).Replace('\\', '/'),
                        UbicacionRelativa = ubicacionActual
                    });
                }

                // Listar Archivos (Ligero, solo metadatos)
                foreach (var file in dirInfo.GetFiles())
                {
                    nodos.Add(new ArchivoVirtual
                    {
                        Nombre = file.Name,
                        RutaRelativaVirtual = Path.Combine(rutaRelativa, file.Name).Replace('\\', '/'),
                        UbicacionRelativa = ubicacionActual,
                        TamanoBytes = file.Length,
                        FechaModificacion = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Capturamos el bloqueo de AD limpiamente para que la UI no colapse
                throw new Exception("Acceso denegado: Su usuario no tiene permisos para ver esta carpeta.");
            }

            return nodos;
        }

        /// <summary>
        /// Escanea recursivamente los formatos y tipos de archivos espaciales y documentales presentes
        /// en la carpeta del proyecto sin bloquear la interfaz.
        /// </summary>
        public virtual async Task<ResumenFormatosArchivos> EscanearFormatosArchivosAsync(string? rutaRaizProyecto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>());

            return await Task.Run(() =>
            {
                if (!Directory.Exists(rutaRaizProyecto))
                    return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>());

                try
                {
                    var extensionesConteo = new Dictionary<string, (int Conteo, long Tamano)>(StringComparer.OrdinalIgnoreCase);
                    var enumOptions = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
                    };

                    var dir = new DirectoryInfo(rutaRaizProyecto);
                    int totalArchivos = 0;
                    long totalTamano = 0;

                    foreach (var file in dir.EnumerateFiles("*.*", enumOptions))
                    {
                        if (ct.IsCancellationRequested) break;

                        // Si el archivo reside dentro de una carpeta .gdb, no contar sus archivos binarios internos por separado
                        if (file.DirectoryName != null && file.DirectoryName.IndexOf(".gdb", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;

                        totalArchivos++;
                        totalTamano += file.Length;

                        var ext = file.Extension.ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(ext)) continue;

                        if (extensionesConteo.TryGetValue(ext, out var valor))
                        {
                            extensionesConteo[ext] = (valor.Conteo + 1, valor.Tamano + file.Length);
                        }
                        else
                        {
                            extensionesConteo[ext] = (1, file.Length);
                        }
                    }

                    // Contabilizar cada contenedor .gdb como un dataset vectorial único
                    try
                    {
                        foreach (var gdbDir in dir.EnumerateDirectories("*.gdb", enumOptions))
                        {
                            if (ct.IsCancellationRequested) break;

                            long tamanoGdb = 0;
                            try
                            {
                                tamanoGdb = gdbDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                            }
                            catch { }

                            totalArchivos++;
                            totalTamano += tamanoGdb;

                            if (extensionesConteo.TryGetValue(".gdb", out var valorGdb))
                            {
                                extensionesConteo[".gdb"] = (valorGdb.Conteo + 1, valorGdb.Tamano + tamanoGdb);
                            }
                            else
                            {
                                extensionesConteo[".gdb"] = (1, tamanoGdb);
                            }
                        }
                    }
                    catch { }

                    // Definir las categorías reconocidas en Geomática y SIG
                    var categoriasConfig = new[]
                    {
                        new {
                            Nombre = "Vectorial",
                            Icono = "🗺️",
                            Bg = "#E8F4EC",
                            Border = "#B2DFBF",
                            Fg = "#1A5C34",
                            Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".shp", ".gpkg", ".geojson", ".kml", ".kmz", ".tab", ".mif", ".gml", ".gdb", ".geodatabase" }
                        },
                        new {
                            Nombre = "Raster / Ortofoto",
                            Icono = "🛰️",
                            Bg = "#E3F2FD",
                            Border = "#BBDEFB",
                            Fg = "#1565C0",
                            Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".tif", ".tiff", ".img", ".dem", ".asc", ".dt2", ".ecw", ".jp2", ".sid" }
                        },
                        new {
                            Nombre = "LiDAR / Nubes",
                            Icono = "☁️",
                            Bg = "#F3E5F5",
                            Border = "#E1BEE7",
                            Fg = "#6A1B9A",
                            Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".las", ".laz", ".xyz", ".pts", ".ply" }
                        },
                        new {
                            Nombre = "CAD / Planos",
                            Icono = "📐",
                            Bg = "#FFF8E1",
                            Border = "#FFE082",
                            Fg = "#B78103",
                            Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dwg", ".dxf", ".dgn" }
                        },
                        new {
                            Nombre = "Documentación",
                            Icono = "📄",
                            Bg = "#F5F5F5",
                            Border = "#E0E0E0",
                            Fg = "#424242",
                            Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".csv", ".txt", ".md" }
                        }
                    };

                    // Sidecars o archivos auxiliares espaciales conocidos
                    var sidecarsConfig = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".dbf", ".shx", ".prj", ".cpg", ".sbn", ".sbx", ".tfw", ".ovr", ".aux.xml", ".rrd", ".aux", ".j2w", ".wld", ".qmd", ".xml"
                    };

                    var categoriasResultado = new List<CategoriaFormatoItem>();

                    foreach (var cat in categoriasConfig)
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

                    // Construir lista detallada de extensiones individuales
                    var extensionesResultado = new List<FormatoExtensionItem>();
                    foreach (var kvp in extensionesConteo)
                    {
                        string ext = kvp.Key;
                        int conteo = kvp.Value.Conteo;
                        long tamano = kvp.Value.Tamano;

                        bool esSidecar = sidecarsConfig.Contains(ext);
                        var catEncontrada = categoriasConfig.FirstOrDefault(c => c.Exts.Contains(ext));

                        string categoriaNombre = catEncontrada != null ? catEncontrada.Nombre : (esSidecar ? "Auxiliar / Sidecar" : "Otros");
                        string icono = catEncontrada != null ? catEncontrada.Icono : (esSidecar ? "📎" : "📁");
                        string bg = catEncontrada != null ? catEncontrada.Bg : (esSidecar ? "#ECEFF1" : "#FAFAFA");
                        string border = catEncontrada != null ? catEncontrada.Border : (esSidecar ? "#CFD8DC" : "#E0E0E0");
                        string fg = catEncontrada != null ? catEncontrada.Fg : (esSidecar ? "#546E7A" : "#616161");

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
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProyectoArchivosService] Error escaneando formatos en {rutaRaizProyecto}: {ex.Message}");
                    return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());
                }
            }, ct);
        }

        /// <summary>
        /// Lista recursivamente todos los archivos en el proyecto que coinciden con una extensión específica.
        /// Asigna a cada uno su subcarpeta relativa (UbicacionRelativa) para máxima transparencia al usuario.
        /// </summary>
        public virtual List<ArchivoVirtual> ListarArchivosPorExtension(string rutaRaizProyecto, string extension, CancellationToken ct = default)
        {
            var resultados = new List<ArchivoVirtual>();
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto) || !Directory.Exists(rutaRaizProyecto))
                return resultados;

            string extFiltro = extension.StartsWith('.') ? extension : "." + extension;

            try
            {
                var dir = new DirectoryInfo(rutaRaizProyecto);
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
                };

                string raizNormalizada = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (var file in dir.EnumerateFiles("*" + extFiltro, enumOptions))
                {
                    if (ct.IsCancellationRequested) break;

                    if (!file.Extension.Equals(extFiltro, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fileDir = file.DirectoryName ?? "";
                    string ubicacionRelativa = "";
                    if (fileDir.StartsWith(raizNormalizada, StringComparison.OrdinalIgnoreCase))
                    {
                        ubicacionRelativa = fileDir.Substring(raizNormalizada.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    }
                    if (string.IsNullOrWhiteSpace(ubicacionRelativa))
                    {
                        ubicacionRelativa = "(raíz)";
                    }

                    string rutaRelativaVirtual = (ubicacionRelativa == "(raíz)" ? file.Name : $"{ubicacionRelativa}/{file.Name}").Replace('\\', '/');

                    resultados.Add(new ArchivoVirtual
                    {
                        Nombre = file.Name,
                        RutaRelativaVirtual = rutaRelativaVirtual,
                        UbicacionRelativa = ubicacionRelativa,
                        TamanoBytes = file.Length,
                        FechaModificacion = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoArchivosService] Error listando archivos por extensión {extension}: {ex.Message}");
            }

            return resultados;
        }

        /// <summary>
        /// Comprime de forma asíncrona una carpeta o conjunto de archivos del proyecto en un archivo .ZIP,
        /// reportando el progreso porcentual y permitiendo cancelación sin congelar la interfaz de usuario.
        /// Preserva la jerarquía de subcarpetas (incluyendo directorios .gdb) y carpetas canónicas vacías.
        /// </summary>
        public virtual async Task<bool> EmpaquetarCarpetaZipAsync(
            string rutaOrigen,
            string rutaDestinoZip,
            IProgress<double>? progreso = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rutaOrigen))
                throw new ArgumentException("La ruta de origen no puede estar vacía.", nameof(rutaOrigen));

            if (!Directory.Exists(rutaOrigen) && !File.Exists(rutaOrigen))
                throw new DirectoryNotFoundException($"La ruta de origen '{rutaOrigen}' no existe o no es accesible.");

            if (string.IsNullOrWhiteSpace(rutaDestinoZip))
                throw new ArgumentException("La ruta de destino del archivo ZIP no puede estar vacía.", nameof(rutaDestinoZip));

            return await Task.Run(async () =>
            {
                string dirDestino = Path.GetDirectoryName(rutaDestinoZip) ?? "";
                if (!string.IsNullOrEmpty(dirDestino) && !Directory.Exists(dirDestino))
                {
                    Directory.CreateDirectory(dirDestino);
                }

                if (File.Exists(rutaDestinoZip))
                {
                    File.Delete(rutaDestinoZip);
                }

                // Caso especial: Empaquetar un único archivo
                if (File.Exists(rutaOrigen) && !Directory.Exists(rutaOrigen))
                {
                    var fileInfo = new FileInfo(rutaOrigen);
                    using (var zipStream = new FileStream(rutaDestinoZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        var entry = archive.CreateEntry(fileInfo.Name, CompressionLevel.Optimal);
                        entry.LastWriteTime = fileInfo.LastWriteTime;

                        using (var src = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true))
                        using (var dst = entry.Open())
                        {
                            await src.CopyToAsync(dst, 65536, ct).ConfigureAwait(false);
                        }
                    }
                    progreso?.Report(100.0);
                    return true;
                }

                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
                };

                var dirInfo = new DirectoryInfo(rutaOrigen);
                string raizNormalizada = dirInfo.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var todosLosArchivos = dirInfo.EnumerateFiles("*", enumOptions)
                    .Where(f => !f.Name.StartsWith(".probe_", StringComparison.OrdinalIgnoreCase) &&
                                !f.Name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var directorios = dirInfo.EnumerateDirectories("*", enumOptions).ToList();

                try
                {
                    using (var zipStream = new FileStream(rutaDestinoZip, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        // Preservar carpetas canónicas vacías
                        foreach (var subDir in directorios)
                        {
                            ct.ThrowIfCancellationRequested();
                            bool tieneElementos = false;
                            try
                            {
                                tieneElementos = Directory.EnumerateFileSystemEntries(subDir.FullName).Any();
                            }
                            catch { }

                            if (!tieneElementos)
                            {
                                string relFolder = subDir.FullName.Substring(raizNormalizada.Length)
                                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    .Replace('\\', '/') + "/";
                                archive.CreateEntry(relFolder);
                            }
                        }

                        if (todosLosArchivos.Count == 0)
                        {
                            progreso?.Report(100.0);
                            return true;
                        }

                        int totalArchivos = todosLosArchivos.Count;
                        int procesados = 0;

                        foreach (var file in todosLosArchivos)
                        {
                            ct.ThrowIfCancellationRequested();

                            string rutaCompleta = file.FullName;
                            string entradaRelativa = rutaCompleta.Substring(raizNormalizada.Length)
                                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                .Replace('\\', '/');

                            try
                            {
                                var entry = archive.CreateEntry(entradaRelativa, CompressionLevel.Optimal);
                                entry.LastWriteTime = file.LastWriteTime;

                                using (var sourceStream = new FileStream(rutaCompleta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, useAsync: true))
                                using (var entryStream = entry.Open())
                                {
                                    await sourceStream.CopyToAsync(entryStream, 65536, ct).ConfigureAwait(false);
                                }
                            }
                            catch (IOException ioEx)
                            {
                                Debug.WriteLine($"[ProyectoArchivosService] Archivo en uso o bloqueado durante ZIP: {file.FullName}. {ioEx.Message}");
                            }
                            catch (UnauthorizedAccessException authEx)
                            {
                                Debug.WriteLine($"[ProyectoArchivosService] Sin permisos para leer archivo durante ZIP: {file.FullName}. {authEx.Message}");
                            }

                            procesados++;
                            progreso?.Report((double)procesados / totalArchivos * 100.0);
                        }
                    }

                    progreso?.Report(100.0);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    if (File.Exists(rutaDestinoZip))
                    {
                        try { File.Delete(rutaDestinoZip); } catch { }
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProyectoArchivosService] Error generando archivo ZIP: {ex.Message}");
                    if (File.Exists(rutaDestinoZip))
                    {
                        try { File.Delete(rutaDestinoZip); } catch { }
                    }
                    throw;
                }
            }, ct);
        }
    }
}