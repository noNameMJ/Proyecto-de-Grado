using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geomatica.Desktop.Models;
using Geomatica.Domain.Catalogos;

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

    public record ProgresoEscaneoFormatos(
        int TotalArchivos,
        int TotalCarpetas,
        IReadOnlyList<FormatoExtensionItem> ExtensionesParciales,
        IReadOnlyList<CategoriaFormatoItem>? CategoriasParciales = null);

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

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (EvaluacionPermisos Eval, DateTime ExpiraUtc)> _permisosCache
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Intenta obtener una evaluación de permisos previamente calculada y aún vigente en memoria.
        /// </summary>
        public virtual bool TryObtenerPermisosCacheados(string? rutaRaizProyecto, out EvaluacionPermisos? eval)
        {
            eval = null;
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto)) return false;
            try
            {
                string clave = ClaveIndice(rutaRaizProyecto);
                if (_permisosCache.TryGetValue(clave, out var item) && item.ExpiraUtc > DateTime.UtcNow)
                {
                    eval = item.Eval;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Evalúa los permisos efectivos del usuario actual de Windows (Active Directory UIS)
        /// sobre la carpeta del proyecto en el servidor o disco local.
        /// Reutiliza el resultado en caché si fue calculado recientemente (TTL de 2 minutos).
        /// </summary>
        public virtual EvaluacionPermisos EvaluarPermisosCarpeta(string? rutaRaizProyecto)
        {
            return EvaluarPermisosCarpeta(rutaRaizProyecto, forzar: false);
        }

        /// <summary>
        /// Evalúa los permisos efectivos con la opción de forzar una nueva prueba física en disco/red.
        /// </summary>
        public virtual EvaluacionPermisos EvaluarPermisosCarpeta(string? rutaRaizProyecto, bool forzar)
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

            string clave;
            try { clave = ClaveIndice(rutaRaizProyecto); }
            catch { clave = rutaRaizProyecto; }

            if (!forzar && _permisosCache.TryGetValue(clave, out var item) && item.ExpiraUtc > DateTime.UtcNow)
            {
                return item.Eval;
            }

            var eval = EjecutarEvaluacionPermisos(rutaRaizProyecto);
            _permisosCache[clave] = (eval, DateTime.UtcNow.AddMinutes(2));
            return eval;
        }

        /// <summary>
        /// Evalúa los permisos de forma asíncrona en un hilo de fondo para no bloquear el hilo de interfaz (UI).
        /// </summary>
        public virtual Task<EvaluacionPermisos> EvaluarPermisosCarpetaAsync(string? rutaRaizProyecto, bool forzar = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return Task.FromResult(EvaluarPermisosCarpeta(rutaRaizProyecto));

            if (!forzar && TryObtenerPermisosCacheados(rutaRaizProyecto, out var cacheado) && cacheado != null)
                return Task.FromResult(cacheado);

            return Task.Run(() => EvaluarPermisosCarpeta(rutaRaizProyecto, forzar), ct);
        }

        private EvaluacionPermisos EjecutarEvaluacionPermisos(string rutaRaizProyecto)
        {
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
        /// Comprueba si la ruta especificada existe físicamente y contiene al menos un archivo (ignorando sondas temporales .probe_).
        /// </summary>
        public virtual bool TieneArchivos(string? rutaRaizProyecto)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return false;

            try
            {
                if (!Directory.Exists(rutaRaizProyecto))
                    return false;

                return Directory.EnumerateFiles(rutaRaizProyecto)
                    .Any(f => !Path.GetFileName(f).StartsWith(".probe_", StringComparison.OrdinalIgnoreCase));
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
        /// - En caso de existir subcarpetas o archivos en la ruta seleccionada, NO crearlas.
        /// - En caso de no existir ni subcarpetas ni archivos, consulta mediante el delegado si se desean crear o no.
        /// </summary>
        /// <returns>True si se crearon las subcarpetas estándar; False si no se crearon (o si ya existían archivos/carpetas).</returns>
        public virtual bool GestionarEstructuraCarpetas(string rutaRaizProyecto, Func<bool> solicitarConfirmacion)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return false;

            // 1. En caso de existir carpetas o archivos en la ruta seleccionada, NO crear la estructura predeterminada
            if (TieneCarpetas(rutaRaizProyecto) || TieneArchivos(rutaRaizProyecto))
            {
                return false;
            }

            // 2. En caso de no existir carpetas ni archivos, preguntar si se desean o no
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
        /// Copia recursivamente el contenido de un directorio hacia otra ubicación física en disco.
        /// </summary>
        public virtual void CopiarDirectorioRecursivo(string origen, string destino)
        {
            Directory.CreateDirectory(destino);
            foreach (var file in Directory.GetFiles(origen))
            {
                File.Copy(file, Path.Combine(destino, Path.GetFileName(file)), true);
            }
            foreach (var subDir in Directory.GetDirectories(origen))
            {
                CopiarDirectorioRecursivo(subDir, Path.Combine(destino, Path.GetFileName(subDir)));
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
                        UbicacionRelativa = ubicacionActual,
                        FechaModificacion = dir.LastWriteTime
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
        public virtual async Task<ResumenFormatosArchivos> EscanearFormatosArchivosAsync(
            string? rutaRaizProyecto,
            IProgress<ProgresoEscaneoFormatos>? progreso = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto))
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());

            if (!Directory.Exists(rutaRaizProyecto))
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());

            try
            {
                var indice = await ObtenerIndiceAsync(rutaRaizProyecto, progreso, null, ct).ConfigureAwait(false);
                return indice.GenerarResumenFormatos();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoArchivosService] Error escaneando formatos en {rutaRaizProyecto}: {ex.Message}");
                return new ResumenFormatosArchivos(0, 0, Array.Empty<CategoriaFormatoItem>(), Array.Empty<FormatoExtensionItem>());
            }
        }

        public virtual List<ArchivoVirtual> ListarArchivosPorExtensiones(string rutaRaizProyecto, IEnumerable<string> extensiones, CancellationToken ct = default)
        {
            var resultados = new List<ArchivoVirtual>();
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto) || !Directory.Exists(rutaRaizProyecto) || extensiones == null)
                return resultados;

            var extSet = new HashSet<string>(
                extensiones
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(CatalogoFormatosGis.NormalizarExtension),
                StringComparer.OrdinalIgnoreCase
            );

            if (extSet.Count == 0)
                return resultados;

            try
            {
                // Reutiliza el índice en caché si existe; si no, lo construye (recorrido paralelo que
                // omite el contenido interno de las .gdb) y lo deja en caché para las siguientes consultas.
                var indice = ObtenerIndiceSincrono(rutaRaizProyecto, ct);
                resultados = indice.Filtrar(extSet);
            }
            catch (OperationCanceledException)
            {
                // Búsqueda cancelada: se devuelve lo que haya.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoArchivosService] Error listando archivos por múltiples extensiones: {ex.Message}");
            }

            return resultados;
        }

        // ══════════════════════════════════════════════════════════════
        // Índice de archivos por proyecto (caché en memoria)
        // ══════════════════════════════════════════════════════════════

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<IndiceArchivosProyecto>>> _indices
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<IProgress<ProgresoEscaneoFormatos>>> _progresoSubscribers
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Número de carpetas listadas en paralelo al construir el índice.</summary>
        public int GradoParalelismoIndice { get; set; } = 8;

        private static string ClaveIndice(string ruta)
            => Path.GetFullPath(ruta).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        /// <summary>Devuelve el índice ya construido para la ruta, si existe y terminó correctamente.</summary>
        public virtual bool TryObtenerIndice(string? rutaRaizProyecto, out IndiceArchivosProyecto? indice)
        {
            indice = null;
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto)) return false;
            if (_indices.TryGetValue(ClaveIndice(rutaRaizProyecto), out var lazy)
                && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
            {
                indice = lazy.Value.Result;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Obtiene (o construye en segundo plano) el índice de archivos del proyecto.
        /// Llamadas concurrentes para la misma ruta comparten una única construcción.
        /// Permite suscribir listeners de progreso en tiempo real para formatos y conteos.
        /// </summary>
        public virtual Task<IndiceArchivosProyecto> ObtenerIndiceAsync(
            string rutaRaizProyecto,
            IProgress<ProgresoEscaneoFormatos>? progresoFormatos,
            IProgress<int>? progresoConteo = null,
            CancellationToken ct = default)
        {
            var clave = ClaveIndice(rutaRaizProyecto);

            if (progresoFormatos != null)
            {
                var bag = _progresoSubscribers.GetOrAdd(clave, _ => new System.Collections.Concurrent.ConcurrentBag<IProgress<ProgresoEscaneoFormatos>>());
                bag.Add(progresoFormatos);

                if (TryObtenerIndice(rutaRaizProyecto, out var existente) && existente != null)
                {
                    var resumen = existente.GenerarResumenFormatos();
                    progresoFormatos.Report(new ProgresoEscaneoFormatos(
                        existente.TotalArchivos,
                        existente.TotalCarpetas,
                        resumen.ExtensionesLista,
                        resumen.Categorias));
                }
            }

            var progressBridge = new Progress<ProgresoEscaneoFormatos>(p =>
            {
                if (_progresoSubscribers.TryGetValue(clave, out var subBag))
                {
                    foreach (var sub in subBag)
                    {
                        try { sub.Report(p); } catch { }
                    }
                }
            });

            var lazy = _indices.GetOrAdd(clave, _ => new Lazy<Task<IndiceArchivosProyecto>>(() =>
                Task.Run(() => IndiceArchivosProyecto.Construir(clave, GradoParalelismoIndice, progressBridge, progresoConteo, CancellationToken.None))));

            var tarea = lazy.Value;
            tarea.ContinueWith(_ =>
            {
                _indices.TryRemove(new KeyValuePair<string, Lazy<Task<IndiceArchivosProyecto>>>(clave, lazy));
                _progresoSubscribers.TryRemove(clave, out var _);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            return ct.CanBeCanceled ? tarea.WaitAsync(ct) : tarea;
        }

        public virtual Task<IndiceArchivosProyecto> ObtenerIndiceAsync(
            string rutaRaizProyecto,
            IProgress<int>? progreso = null,
            CancellationToken ct = default)
            => ObtenerIndiceAsync(rutaRaizProyecto, null, progreso, ct);

        private IndiceArchivosProyecto ObtenerIndiceSincrono(string rutaRaizProyecto, CancellationToken ct)
        {
            if (TryObtenerIndice(rutaRaizProyecto, out var existente) && existente != null)
                return existente;
            return ObtenerIndiceAsync(rutaRaizProyecto, null, ct).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Descarta el índice de la ruta (tras Refrescar o tras operaciones de archivos de la propia aplicación).
        /// </summary>
        public virtual void InvalidarIndice(string? rutaRaizProyecto)
        {
            if (string.IsNullOrWhiteSpace(rutaRaizProyecto)) return;
            try
            {
                string clave = ClaveIndice(rutaRaizProyecto);
                _indices.TryRemove(clave, out _);
                _permisosCache.TryRemove(clave, out _);
            }
            catch { }
        }

        /// <summary>
        /// Lista recursivamente todos los archivos en el proyecto que coinciden con una extensión específica.
        /// Asigna a cada uno su subcarpeta relativa (UbicacionRelativa) para máxima transparencia al usuario.
        /// </summary>
        public virtual List<ArchivoVirtual> ListarArchivosPorExtension(string rutaRaizProyecto, string extension, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(extension)) return new List<ArchivoVirtual>();
            return ListarArchivosPorExtensiones(rutaRaizProyecto, new[] { extension }, ct);
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
