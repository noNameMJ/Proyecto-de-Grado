using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Geomatica.ServerAgent;

public sealed class FileAuditorWorker : BackgroundService
{
    private readonly ILogger<FileAuditorWorker> _logger;
    private readonly IConfiguration _config;
    private readonly IProyectoRepository _proyectoRepository;

    private readonly ConcurrentDictionary<string, DebouncedFileEvent> _pendingEvents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private List<MonitoredProject> _activeProjects = new();
    private readonly HashSet<string> _watchedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, bool> _proyectosVerificadosParaBarrido = new();

    // ══════════════════════════════════════════════════════════════
    // AUDITORÍA DE SEGURIDAD WINDOWS (EVENTOS 4663, 4660 y 4656)
    // ══════════════════════════════════════════════════════════════
    private const string UsuarioNoIdentificado = "No identificado";
    private static readonly TimeSpan MaxEventAge = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MaxDeleteEventAge = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaxHandleAge = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, RecentWriterInfo> _recentWriters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RecentWriterInfo> _recentDeleters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HandleContextInfo> _handleToObjectMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _recentSecurityLogThrottle = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> _deviceToDriveMap = new(StringComparer.OrdinalIgnoreCase);
    private EventLogWatcher? _securityWatcher;

    // Máscaras de acceso NTFS en Eventos 4663 / 4656
    private const uint DELETE_MASK = 0x10000 | 0x40; // 0x10000 = DELETE, 0x40 = DeleteChild
    private const uint WRITE_MASK = 0x2 | 0x4 | 0x6 | 0x100 | 0x40000 | 0x80000; // WriteData, AppendData, WriteAttributes, WriteDAC, WriteOwner
    private const uint MUTATION_MASK = DELETE_MASK | WRITE_MASK;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryDosDeviceW")]
    private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

    [DllImport("netapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int NetFileEnum(
        string? servername,
        string? basepath,
        string? username,
        int level,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        IntPtr resume_handle);

    [DllImport("netapi32.dll", SetLastError = true)]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FILE_INFO_3
    {
        public int fi3_id;
        public int fi3_permissions;
        public int fi3_num_locks;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? fi3_pathname;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? fi3_username;
    }

    public FileAuditorWorker(
        ILogger<FileAuditorWorker> logger,
        IConfiguration config,
        IProyectoRepository proyectoRepository)
    {
        _logger = logger;
        _config = config;
        _proyectoRepository = proyectoRepository;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("======================================================");
        _logger.LogInformation("  Geomatica.ServerAgent - Agente de Auditoría en Servidor");
        _logger.LogInformation("  Iniciando servicio de monitoreo en segundo plano...");
        _logger.LogInformation("======================================================");

        try
        {
            // 1. Asegurar que la tabla y columnas de auditoría existan en PostgreSQL
            await _proyectoRepository.AsegurarTablaAuditoriaArchivoAsync(stoppingToken);
            _logger.LogInformation("[BD] Conexión establecida y tabla 'geovisor.auditoria_archivo' verificada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BD] Error inicializando conexión a PostgreSQL. Verifique la cadena 'PostgresConnection' en appsettings.json. Reintentando de fondo.");
        }

        // 2. Asegurar subcategorías de auditoría de Windows (File System, Handle Manipulation, Detailed File Share)
        AsegurarSubcategoriasAuditpol();

        // 3. Iniciar escucha de eventos de auditoría de seguridad Windows (Eventos 4663, 4660, 4656 y 5145)
        IniciarAuditoriaEventosSeguridadWindows();

        // 3. Carga inicial inmediata antes de entrar al bucle periódico
        try
        {
            ActualizarMapeoDispositivos();
            await RecargarProyectosYVigilantesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Proyectos] Error en la carga inicial de proyectos desde la base de datos.");
        }

        // 4. Tarea de recarga periódica de proyectos, mapeo de dispositivos y limpieza de caché
        int reloadSeconds = _config.GetValue<int>("AgentSettings:IntervaloRecargaProyectosSegundos", 60);
        if (reloadSeconds < 10) reloadSeconds = 10;

        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(reloadSeconds), stoppingToken);
                try
                {
                    ActualizarMapeoDispositivos();
                    LimpiarEscritoresExpirados();
                    await RecargarProyectosYVigilantesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Proyectos] Error al recargar proyectos desde la base de datos.");
                }
            }
        }, stoppingToken);

        // 4b. Mantenimiento: Purga periódica por vejez (cada 24 horas, retención: 7 días, piso: 5)
        _ = Task.Run(async () =>
        {
            try
            {
                // Esperar 2 minutos tras el arranque para estabilizar vigilantes
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("[Mantenimiento] Ejecutando purga automática de auditoría de archivos por vejez (7 días, piso mínimo 5)...");
                    int purgados = await _proyectoRepository.PurgarAuditoriaArchivoPorVejezAsync(7, 5, stoppingToken);
                    if (purgados > 0)
                    {
                        _logger.LogInformation("[Mantenimiento] Purga completada: {Count} registros históricos antiguos eliminados.", purgados);
                    }
                    else
                    {
                        _logger.LogInformation("[Mantenimiento] Purga completada: No se encontraron registros antiguos para eliminar.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Mantenimiento] Error al ejecutar purga periódica de auditoría de archivos.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }, stoppingToken);

        // 5. Bucle de procesamiento de la cola de eventos debounced
        int debounceMs = _config.GetValue<int>("AgentSettings:TiempoDebounceMilisegundos", 1000);
        if (debounceMs < 200) debounceMs = 200;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarEventosPendientesAsync(debounceMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Eventos] Error no controlado en bucle de procesamiento.");
            }

            await Task.Delay(250, stoppingToken);
        }

        LimpiarVigilantes();
        _logger.LogInformation("[Servicio] Geomatica.ServerAgent detenido limpiamente.");
    }

    private void AsegurarSubcategoriasAuditpol()
    {
        if (!OperatingSystem.IsWindows()) return;

        // GUIDs universales independientes del idioma de Windows Server:
        // {0CCE921D-69AE-11D9-BED3-505054503030} = File System (Eventos 4656, 4660, 4663)
        // {0CCE9223-69AE-11D9-BED3-505054503030} = Handle Manipulation (Eventos 4656, 4658, 4660)
        // {0CCE9244-69AE-11D9-BED3-505054503030} = Detailed File Share (Evento 5145 con ruta + AccessMask SMB)
        string[] subcategories =
        {
            "{0CCE921D-69AE-11D9-BED3-505054503030}",
            "{0CCE9223-69AE-11D9-BED3-505054503030}",
            "{0CCE9244-69AE-11D9-BED3-505054503030}"
        };

        foreach (var guid in subcategories)
        {
            try
            {
                using var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "auditpol.exe",
                        Arguments = $"/set /subcategory:\"{guid}\" /success:enable",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.Start();
                proc.WaitForExit(3000);
            }
            catch
            {
                // Ignorar si no corre con privilegios de Administrador
            }
        }
    }

    private void IniciarAuditoriaEventosSeguridadWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            ActualizarMapeoDispositivos();

            // Consulta XPath estandarizada para eventos de auditoría de archivos (4663, 4660, 4656, 5140 y 5145)
            string queryXml = "*[System[(EventID=4663 or EventID=4660 or EventID=4656 or EventID=5145)]]";
            var query = new EventLogQuery("Security", PathType.LogName, queryXml);

            _securityWatcher = new EventLogWatcher(query);
            _securityWatcher.EventRecordWritten += OnSecurityEventRecordWritten;
            _securityWatcher.Enabled = true;

            _logger.LogInformation("[Seguridad 4663/4660/4656/5145] Escucha activa en el canal 'Security' para los Eventos 4663, 4660, 4656 y 5145.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("[Seguridad 4663] Permiso denegado al leer el registro 'Security': {Msg}. Ejecute como Administrador o configure 'Event Log Readers'.", ex.Message);
        }
        catch (EventLogReadingException ex)
        {
            _logger.LogWarning("[Seguridad 4663] No se pudo conectar al canal 'Security': {Msg}.", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Seguridad 4663] Error al inicializar EventLogWatcher.");
        }
    }

    private void OnSecurityEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventRecord == null) return;

        try
        {
            using var record = e.EventRecord;
            int eventId = record.Id;
            string xml = record.ToXml();
            if (string.IsNullOrWhiteSpace(xml)) return;

            var doc = XDocument.Parse(xml);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var eventData = doc.Root?.Element(ns + "EventData");
            if (eventData == null) return;

            string? userName = null;
            string? domainName = null;
            string? objectName = null;
            string? accessMaskStr = null;
            string? handleId = null;
            string? shareLocalPath = null;
            string? relativeTargetName = null;

            foreach (var data in eventData.Elements(ns + "Data"))
            {
                string? name = (string?)data.Attribute("Name");
                if (name == null) continue;

                if (name == "SubjectUserName") userName = data.Value;
                else if (name == "SubjectDomainName") domainName = data.Value;
                else if (name == "ObjectName") objectName = data.Value;
                else if (name == "AccessMask") accessMaskStr = data.Value;
                else if (name == "HandleId") handleId = data.Value?.Trim();
                else if (name == "ShareLocalPath") shareLocalPath = data.Value?.Trim();
                else if (name == "RelativeTargetName") relativeTargetName = data.Value?.Trim();
            }

            // En eventos SMB 5145, construir ObjectName a partir de ShareLocalPath + RelativeTargetName
            if (string.IsNullOrWhiteSpace(objectName) && !string.IsNullOrWhiteSpace(shareLocalPath))
            {
                objectName = !string.IsNullOrWhiteSpace(relativeTargetName) && relativeTargetName != @"\"
                    ? Path.Combine(shareLocalPath, relativeTargetName)
                    : shareLocalPath;
            }

            if (string.IsNullOrWhiteSpace(userName) || EsCuentaSistemaOMaquina(userName))
            {
                return;
            }

            string usuarioCompleto = FormatearUsuarioDominio(domainName, userName);
            string normalizedHandle = NormalizarHandleId(handleId);

            // ══════════════════════════════════════════════════════════════
            // CASO A: EVENTO 4660 ("An object was deleted")
            // El evento 4660 NO incluye ObjectName ni AccessMask en su XML;
            // correlaciona la eliminación mediante HandleId con el 4663 previo.
            // ══════════════════════════════════════════════════════════════
            if (eventId == 4660)
            {
                string? rutaResuelta = !string.IsNullOrWhiteSpace(objectName)
                    ? NormalizarRutaWindows(objectName)
                    : null;

                if (string.IsNullOrWhiteSpace(rutaResuelta) && !string.IsNullOrEmpty(normalizedHandle))
                {
                    if (_handleToObjectMap.TryGetValue(normalizedHandle, out var handleCtx))
                    {
                        rutaResuelta = handleCtx.RutaNormalizada;
                    }
                }

                if (!string.IsNullOrWhiteSpace(rutaResuelta))
                {
                    RegistrarEnCacheDeleters(rutaResuelta, new RecentWriterInfo
                    {
                        Usuario = usuarioCompleto,
                        Timestamp = DateTime.UtcNow,
                        AccessMask = 0x10000
                    });

                    _logger.LogInformation("[Seguridad 4660] Evento 4660 (DELETE por Handle={Handle}) recibido: Archivo='{Path}', Usuario='{User}'",
                        normalizedHandle, rutaResuelta, usuarioCompleto);
                }
                return;
            }

            // ══════════════════════════════════════════════════════════════
            // CASO B: EVENTO 4663 o 4656 ("An attempt was made to access an object" / "A handle to an object was requested")
            // ══════════════════════════════════════════════════════════════
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return;
            }

            string rutaNormalizada = NormalizarRutaWindows(objectName);
            if (string.IsNullOrWhiteSpace(rutaNormalizada)) return;

            uint accessMask = ParsearAccessMask(accessMaskStr);

            // Guardar correlación HandleId -> Ruta para futuros eventos 4660 de borrado
            if (!string.IsNullOrEmpty(normalizedHandle))
            {
                _handleToObjectMap[normalizedHandle] = new HandleContextInfo
                {
                    RutaNormalizada = rutaNormalizada,
                    Usuario = usuarioCompleto,
                    Timestamp = DateTime.UtcNow
                };
            }

            if (accessMask != 0 && (accessMask & MUTATION_MASK) == 0)
            {
                return;
            }

            var info = new RecentWriterInfo
            {
                Usuario = usuarioCompleto,
                Timestamp = DateTime.UtcNow,
                AccessMask = accessMask
            };

            // En eventos 4663 (acceso real al kernel), la máscara indica la operación exacta ejecutada.
            // En eventos 4656 / 5145 (solicitud de handle / recurso compartido SMB), si el cliente solicita permisos combinados
            // de escritura + borrado (ej. 0x17019F o 0x12019F al crear/copiar un archivo), es una ESCRITURA y NO debe contaminar _recentDeleters.
            // En cambio, cuando el cliente SMB abre el archivo para eliminarlo o renombrarlo, solicita 0x110080, 0x10080 o 0x10000 (DELETE sin WriteData/AppendData).
            bool tieneWriteData = (accessMask & (0x2 | 0x4)) != 0;
            bool esDelete = eventId == 4663
                ? (accessMask & DELETE_MASK) != 0
                : (accessMask & DELETE_MASK) != 0 && !tieneWriteData;
            bool esWrite = (accessMask & WRITE_MASK) != 0;

            if (esDelete)
            {
                RegistrarEnCacheDeleters(rutaNormalizada, info);
                if (DebeRegistrarLogSeguridad($"DEL|{rutaNormalizada}|{usuarioCompleto}"))
                {
                    _logger.LogInformation("[Seguridad {EventId}] Evento {EventId} (DELETE) recibido: Archivo='{Path}', Usuario='{User}', Máscara=0x{Mask:X}",
                        eventId, eventId, rutaNormalizada, usuarioCompleto, accessMask);
                }
            }

            if (esWrite)
            {
                RegistrarEnCacheWriters(rutaNormalizada, info);
                if (DebeRegistrarLogSeguridad($"WRT|{rutaNormalizada}|{usuarioCompleto}"))
                {
                    _logger.LogInformation("[Seguridad {EventId}] Evento {EventId} (WRITE) recibido: Archivo='{Path}', Usuario='{User}', Máscara=0x{Mask:X}",
                        eventId, eventId, rutaNormalizada, usuarioCompleto, accessMask);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Seguridad 4663] Error analizando registro de evento.");
        }
    }

    private bool DebeRegistrarLogSeguridad(string key)
    {
        var now = DateTime.UtcNow;
        if (_recentSecurityLogThrottle.TryGetValue(key, out var lastTime) && (now - lastTime).TotalSeconds < 2.0)
        {
            return false;
        }
        _recentSecurityLogThrottle[key] = now;
        return true;
    }

    private void RegistrarEnCacheWriters(string rutaNormalizada, RecentWriterInfo info)
    {
        _recentWriters[rutaNormalizada] = info;
        string fName = Path.GetFileName(rutaNormalizada);
        if (!string.IsNullOrEmpty(fName))
        {
            _recentWriters[fName] = info;
        }
    }

    private void RegistrarEnCacheDeleters(string rutaNormalizada, RecentWriterInfo info)
    {
        _recentDeleters[rutaNormalizada] = info;
        string fName = Path.GetFileName(rutaNormalizada);
        if (!string.IsNullOrEmpty(fName))
        {
            _recentDeleters[fName] = info;
        }
    }

    private static bool EsCuentaSistemaOMaquina(string userName)
    {
        return userName.EndsWith("$", StringComparison.Ordinal) ||
               userName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
               userName.Equals("LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
               userName.Equals("NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
               userName.Equals("ANONYMOUS LOGON", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatearUsuarioDominio(string? domainName, string userName)
    {
        if (!string.IsNullOrWhiteSpace(domainName) &&
            !domainName.Equals("-", StringComparison.Ordinal) &&
            !domainName.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase) &&
            !domainName.Equals(userName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{domainName}\\{userName}";
        }
        return userName;
    }

    private static uint ParsearAccessMask(string? accessMaskStr)
    {
        if (string.IsNullOrWhiteSpace(accessMaskStr)) return 0;
        string trimmed = accessMaskStr.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            uint.TryParse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint maskHex);
            return maskHex;
        }
        uint.TryParse(trimmed, out uint maskDec);
        return maskDec;
    }

    private static string NormalizarHandleId(string? rawHandle)
    {
        if (string.IsNullOrWhiteSpace(rawHandle)) return string.Empty;
        string h = rawHandle.Trim();
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(h.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out ulong val))
            {
                return val == 0 ? string.Empty : $"0x{val:X}";
            }
        }
        else if (ulong.TryParse(h, out ulong valDec))
        {
            return valDec == 0 ? string.Empty : $"0x{valDec:X}";
        }
        return h;
    }

    private static void ActualizarMapeoDispositivos()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var sb = new StringBuilder(1024);
            foreach (var drive in DriveInfo.GetDrives())
            {
                string driveName = drive.Name.TrimEnd('\\'); // Ej: "C:" o "G:"
                sb.Clear();
                if (QueryDosDevice(driveName, sb, sb.Capacity) > 0)
                {
                    string devicePath = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(devicePath))
                    {
                        _deviceToDriveMap[devicePath] = driveName;
                    }
                }
            }
        }
        catch
        {
            // Ignorar en caso de restricciones de entorno
        }
    }

    public static string NormalizarRutaWindows(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
        string path = rawPath.Trim().Replace('/', '\\');

        // Quitar prefijo \??\ si existe (espacio de nombres del Administrador de Objetos NT)
        if (path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(4);
        }

        // Convertir rutas de dispositivo del kernel \Device\HarddiskVolumeX\ a letra de unidad
        if (path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
        {
            if (_deviceToDriveMap.IsEmpty)
            {
                ActualizarMapeoDispositivos();
            }

            foreach (var kvp in _deviceToDriveMap)
            {
                if (path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    path = kvp.Value + path.Substring(kvp.Key.Length);
                    break;
                }
            }
        }

        // Quitar sufijos de flujos de datos alternativos de NTFS (ej: :Zone.Identifier o :$DATA)
        int colonIdx = path.StartsWith(@"\\", StringComparison.Ordinal) ? path.IndexOf(':') : path.IndexOf(':', 2);
        if (colonIdx > 0)
        {
            path = path.Substring(0, colonIdx);
        }

        return path.TrimEnd('\\');
    }

    private async Task<string> ObtenerUsuarioDeCambioAsync(string fullPath, string? oldFullPath, CancellationToken ct)
    {
        string normPath = NormalizarRutaWindows(fullPath);
        string fileName = Path.GetFileName(normPath);
        string? normOld = !string.IsNullOrWhiteSpace(oldFullPath) ? NormalizarRutaWindows(oldFullPath) : null;
        string? oldFileName = !string.IsNullOrWhiteSpace(normOld) ? Path.GetFileName(normOld) : null;

        // 1. Coincidencia desde la caché en memoria (reintentando hasta 3 veces esperando 250ms por latencia de despacho de EventLog)
        for (int intento = 0; intento < 3; intento++)
        {
            var now = DateTime.UtcNow;

            // Coincidencia exacta por ruta completa normalizada en escritores
            if (_recentWriters.TryGetValue(normPath, out var writer) && (now - writer.Timestamp) <= MaxEventAge)
            {
                _logger.LogInformation("[Seguridad 4663] Coincidencia exacta por ruta para '{Path}': Usuario='{User}'", normPath, writer.Usuario);
                return writer.Usuario;
            }

            // Si es renombrado/movido, verificar primero en eliminadores (0x10000 sobre ruta origen) y luego en escritores
            if (!string.IsNullOrEmpty(normOld))
            {
                if (_recentDeleters.TryGetValue(normOld, out var deleterOld) && (now - deleterOld.Timestamp) <= MaxEventAge)
                {
                    _logger.LogInformation("[Seguridad 4663] Coincidencia por ruta origen (DELETE) para '{Path}': Usuario='{User}'", normOld, deleterOld.Usuario);
                    return deleterOld.Usuario;
                }
                if (_recentWriters.TryGetValue(normOld, out var writerOld) && (now - writerOld.Timestamp) <= MaxEventAge)
                {
                    _logger.LogInformation("[Seguridad 4663] Coincidencia por ruta origen (WRITE) para '{Path}': Usuario='{User}'", normOld, writerOld.Usuario);
                    return writerOld.Usuario;
                }
                if (!string.IsNullOrEmpty(oldFileName) &&
                    _recentDeleters.TryGetValue(oldFileName, out var deleterOldName) &&
                    (now - deleterOldName.Timestamp) <= TimeSpan.FromSeconds(45))
                {
                    _logger.LogInformation("[Seguridad 4663] Coincidencia por nombre origen (DELETE) para '{File}': Usuario='{User}'", oldFileName, deleterOldName.Usuario);
                    return deleterOldName.Usuario;
                }
            }

            // Coincidencia por nombre de archivo reciente (< 60 segundos)
            if (!string.IsNullOrEmpty(fileName))
            {
                if (_recentWriters.TryGetValue(fileName, out var writerByName) && (now - writerByName.Timestamp) <= TimeSpan.FromSeconds(60))
                {
                    _logger.LogInformation("[Seguridad 4663] Coincidencia por nombre de archivo reciente ('{File}'): Usuario='{User}'",
                        fileName, writerByName.Usuario);
                    return writerByName.Usuario;
                }

                foreach (var kvp in _recentWriters)
                {
                    if ((now - kvp.Value.Timestamp) <= TimeSpan.FromSeconds(60))
                    {
                        string cachedFileName = Path.GetFileName(kvp.Key);
                        if (string.Equals(fileName, cachedFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("[Seguridad 4663] Coincidencia por nombre en caché ('{File}'): Usuario='{User}'",
                                fileName, kvp.Value.Usuario);
                            return kvp.Value.Usuario;
                        }
                    }
                }
            }

            if (intento < 2)
            {
                await Task.Delay(250, ct);
            }
        }

        // 2. Consulta directa al registro de seguridad de Windows (EventLogReader)
        string? usuarioDirecto = ConsultarUltimoUsuarioEventLog(normPath, normOld, soloEliminacion: false, segundosAtras: 45);
        if (!string.IsNullOrEmpty(usuarioDirecto))
        {
            _logger.LogInformation("[Seguridad 4663] Coincidencia por consulta directa a EventLog para '{Path}': Usuario='{User}'", normPath, usuarioDirecto);
            return usuarioDirecto;
        }

        // 3. Si el Evento 4663/4660 no capturó la identidad, no atribuimos falsamente con NTFS
        _logger.LogWarning("[Seguridad 4663] Sin evento 4663 reciente para '{Path}' (Caché activa: {Count} eventos). Se registrará como '{User}'.",
            normPath, _recentWriters.Count, UsuarioNoIdentificado);
        return UsuarioNoIdentificado;
    }

    private async Task<string> ObtenerUsuarioDeCambioParaEliminacionAsync(string fullPath, CancellationToken ct)
    {
        string normPath = NormalizarRutaWindows(fullPath);
        string fileName = Path.GetFileName(normPath);
        string dirPath = Path.GetDirectoryName(normPath) ?? string.Empty;

        // 1. Reintentar desde la caché en memoria EXCLUSIVA de eliminadores (_recentDeleters)
        // Esto previene al 100% que el creador o modificador del archivo sea confundido con quien lo eliminó
        for (int intento = 0; intento < 3; intento++)
        {
            var now = DateTime.UtcNow;

            // Coincidencia exacta por ruta en eventos con máscara DELETE (0x10000 / 0x40) o Evento 4660
            if (_recentDeleters.TryGetValue(normPath, out var deleter) && (now - deleter.Timestamp) <= MaxDeleteEventAge)
            {
                _logger.LogInformation("[Seguridad 4663/4660] Eliminación exacta capturada para '{Path}': Usuario='{User}'", normPath, deleter.Usuario);
                return deleter.Usuario;
            }

            // Coincidencia por nombre de archivo reciente en eventos DELETE
            if (!string.IsNullOrEmpty(fileName))
            {
                if (_recentDeleters.TryGetValue(fileName, out var deleterByName) && (now - deleterByName.Timestamp) <= MaxDeleteEventAge)
                {
                    _logger.LogInformation("[Seguridad 4663/4660] Eliminación por nombre de archivo ('{File}'): Usuario='{User}'",
                        fileName, deleterByName.Usuario);
                    return deleterByName.Usuario;
                }
            }

            // Coincidencia por DeleteChild (0x40) sobre la carpeta padre en _recentDeleters
            if (!string.IsNullOrEmpty(dirPath) &&
                _recentDeleters.TryGetValue(dirPath, out var parentDeleter) &&
                (now - parentDeleter.Timestamp) <= TimeSpan.FromSeconds(10))
            {
                _logger.LogInformation("[Seguridad 4663] Eliminación capturada por DeleteChild en carpeta '{Dir}': Usuario='{User}'",
                    dirPath, parentDeleter.Usuario);
                return parentDeleter.Usuario;
            }

            if (intento < 2)
            {
                await Task.Delay(300, ct);
            }
        }

        // 2. Consulta directa al registro de seguridad de Windows EXIGIENDO máscara DELETE (0x10000 / 0x40), Evento 4660 o carpeta en <= 5s
        string? usuarioDirecto = ConsultarUltimoUsuarioEventLog(normPath, null, soloEliminacion: true, segundosAtras: 45);
        if (!string.IsNullOrEmpty(usuarioDirecto))
        {
            _logger.LogInformation("[Seguridad 4663/4660] Eliminación capturada por consulta directa a EventLog para '{Path}': Usuario='{User}'", normPath, usuarioDirecto);
            return usuarioDirecto;
        }

        // 3. Respaldo por actividad inmediata en la carpeta contenedora (<= 5 segundos)
        // Si Windows auditó únicamente la modificación del directorio padre al desvincular el archivo
        if (!string.IsNullOrEmpty(dirPath))
        {
            var now = DateTime.UtcNow;
            if (_recentWriters.TryGetValue(dirPath, out var dirWriter) && (now - dirWriter.Timestamp) <= TimeSpan.FromSeconds(5))
            {
                _logger.LogInformation("[Seguridad 4663] Eliminación asociada a modificación inmediata de carpeta '{Dir}': Usuario='{User}'",
                    dirPath, dirWriter.Usuario);
                return dirWriter.Usuario;
            }
        }

        // 4. Respaldo por sesión SMB activa en el servidor para esa ruta local (NetFileEnum / OpenFiles)
        string? usuarioSesionSmb = ConsultarUsuarioSesionSmbActiva(normPath, dirPath);
        if (!string.IsNullOrEmpty(usuarioSesionSmb))
        {
            _logger.LogInformation("[Seguridad SMB] Eliminación atribuida por sesión SMB activa en '{Dir}': Usuario='{User}'",
                dirPath, usuarioSesionSmb);
            return usuarioSesionSmb;
        }

        // 5. Si el Evento 4663/4660 no capturó la eliminación real, registrar diagnóstico de los últimos eventos en Security
        DiagnosticarUltimosEventosSeguridad(normPath);
        _logger.LogWarning("[Seguridad 4663] Eliminación no capturada por 4663/4660 para '{Path}'. Se registrará como '{User}'.",
            normPath, UsuarioNoIdentificado);
        return UsuarioNoIdentificado;
    }

    private string? ConsultarUltimoUsuarioEventLog(string fullPath, string? oldFullPath, bool soloEliminacion = false, int segundosAtras = 45)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            string normPath = NormalizarRutaWindows(fullPath);
            string fileName = Path.GetFileName(normPath);
            string? normOld = !string.IsNullOrWhiteSpace(oldFullPath) ? NormalizarRutaWindows(oldFullPath) : null;
            string? oldFileName = !string.IsNullOrWhiteSpace(normOld) ? Path.GetFileName(normOld) : null;
            string dirPath = Path.GetDirectoryName(normPath) ?? string.Empty;

            // Consulta XPath estandarizada idéntica a wevtutil en orden inverso (más reciente primero)
            string queryXml = "*[System[(EventID=4663 or EventID=4660 or EventID=4656 or EventID=5145)]]";
            var query = new EventLogQuery("Security", PathType.LogName, queryXml)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            EventRecord? record;
            int inspectCount = 0;
            string? usuarioCarpetaPadre = null;
            string? usuarioEvento4660Reciente = null;

            // Como leemos en reversa (del más reciente hacia atrás), un evento 4660 aparece ANTES que su 4663/4656 previo.
            // Guardamos los HandleId de eventos 4660 vistos para vincularlos en cuanto aparezca el 4663/4656 que abrió ese handle.
            var handlesEliminadosRecientes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while ((record = reader.ReadEvent()) != null && inspectCount++ < 1500)
            {
                using (record)
                {
                    double ageSeconds = 0;
                    if (record.TimeCreated.HasValue)
                    {
                        ageSeconds = (DateTime.UtcNow - record.TimeCreated.Value.ToUniversalTime()).TotalSeconds;
                        // Al leer en orden inverso, si ya superamos la ventana de tiempo hacia atrás, podemos detener la búsqueda
                        if (ageSeconds > segundosAtras)
                        {
                            break;
                        }
                    }

                    int eventId = record.Id;
                    string xml = record.ToXml();
                    if (string.IsNullOrWhiteSpace(xml)) continue;

                    var doc = XDocument.Parse(xml);
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var eventData = doc.Root?.Element(ns + "EventData");
                    if (eventData == null) continue;

                    string? userName = null;
                    string? domainName = null;
                    string? objectName = null;
                    string? accessMaskStr = null;
                    string? handleId = null;
                    string? shareLocalPath = null;
                    string? relativeTargetName = null;

                    foreach (var data in eventData.Elements(ns + "Data"))
                    {
                        string? name = (string?)data.Attribute("Name");
                        if (name == null) continue;
                        if (name == "SubjectUserName") userName = data.Value;
                        else if (name == "SubjectDomainName") domainName = data.Value;
                        else if (name == "ObjectName") objectName = data.Value;
                        else if (name == "AccessMask") accessMaskStr = data.Value;
                        else if (name == "HandleId") handleId = data.Value?.Trim();
                        else if (name == "ShareLocalPath") shareLocalPath = data.Value?.Trim();
                        else if (name == "RelativeTargetName") relativeTargetName = data.Value?.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(objectName) && !string.IsNullOrWhiteSpace(shareLocalPath))
                    {
                        objectName = !string.IsNullOrWhiteSpace(relativeTargetName) && relativeTargetName != @"\"
                            ? Path.Combine(shareLocalPath, relativeTargetName)
                            : shareLocalPath;
                    }

                    if (string.IsNullOrWhiteSpace(userName) || EsCuentaSistemaOMaquina(userName))
                    {
                        continue;
                    }

                    string usuarioCompleto = FormatearUsuarioDominio(domainName, userName);
                    string normHandle = NormalizarHandleId(handleId);

                    // Si es Evento 4660 (Object Deleted), registrar su HandleId o resolverlo desde el mapa en memoria
                    if (eventId == 4660)
                    {
                        if (soloEliminacion && usuarioEvento4660Reciente == null && Math.Abs(ageSeconds) <= 8.0)
                        {
                            usuarioEvento4660Reciente = usuarioCompleto;
                        }

                        if (!string.IsNullOrEmpty(normHandle))
                        {
                            handlesEliminadosRecientes[normHandle] = usuarioCompleto;

                            if (_handleToObjectMap.TryGetValue(normHandle, out var handleCtx))
                            {
                                string fHandle = Path.GetFileName(handleCtx.RutaNormalizada);
                                if (string.Equals(handleCtx.RutaNormalizada, normPath, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(fileName) && string.Equals(fHandle, fileName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    RegistrarEnCacheDeleters(handleCtx.RutaNormalizada, new RecentWriterInfo
                                    {
                                        Usuario = usuarioCompleto,
                                        Timestamp = DateTime.UtcNow,
                                        AccessMask = 0x10000
                                    });
                                    return usuarioCompleto;
                                }
                            }
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(objectName)) continue;

                    uint accessMask = ParsearAccessMask(accessMaskStr);
                    string rutaEvento = NormalizarRutaWindows(objectName);
                    string fNameEvento = Path.GetFileName(rutaEvento);

                    // Si este 4663/4656 comparte HandleId con un 4660 visto hace instantes, entonces este archivo fue eliminado
                    bool fueEliminadoPor4660 = !string.IsNullOrEmpty(normHandle) && handlesEliminadosRecientes.ContainsKey(normHandle);

                    // 1. Guardar como respaldo si es la carpeta contenedora en los últimos 5 segundos
                    if (usuarioCarpetaPadre == null && !string.IsNullOrEmpty(dirPath) &&
                        string.Equals(rutaEvento, dirPath, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(ageSeconds) <= 5.0 &&
                        ((accessMask & (DELETE_MASK | WRITE_MASK)) != 0))
                    {
                        usuarioCarpetaPadre = usuarioCompleto;
                    }

                    // 2. Comprobar máscaras según tipo de búsqueda
                    bool tieneWriteData = (accessMask & (0x2 | 0x4)) != 0;
                    bool esDeleteEvento = eventId == 4663
                        ? (accessMask & DELETE_MASK) != 0
                        : (accessMask & DELETE_MASK) != 0 && !tieneWriteData;

                    if (soloEliminacion)
                    {
                        if (!fueEliminadoPor4660 && !esDeleteEvento) continue;
                    }
                    else
                    {
                        if (!fueEliminadoPor4660 && accessMask != 0 && (accessMask & MUTATION_MASK) == 0) continue;
                    }

                    bool coincideRuta = string.Equals(rutaEvento, normPath, StringComparison.OrdinalIgnoreCase);
                    bool coincideNombre = !string.IsNullOrEmpty(fileName) && string.Equals(fNameEvento, fileName, StringComparison.OrdinalIgnoreCase);
                    bool coincideOrigen = !string.IsNullOrEmpty(normOld) && (string.Equals(rutaEvento, normOld, StringComparison.OrdinalIgnoreCase) ||
                                          (!string.IsNullOrEmpty(oldFileName) && string.Equals(fNameEvento, oldFileName, StringComparison.OrdinalIgnoreCase)));

                    if (coincideRuta || coincideNombre || coincideOrigen)
                    {
                        var info = new RecentWriterInfo
                        {
                            Usuario = usuarioCompleto,
                            Timestamp = DateTime.UtcNow,
                            AccessMask = fueEliminadoPor4660 ? 0x10000 : accessMask
                        };

                        if (soloEliminacion || esDeleteEvento || fueEliminadoPor4660)
                        {
                            RegistrarEnCacheDeleters(rutaEvento, info);
                        }
                        if (!soloEliminacion && (accessMask & WRITE_MASK) != 0)
                        {
                            RegistrarEnCacheWriters(rutaEvento, info);
                        }

                        return usuarioCompleto;
                    }
                }
            }

            if (!string.IsNullOrEmpty(usuarioCarpetaPadre))
            {
                return usuarioCarpetaPadre;
            }

            if (soloEliminacion && !string.IsNullOrEmpty(usuarioEvento4660Reciente))
            {
                return usuarioEvento4660Reciente;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Seguridad 4663] Error en consulta directa a EventLog.");
        }

        return null;
    }

    private string? ConsultarUsuarioSesionSmbActiva(string normPath, string dirPath)
    {
        if (!OperatingSystem.IsWindows()) return null;

        IntPtr bufPtr = IntPtr.Zero;
        try
        {
            string basePath = !string.IsNullOrWhiteSpace(dirPath) ? dirPath : normPath;
            int status = NetFileEnum(null, basePath, null, 3, out bufPtr, -1, out int entriesRead, out _, IntPtr.Zero);
            if (status == 0 && entriesRead > 0 && bufPtr != IntPtr.Zero)
            {
                int structSize = Marshal.SizeOf<FILE_INFO_3>();
                var usuariosActivos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < entriesRead; i++)
                {
                    IntPtr itemPtr = IntPtr.Add(bufPtr, i * structSize);
                    var info = Marshal.PtrToStructure<FILE_INFO_3>(itemPtr);
                    if (!string.IsNullOrWhiteSpace(info.fi3_username) && !EsCuentaSistemaOMaquina(info.fi3_username))
                    {
                        usuariosActivos.Add(info.fi3_username.Trim());
                    }
                }

                // Si exactamente un usuario tiene la carpeta o recurso abierto por SMB en este instante, atribuirlo
                if (usuariosActivos.Count == 1)
                {
                    return usuariosActivos.First();
                }
            }
        }
        catch
        {
            // Ignorar si no tiene privilegios de NetFileEnum
        }
        finally
        {
            if (bufPtr != IntPtr.Zero)
            {
                NetApiBufferFree(bufPtr);
            }
        }

        return null;
    }

    private void DiagnosticarUltimosEventosSeguridad(string normPath)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            string queryXml = "*[System[(EventID=4663 or EventID=4660 or EventID=4656 or EventID=5145)]]";
            var query = new EventLogQuery("Security", PathType.LogName, queryXml)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            var resumen = new List<string>();
            EventRecord? record;
            int count = 0;

            while ((record = reader.ReadEvent()) != null && count++ < 15)
            {
                using (record)
                {
                    if (record.TimeCreated.HasValue &&
                        (DateTime.UtcNow - record.TimeCreated.Value.ToUniversalTime()).TotalSeconds > 20)
                    {
                        break;
                    }

                    var doc = XDocument.Parse(record.ToXml());
                    var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                    var eventData = doc.Root?.Element(ns + "EventData");
                    if (eventData == null) continue;

                    string? user = eventData.Elements(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "SubjectUserName")?.Value;
                    string? obj = eventData.Elements(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "ObjectName")?.Value
                                  ?? eventData.Elements(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "RelativeTargetName")?.Value;
                    string? mask = eventData.Elements(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "AccessMask")?.Value;
                    string? handle = eventData.Elements(ns + "Data").FirstOrDefault(d => (string?)d.Attribute("Name") == "HandleId")?.Value;

                    if (!string.IsNullOrEmpty(user) && !EsCuentaSistemaOMaquina(user))
                    {
                        resumen.Add($"[ID={record.Id}, User={user}, Mask={mask ?? "-"}, Handle={handle ?? "-"}, Obj={obj ?? "-"}]");
                    }
                    if (resumen.Count >= 5) break;
                }
            }

            if (resumen.Count > 0)
            {
                _logger.LogInformation("[Diagnóstico Security] Últimos eventos en los últimos 20s al buscar '{Path}': {Eventos}",
                    normPath, string.Join(" | ", resumen));
            }
            else
            {
                _logger.LogWarning("[Diagnóstico Security] No se encontró NINGÚN evento de usuario (4663/4660/4656/5145) en los últimos 20s al eliminar '{Path}'. Verifique SACL ('Delete'/'DeleteSubdirectoriesAndFiles') y 'auditpol' para 'Handle Manipulation' y 'Detailed File Share'.",
                    normPath);
            }
        }
        catch
        {
            // Ignorar errores de diagnóstico
        }
    }

    private void LimpiarEscritoresExpirados()
    {
        var now = DateTime.UtcNow;

        var cutoffWriters = now - MaxEventAge;
        foreach (var kvp in _recentWriters)
        {
            if (kvp.Value.Timestamp < cutoffWriters)
            {
                _recentWriters.TryRemove(kvp.Key, out _);
            }
        }

        var cutoffDeleters = now - MaxDeleteEventAge;
        foreach (var kvp in _recentDeleters)
        {
            if (kvp.Value.Timestamp < cutoffDeleters)
            {
                _recentDeleters.TryRemove(kvp.Key, out _);
            }
        }

        var cutoffHandles = now - MaxHandleAge;
        foreach (var kvp in _handleToObjectMap)
        {
            if (kvp.Value.Timestamp < cutoffHandles)
            {
                _handleToObjectMap.TryRemove(kvp.Key, out _);
            }
        }

        foreach (var kvp in _recentSecurityLogThrottle)
        {
            if ((now - kvp.Value).TotalSeconds > 10)
            {
                _recentSecurityLogThrottle.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void AsegurarSaclAuditoriaCarpeta(string rutaDir)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(rutaDir)) return;

        try
        {
            var dInfo = new DirectoryInfo(rutaDir);
            var dSec = dInfo.GetAccessControl(AccessControlSections.Audit);

            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var requiredRights = FileSystemRights.WriteData |
                                 FileSystemRights.AppendData |
                                 FileSystemRights.Delete |
                                 FileSystemRights.DeleteSubdirectoriesAndFiles;

            bool yaExisteCompleta = false;

            var rules = dSec.GetAuditRules(true, false, typeof(SecurityIdentifier));
            foreach (FileSystemAuditRule r in rules)
            {
                if (r.IdentityReference.Equals(everyone) &&
                    r.AuditFlags.HasFlag(AuditFlags.Success) &&
                    (r.FileSystemRights & requiredRights) == requiredRights)
                {
                    yaExisteCompleta = true;
                    break;
                }
            }

            if (!yaExisteCompleta)
            {
                var auditRule = new FileSystemAuditRule(
                    everyone,
                    requiredRights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AuditFlags.Success);

                dSec.AddAuditRule(auditRule);
                dInfo.SetAccessControl(dSec);
                _logger.LogInformation("[Seguridad 4663] Regla SACL de auditoría configurada automáticamente en '{Dir}'", rutaDir);
            }
        }
        catch (PrivilegeNotHeldException)
        {
            _logger.LogWarning("[Seguridad 4663] Privilegio 'SeSecurityPrivilege' no presente para configurar SACL en '{Dir}'. Asegúrese de ejecutar como Administrador.", rutaDir);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[Seguridad 4663] Verificación SACL en '{Dir}': {Msg}", rutaDir, ex.Message);
        }
    }

    private async Task RecargarProyectosYVigilantesAsync(CancellationToken ct)
    {
        var proyectosDto = await _proyectoRepository.ListarAsync();
        var listaMapeada = new List<MonitoredProject>();

        var mapeos = _config.GetSection("AgentSettings:MapeoRutas").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var p in proyectosDto)
        {
            if (string.IsNullOrWhiteSpace(p.RutaArchivos)) continue;

            string rutaLocal = ResolverRutaLocal(p.RutaArchivos, mapeos);
            if (string.IsNullOrWhiteSpace(rutaLocal))
            {
                _logger.LogInformation("[Mapeo] Proyecto {Id} ('{Titulo}') omitido en este servidor: la ruta '{RutaRed}' no pertenece a los discos locales configurados o resuelve a otro servidor.",
                    p.Id, p.Titulo, p.RutaArchivos);
                continue;
            }

            if (!Directory.Exists(rutaLocal))
            {
                _logger.LogWarning("[Mapeo] Proyecto {Id} ('{Titulo}'): la ruta local resuelta '{RutaLocal}' no existe físicamente en el disco. Se reintentará en el próximo ciclo.",
                    p.Id, p.Titulo, rutaLocal);
                continue;
            }

            listaMapeada.Add(new MonitoredProject
            {
                IdProyecto = p.Id,
                Titulo = p.Titulo,
                RutaRed = p.RutaArchivos,
                RutaLocal = rutaLocal.TrimEnd('\\', '/')
            });
        }

        _activeProjects = listaMapeada;

        // Evitar crear FileSystemWatchers redundantes para subdirectorios que ya están cubiertos por una carpeta raíz padre
        var todasRutas = _activeProjects
            .Select(p => p.RutaLocal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.Length)
            .ToList();

        var carpetasRaiz = new List<string>();
        foreach (var ruta in todasRutas)
        {
            bool cubiertaPorPadre = carpetasRaiz.Any(padre =>
                ruta.StartsWith(padre + @"\", StringComparison.OrdinalIgnoreCase));

            if (!cubiertaPorPadre)
            {
                carpetasRaiz.Add(ruta);
            }
        }

        lock (_watchers)
        {
            foreach (var rutaDir in carpetasRaiz)
            {
                AsegurarSaclAuditoriaCarpeta(rutaDir);

                if (!_watchedDirectories.Contains(rutaDir) && Directory.Exists(rutaDir))
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(rutaDir)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                            InternalBufferSize = 65536
                        };

                        watcher.Created += OnFileSystemEvent;
                        watcher.Changed += OnFileSystemEvent;
                        watcher.Deleted += OnFileSystemEvent;
                        watcher.Renamed += OnFileSystemRenamed;
                        watcher.Error += (s, e) => _logger.LogWarning("[Watcher] Error en vigilante para '{Dir}': {Msg}", rutaDir, e.GetException()?.Message);

                        watcher.EnableRaisingEvents = true;
                        _watchers.Add(watcher);
                        _watchedDirectories.Add(rutaDir);

                        _logger.LogInformation("[Vigilante] Monitoreando carpeta física local: {Dir}", rutaDir);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Vigilante] Error al iniciar FileSystemWatcher para: {Dir}", rutaDir);
                    }
                }
            }
        }

        // Opción 2: Barrido inicial condicional (solo proyectos con COUNT == 0 en auditoría de archivos)
        _ = Task.Run(async () => await VerificarYEjecutarBarridosInicialesAsync(listaMapeada, ct), ct);
    }

    public static string ResolverRutaLocal(string rutaRed, Dictionary<string, string> mapeos)
    {
        if (string.IsNullOrWhiteSpace(rutaRed)) return string.Empty;
        string norm = rutaRed.Trim().Replace('/', '\\').TrimEnd('\\');

        // Ordenar por prefijo más específico (más largo) primero
        foreach (var kvp in mapeos.OrderByDescending(k => k.Key.Length))
        {
            string prefijoRed = kvp.Key.Trim().Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrEmpty(prefijoRed)) continue;

            if (norm.Equals(prefijoRed, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value.Trim().Replace('/', '\\').TrimEnd('\\');
            }

            if (norm.StartsWith(prefijoRed + @"\", StringComparison.OrdinalIgnoreCase))
            {
                string relativo = norm.Substring(prefijoRed.Length).TrimStart('\\');
                string baseLocal = kvp.Value.Trim().Replace('/', '\\').TrimEnd('\\');
                return string.IsNullOrEmpty(relativo) ? baseLocal : Path.Combine(baseLocal, relativo);
            }
        }

        if (norm.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(norm))
        {
            return norm;
        }

        return string.Empty;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        EnqueueEvent(e.FullPath, e.ChangeType);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        string normPath = NormalizarRutaWindows(e.FullPath);
        string oldNormPath = NormalizarRutaWindows(e.OldFullPath);
        if (string.IsNullOrEmpty(normPath) || string.IsNullOrEmpty(oldNormPath)) return;

        // Si el archivo origen acababa de ser creado en el mismo lote de debounce (ej. "Nuevo documento de texto.txt" -> "NombreFinal.txt"),
        // preservar su naturaleza de CREACION o conservar el primer OldFullPath en cadena de renombrados.
        if (_pendingEvents.TryRemove(oldNormPath, out var prevEvt))
        {
            if (prevEvt.ChangeType == WatcherChangeTypes.Created && string.IsNullOrEmpty(prevEvt.OldFullPath))
            {
                _pendingEvents[normPath] = new DebouncedFileEvent
                {
                    FullPath = normPath,
                    OldFullPath = oldNormPath,
                    ChangeType = WatcherChangeTypes.Created,
                    Timestamp = DateTime.UtcNow
                };
                return;
            }

            if (prevEvt.ChangeType == WatcherChangeTypes.Renamed && !string.IsNullOrEmpty(prevEvt.OldFullPath))
            {
                oldNormPath = prevEvt.OldFullPath;
            }
        }

        _pendingEvents[normPath] = new DebouncedFileEvent
        {
            FullPath = normPath,
            OldFullPath = oldNormPath,
            ChangeType = WatcherChangeTypes.Renamed,
            Timestamp = DateTime.UtcNow
        };
    }

    private void EnqueueEvent(string fullPath, WatcherChangeTypes changeType)
    {
        string normPath = NormalizarRutaWindows(fullPath);
        if (string.IsNullOrEmpty(normPath)) return;

        _pendingEvents.AddOrUpdate(
            normPath,
            _ => new DebouncedFileEvent
            {
                FullPath = normPath,
                ChangeType = changeType,
                Timestamp = DateTime.UtcNow
            },
            (_, existing) =>
            {
                // Si el archivo acaba de ser renombrado o creado y llega un evento Changed secundario (metadatos/atributos NTFS),
                // NO sobrescribir el evento Renamed/Created ni perder OldFullPath.
                if (changeType == WatcherChangeTypes.Changed &&
                    (existing.ChangeType == WatcherChangeTypes.Renamed || existing.ChangeType == WatcherChangeTypes.Created))
                {
                    existing.Timestamp = DateTime.UtcNow;
                    return existing;
                }

                existing.ChangeType = changeType;
                existing.Timestamp = DateTime.UtcNow;
                if (changeType == WatcherChangeTypes.Deleted)
                {
                    existing.OldFullPath = null;
                }
                return existing;
            });
    }

    private async Task ProcesarEventosPendientesAsync(int debounceMs, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var toProcess = new List<DebouncedFileEvent>();

        foreach (var kvp in _pendingEvents)
        {
            if ((now - kvp.Value.Timestamp).TotalMilliseconds >= debounceMs)
            {
                if (_pendingEvents.TryRemove(kvp.Key, out var evt))
                {
                    toProcess.Add(evt);
                }
            }
        }

        foreach (var evt in toProcess)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ProcesarEventoArchivoAsync(evt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Procesador] Error registrando auditoría para '{Path}': {Msg}", evt.FullPath, ex.Message);
            }
        }
    }

    private static bool EsRutaExcluidaOSistema(string fullPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return true;

        // Omitir archivos temporales de Office, bloqueos o sondas de permisos
        if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(".probe_", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Omitir papelera de reciclaje y carpetas internas del sistema o de FileGDB (.gdb)
        if (fullPath.Contains(@"\$RECYCLE.BIN\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains(@"\System Volume Information\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase) ||
            fullPath.Contains(@".gdb\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task ProcesarEventoArchivoAsync(DebouncedFileEvent evt, CancellationToken ct)
    {
        string fullPath = evt.FullPath;
        string fileName = Path.GetFileName(fullPath);
        if (EsRutaExcluidaOSistema(fullPath, fileName))
        {
            return;
        }

        var match = _activeProjects
            .Where(p => fullPath.Equals(p.RutaLocal, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.StartsWith(p.RutaLocal + @"\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.RutaLocal.Length)
            .FirstOrDefault();

        if (match == null) return;

        string relativePath = fullPath.Substring(match.RutaLocal.Length).TrimStart('/', '\\').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        bool existeEnDisco = File.Exists(fullPath);
        bool esDirectorio = Directory.Exists(fullPath);

        if (esDirectorio && !existeEnDisco) return;

        // ══════════════════════════════════════════════════════════════
        // CASO 1: RENOMBRAR O MOVER ARCHIVO NATIVO (EVENTO RENAMED)
        // ══════════════════════════════════════════════════════════════
        if (evt.ChangeType == WatcherChangeTypes.Renamed && !string.IsNullOrWhiteSpace(evt.OldFullPath))
        {
            if (existeEnDisco)
            {
                var fi = new FileInfo(fullPath);
                string usuario = await ObtenerUsuarioDeCambioAsync(fullPath, evt.OldFullPath, ct);
                string machine = Environment.MachineName;

                string oldFullPath = evt.OldFullPath;
                string oldFileName = Path.GetFileName(oldFullPath);
                string oldRelativePath = oldFullPath.StartsWith(match.RutaLocal + @"\", StringComparison.OrdinalIgnoreCase)
                    ? oldFullPath.Substring(match.RutaLocal.Length).TrimStart('/', '\\').Replace('\\', '/')
                    : oldFileName;

                var estadoOrigen = await _proyectoRepository.ObtenerUltimoEstadoArchivoAsync(match.IdProyecto, oldRelativePath, ct);

                // Reasignar el historial previo del archivo antiguo a la nueva ruta relativa para mantener
                // intacta la línea de tiempo de hasta 5 cambios al consultar el archivo en Geomatica.Desktop
                if (estadoOrigen != null)
                {
                    await _proyectoRepository.ReasignarRutaAuditoriaArchivoAsync(
                        idProyecto: match.IdProyecto,
                        rutaRelativaAntigua: oldRelativePath,
                        rutaRelativaNueva: relativePath,
                        nombreArchivoNuevo: fileName,
                        ct: ct);
                }

                string oldDir = Path.GetDirectoryName(oldFullPath) ?? "";
                string newDir = Path.GetDirectoryName(fullPath) ?? "";

                bool cambioNombre = !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase);
                bool cambioCarpeta = !string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase);

                string accion;
                string desc;

                if (cambioNombre && cambioCarpeta)
                {
                    accion = "RENOMBRAR";
                    desc = $"Archivo movido y renombrado desde '{oldRelativePath}'";
                }
                else if (cambioNombre)
                {
                    accion = "RENOMBRAR";
                    desc = $"Archivo renombrado desde '{oldFileName}'";
                }
                else
                {
                    accion = "MOVER";
                    desc = $"Archivo movido desde '{oldRelativePath}'";
                }

                await _proyectoRepository.RegistrarAuditoriaArchivoAsync(
                    idProyecto: match.IdProyecto,
                    rutaRelativa: relativePath,
                    nombreArchivo: fileName,
                    accion: accion,
                    detalles: desc,
                    tamanoBytes: fi.Length,
                    usuario: usuario,
                    equipo: machine,
                    fechaArchivo: fi.LastWriteTimeUtc,
                    ct: ct);

                _logger.LogInformation("[{Accion}] Proyecto {Id} ({Titulo}): '{Rel}' ({Tam}) por {User}. Detalle: {Detalle}",
                    accion, match.IdProyecto, match.Titulo, relativePath, FormatearTamano(fi.Length), usuario, desc);

                return;
            }
        }

        var estadoPrevio = await _proyectoRepository.ObtenerUltimoEstadoArchivoAsync(match.IdProyecto, relativePath, ct);

        if (existeEnDisco)
        {
            var fi = new FileInfo(fullPath);
            if (fi.Length == 0)
            {
                await Task.Delay(150, ct);
                fi.Refresh();
            }
            string usuario = await ObtenerUsuarioDeCambioAsync(fullPath, evt.OldFullPath, ct);
            string machine = Environment.MachineName;

            if (estadoPrevio == null || string.Equals(estadoPrevio.Accion, "ELIMINAR", StringComparison.OrdinalIgnoreCase))
            {
                string desc = $"Archivo detectado en el servidor por agente de fondo (Tamaño: {FormatearTamano(fi.Length)})";
                await _proyectoRepository.RegistrarAuditoriaArchivoAsync(
                    idProyecto: match.IdProyecto,
                    rutaRelativa: relativePath,
                    nombreArchivo: fileName,
                    accion: "CREACION",
                    detalles: desc,
                    tamanoBytes: fi.Length,
                    usuario: usuario,
                    equipo: machine,
                    fechaArchivo: fi.LastWriteTimeUtc,
                    ct: ct);

                _logger.LogInformation("[CREACION] Proyecto {Id} ({Titulo}): '{Rel}' ({Tam}) por {User}",
                    match.IdProyecto, match.Titulo, relativePath, FormatearTamano(fi.Length), usuario);
            }
            else
            {
                bool tamanoCambio = estadoPrevio.TamanoBytes.HasValue && estadoPrevio.TamanoBytes.Value != fi.Length;
                bool fechaCambio = estadoPrevio.FechaArchivo.HasValue &&
                    Math.Abs((fi.LastWriteTimeUtc - estadoPrevio.FechaArchivo.Value).TotalSeconds) > 2.0;

                if (tamanoCambio || fechaCambio)
                {
                    string tamanoPrevioStr = estadoPrevio.TamanoBytes.HasValue ? FormatearTamano(estadoPrevio.TamanoBytes.Value) : "desconocido";
                    string tamanoNuevoStr = FormatearTamano(fi.Length);
                    string desc = tamanoCambio
                        ? $"Archivo modificado en el servidor (Tamaño: {tamanoPrevioStr} ➔ {tamanoNuevoStr}, Modificado: {fi.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss})"
                        : $"Archivo modificado en el servidor (Modificado: {fi.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss})";

                    await _proyectoRepository.RegistrarAuditoriaArchivoAsync(
                        idProyecto: match.IdProyecto,
                        rutaRelativa: relativePath,
                        nombreArchivo: fileName,
                        accion: "MODIFICACION",
                        detalles: desc,
                        tamanoBytes: fi.Length,
                        usuario: usuario,
                        equipo: machine,
                        fechaArchivo: fi.LastWriteTimeUtc,
                        ct: ct);

                    _logger.LogInformation("[MODIFICACION] Proyecto {Id} ({Titulo}): '{Rel}' ({Tam}) por {User}",
                        match.IdProyecto, match.Titulo, relativePath, tamanoNuevoStr, usuario);
                }
            }
        }
        else
        {
            // Registrar eliminación si:
            // 1. No había estado previo en la BD (archivo creado antes de iniciar el agente o sin historial).
            // 2. O si el último estado registrado NO era ya "ELIMINAR" (evita duplicar eventos seguidos de eliminación).
            if (estadoPrevio == null || !string.Equals(estadoPrevio.Accion, "ELIMINAR", StringComparison.OrdinalIgnoreCase))
            {
                string desc = estadoPrevio == null
                    ? "Archivo (sin historial previo en BD) eliminado del servidor"
                    : "Archivo eliminado del servidor";
                string usuario = await ObtenerUsuarioDeCambioParaEliminacionAsync(fullPath, ct);
                string machine = Environment.MachineName;

                await _proyectoRepository.RegistrarAuditoriaArchivoAsync(
                    idProyecto: match.IdProyecto,
                    rutaRelativa: relativePath,
                    nombreArchivo: fileName,
                    accion: "ELIMINAR",
                    detalles: desc,
                    tamanoBytes: estadoPrevio?.TamanoBytes ?? 0,
                    usuario: usuario,
                    equipo: machine,
                    fechaArchivo: null,
                    ct: ct);

                _logger.LogInformation("[ELIMINAR] Proyecto {Id} ({Titulo}): '{Rel}' por {User}. Detalle: {Detalle}",
                    match.IdProyecto, match.Titulo, relativePath, usuario, desc);
            }
        }
    }

    private static string FormatearTamano(long bytes)
    {
        if (bytes < 0) return "-";
        double b = bytes;
        if (b < 1024) return $"{b:N0} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:F1} KB";
        if (b < 1024 * 1024 * 1024) return $"{b / (1024.0 * 1024.0):F2} MB";
        return $"{b / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private void LimpiarVigilantes()
    {
        if (_securityWatcher != null)
        {
            try
            {
                _securityWatcher.Enabled = false;
                _securityWatcher.Dispose();
            }
            catch { }
            _securityWatcher = null;
        }

        lock (_watchers)
        {
            foreach (var w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch { }
            }
            _watchers.Clear();
            _watchedDirectories.Clear();
        }
    }

    private sealed class MonitoredProject
    {
        public int IdProyecto { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string RutaRed { get; set; } = string.Empty;
        public string RutaLocal { get; set; } = string.Empty;
    }

    private sealed class DebouncedFileEvent
    {
        public string FullPath { get; set; } = string.Empty;
        public string? OldFullPath { get; set; }
        public WatcherChangeTypes ChangeType { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private sealed class RecentWriterInfo
    {
        public string Usuario { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public uint AccessMask { get; set; }
    }

    private sealed class HandleContextInfo
    {
        public string RutaNormalizada { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    private async Task VerificarYEjecutarBarridosInicialesAsync(List<MonitoredProject> proyectos, CancellationToken ct)
    {
        foreach (var p in proyectos)
        {
            if (ct.IsCancellationRequested) break;

            // Evitar re-verificar proyectos ya examinados en esta sesión
            if (_proyectosVerificadosParaBarrido.ContainsKey(p.IdProyecto)) continue;

            try
            {
                // Opción 2: Solo si COUNT == 0 en geovisor.auditoria_archivo
                bool tieneAuditoria = await _proyectoRepository.TieneAuditoriaArchivoAsync(p.IdProyecto, ct);
                _proyectosVerificadosParaBarrido.TryAdd(p.IdProyecto, true);

                if (!tieneAuditoria)
                {
                    _logger.LogInformation("[Línea Base] Proyecto {Id} ('{Titulo}') no cuenta con historial de archivos. Iniciando barrido inicial en segundo plano...",
                        p.IdProyecto, p.Titulo);

                    await EjecutarBarridoInicialProyectoAsync(p, ct);
                }
                else
                {
                    _logger.LogDebug("[Línea Base] Proyecto {Id} ('{Titulo}') ya cuenta con registros de auditoría. Se omite escaneo de disco.",
                        p.IdProyecto, p.Titulo);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Línea Base] Error al verificar/ejecutar barrido inicial para proyecto {Id} ('{Titulo}').",
                    p.IdProyecto, p.Titulo);
            }
        }
    }

    private async Task EjecutarBarridoInicialProyectoAsync(MonitoredProject p, CancellationToken ct)
    {
        if (!Directory.Exists(p.RutaLocal)) return;

        int count = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var archivos = Directory.EnumerateFiles(p.RutaLocal, "*", SearchOption.AllDirectories);
            string machine = Environment.MachineName;

            foreach (var filePath in archivos)
            {
                if (ct.IsCancellationRequested) break;

                string fileName = Path.GetFileName(filePath);
                if (EsRutaExcluidaOSistema(filePath, fileName)) continue;

                string relativePath = filePath.Substring(p.RutaLocal.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                try
                {
                    var fi = new FileInfo(filePath);
                    string desc = $"Inventario inicial de línea base (Tamaño: {FormatearTamano(fi.Length)})";

                    await _proyectoRepository.RegistrarAuditoriaArchivoAsync(
                        idProyecto: p.IdProyecto,
                        rutaRelativa: relativePath,
                        nombreArchivo: fileName,
                        accion: "CREACION",
                        detalles: desc,
                        tamanoBytes: fi.Length,
                        usuario: "Sistema (Línea Base)",
                        equipo: machine,
                        fechaArchivo: fi.LastWriteTimeUtc,
                        ct: ct);

                    count++;
                }
                catch (Exception exFile)
                {
                    _logger.LogDebug(exFile, "[Línea Base] No se pudo registrar archivo en barrido inicial: {Path}", filePath);
                }
            }

            sw.Stop();
            _logger.LogInformation("[Línea Base] Barrido inicial finalizado para proyecto {Id} ('{Titulo}'): {Count} archivos registrados en {Ms} ms.",
                p.IdProyecto, p.Titulo, count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Línea Base] Error durante el barrido inicial del proyecto {Id} en '{Dir}'.", p.IdProyecto, p.RutaLocal);
        }
    }

}
