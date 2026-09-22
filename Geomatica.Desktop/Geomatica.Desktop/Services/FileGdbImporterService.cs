using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Data;
using Geomatica.Desktop.Models;
using Microsoft.Win32;

namespace Geomatica.Desktop.Services
{
    public record GdbImportResult(
        bool Success,
        string? GeoPackagePath,
        IReadOnlyList<string> FeatureClasses,
        string? MensajeError,
        bool FromCache
    );

    public interface IFileGdbImporterService
    {
        bool IsArcPyAvailable { get; }
        string? PythonExecutablePath { get; }
        Task<GdbImportResult> ImportarGdbAsync(string gdbPath, CancellationToken ct = default);
        Task<IReadOnlyList<GdbCapaInfo>> ObtenerCapasGdbAsync(string gdbPath, CancellationToken ct = default);
        Task<IReadOnlyList<AdjuntoFotoInfo>> ObtenerAdjuntosElementoAsync(string gdbPath, string globalId, CancellationToken ct = default);
        string ObtenerRutaCache(string gdbPath);
        void LimpiarCache();
    }

    public class FileGdbImporterService : IFileGdbImporterService
    {
        private readonly string? _pythonExecutablePath;
        private readonly string _cacheDirectory;

        public bool IsArcPyAvailable => !string.IsNullOrEmpty(_pythonExecutablePath) && File.Exists(_pythonExecutablePath);
        public string? PythonExecutablePath => _pythonExecutablePath;

        public FileGdbImporterService(string? customPythonPath = null, string? customCacheDirectory = null)
        {
            _pythonExecutablePath = customPythonPath ?? DetectarPythonArcGisPro();
            _cacheDirectory = customCacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Geomatica",
                "GdbCache"
            );

            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error creando directorio de caché '{_cacheDirectory}': {ex.Message}");
            }
        }

        public string ObtenerRutaCache(string gdbPath)
        {
            if (string.IsNullOrWhiteSpace(gdbPath))
                throw new ArgumentException("La ruta de la GDB no puede ser nula ni vacía.", nameof(gdbPath));

            string nombreGdb = Path.GetFileNameWithoutExtension(gdbPath);
            if (string.IsNullOrWhiteSpace(nombreGdb))
                nombreGdb = "gdb_dataset";

            // Sanitizar nombre para el archivo de caché
            var sbNombre = new StringBuilder();
            foreach (char c in nombreGdb)
            {
                sbNombre.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            }
            string nombreLimpio = sbNombre.ToString();

            // Calcular huella digital basada en ruta, tamaño acumulado y fecha de modificación de los archivos de datos
            long totalBytes = 0;
            long maxTicks = 0;

            if (Directory.Exists(gdbPath))
            {
                var dirInfo = new DirectoryInfo(gdbPath);

                try
                {
                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                    {
                        // Excluir archivos de bloqueo temporal (*.lock) creados dinámicamente durante lecturas de ArcPy
                        if (file.Extension.Equals(".lock", StringComparison.OrdinalIgnoreCase))
                            continue;

                        totalBytes += file.Length;
                        if (file.LastWriteTimeUtc.Ticks > maxTicks)
                        {
                            maxTicks = file.LastWriteTimeUtc.Ticks;
                        }
                    }
                }
                catch
                {
                    // Si hay bloqueo temporal en algún archivo, continuar
                }

                if (maxTicks == 0)
                {
                    maxTicks = dirInfo.LastWriteTimeUtc.Ticks;
                }
            }

            string claveHuella = $"{gdbPath.ToLowerInvariant()}|{totalBytes}|{maxTicks}";
            using var sha = SHA256.Create();
            byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(claveHuella));
            string hashHex = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();

            return Path.Combine(_cacheDirectory, $"{nombreLimpio}_{hashHex}.gpkg");
        }

        public async Task<GdbImportResult> ImportarGdbAsync(string gdbPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gdbPath))
            {
                return new GdbImportResult(false, null, Array.Empty<string>(), "Ruta de File Geodatabase nula o vacía.", false);
            }

            if (!Directory.Exists(gdbPath))
            {
                return new GdbImportResult(false, null, Array.Empty<string>(), $"El directorio de la File Geodatabase no existe: {gdbPath}", false);
            }

            string cacheGpkg = ObtenerRutaCache(gdbPath);

            // 1. Validar si ya existe en caché y está completo
            if (File.Exists(cacheGpkg))
            {
                var fi = new FileInfo(cacheGpkg);
                if (fi.Length > 0)
                {
                    RasterDiagnostics.Log($"[FileGdbImporter] Cargando desde caché existente: {cacheGpkg} ({fi.Length / (1024.0 * 1024):F2} MB)");
                    return new GdbImportResult(true, cacheGpkg, Array.Empty<string>(), null, true);
                }
            }

            // 2. Si no está en caché, requerimos ArcPy
            if (!IsArcPyAvailable)
            {
                return new GdbImportResult(
                    false,
                    null,
                    Array.Empty<string>(),
                    "No se detectó el entorno de ArcGIS Pro / ArcPy en este equipo para procesar la File Geodatabase (.gdb). " +
                    "Para visualizar estas capas directamente, expórtelas a GeoPackage (.gpkg) o Shapefile (.shp), o ejecute el programa en un equipo con ArcGIS Pro instalado.",
                    false
                );
            }

            // 3. Ejecutar exportación mediante Python / ArcPy
            string tempScript = Path.Combine(Path.GetTempPath(), $"gdb_export_{Guid.NewGuid():N}.py");
            try
            {
                string scriptPython = GenerarScriptPython();
                await File.WriteAllTextAsync(tempScript, scriptPython, Encoding.UTF8, ct);

                RasterDiagnostics.Log($"[FileGdbImporter] Exportando '{gdbPath}' hacia '{cacheGpkg}' usando Python: {_pythonExecutablePath}");

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExecutablePath!,
                    Arguments = $"\"{tempScript}\" \"{gdbPath}\" \"{cacheGpkg}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    return new GdbImportResult(false, null, Array.Empty<string>(), "La conversión de la Geodatabase excedió el tiempo límite o fue cancelada.", false);
                }

                string stdout = outputBuilder.ToString().Trim();
                string stderr = errorBuilder.ToString().Trim();

                if (process.ExitCode != 0 || !File.Exists(cacheGpkg))
                {
                    string errorMsg = !string.IsNullOrEmpty(stderr) ? stderr : stdout;
                    if (string.IsNullOrWhiteSpace(errorMsg)) errorMsg = $"Código de salida de proceso: {process.ExitCode}";
                    RasterDiagnostics.Log($"[FileGdbImporter] Error en script de exportación: {errorMsg}");
                    return new GdbImportResult(false, null, Array.Empty<string>(), $"Error al convertir la Geodatabase: {errorMsg}", false);
                }

                // Intentar leer lista de capas exportadas del JSON de salida
                var exportadas = new List<string>();
                try
                {
                    using var doc = JsonDocument.Parse(stdout);
                    if (doc.RootElement.TryGetProperty("exported", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrEmpty(s)) exportadas.Add(s);
                        }
                    }
                }
                catch
                {
                    // Si el JSON no pudo parsearse pero el archivo existe, no bloquear
                }

                RasterDiagnostics.Log($"[FileGdbImporter] Conversión exitosa. Capas exportadas: {string.Join(", ", exportadas)}");
                return new GdbImportResult(true, cacheGpkg, exportadas, null, false);
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Excepción no controlada durante importación de GDB: {ex}");
                return new GdbImportResult(false, null, Array.Empty<string>(), $"Excepción procesando Geodatabase: {ex.Message}", false);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempScript)) File.Delete(tempScript);
                }
                catch { }
            }
        }

        public async Task<IReadOnlyList<GdbCapaInfo>> ObtenerCapasGdbAsync(string gdbPath, CancellationToken ct = default)
        {
            var capas = new List<GdbCapaInfo>();
            if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath))
                return capas;

            string cachePath = ObtenerRutaCache(gdbPath);
            string metaPath = cachePath + ".meta.json";

            // 1. Si no existe metaPath pero tenemos ArcPy disponible, generamos metadatos directamente (~0.5s)
            if (!File.Exists(metaPath) && IsArcPyAvailable)
            {
                await GenerarMetadatosGdbAsync(gdbPath, metaPath, ct);
            }

            // 2. Si existe el caché de metadatos JSON, leerlo directamente (ultrarrápido <1ms)
            if (File.Exists(metaPath))
            {
                try
                {
                    string jsonContent = await File.ReadAllTextAsync(metaPath, ct);
                    using var doc = JsonDocument.Parse(jsonContent);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            bool isAtt = elem.TryGetProperty("is_attachment", out var pAtt) && pAtt.GetBoolean();
                            bool isRel = elem.TryGetProperty("is_relationship", out var pRel) && pRel.GetBoolean();
                            string? origin = elem.TryGetProperty("origin", out var pOrig) && pOrig.ValueKind == JsonValueKind.Array && pOrig.GetArrayLength() > 0
                                ? pOrig[0].GetString() : null;
                            string? destination = elem.TryGetProperty("destination", out var pDest) && pDest.ValueKind == JsonValueKind.Array && pDest.GetArrayLength() > 0
                                ? pDest[0].GetString() : null;
                            string? card = elem.TryGetProperty("cardinality", out var pCard) ? pCard.GetString() : null;

                            capas.Add(new GdbCapaInfo
                            {
                                Nombre = elem.GetProperty("name").GetString() ?? "",
                                TipoGeometria = elem.GetProperty("geometry_type").GetString() ?? "",
                                CantidadElementos = elem.GetProperty("count").GetInt64(),
                                CrsNombre = elem.GetProperty("crs").GetString() ?? "",
                                EsTabla = elem.GetProperty("is_table").GetBoolean(),
                                EsAdjunto = isAtt,
                                EsRelacion = isRel,
                                TablaOrigen = origin,
                                TablaDestino = destination,
                                Cardinalidad = card,
                                RutaGdb = gdbPath
                            });
                        }
                        if (capas.Count > 0) return capas;
                    }
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.Log($"[FileGdbImporter] Error leyendo meta cache '{metaPath}': {ex.Message}");
                }
            }

            // 3. Si no está en caché el GeoPackage pero tenemos ArcPy, importar
            if (!File.Exists(cachePath))
            {
                if (!IsArcPyAvailable)
                    return capas;

                var importRes = await ImportarGdbAsync(gdbPath, ct);
                if (!importRes.Success || string.IsNullOrEmpty(importRes.GeoPackagePath))
                    return capas;

                cachePath = importRes.GeoPackagePath;
                metaPath = cachePath + ".meta.json";

                if (File.Exists(metaPath))
                {
                    try
                    {
                        string jsonContent = await File.ReadAllTextAsync(metaPath, ct);
                        using var doc = JsonDocument.Parse(jsonContent);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in doc.RootElement.EnumerateArray())
                            {
                                bool isAtt = elem.TryGetProperty("is_attachment", out var pAtt) && pAtt.GetBoolean();
                                bool isRel = elem.TryGetProperty("is_relationship", out var pRel) && pRel.GetBoolean();
                                string? origin = elem.TryGetProperty("origin", out var pOrig) && pOrig.ValueKind == JsonValueKind.Array && pOrig.GetArrayLength() > 0
                                    ? pOrig[0].GetString() : null;
                                string? destination = elem.TryGetProperty("destination", out var pDest) && pDest.ValueKind == JsonValueKind.Array && pDest.GetArrayLength() > 0
                                    ? pDest[0].GetString() : null;
                                string? card = elem.TryGetProperty("cardinality", out var pCard) ? pCard.GetString() : null;

                                capas.Add(new GdbCapaInfo
                                {
                                    Nombre = elem.GetProperty("name").GetString() ?? "",
                                    TipoGeometria = elem.GetProperty("geometry_type").GetString() ?? "",
                                    CantidadElementos = elem.GetProperty("count").GetInt64(),
                                    CrsNombre = elem.GetProperty("crs").GetString() ?? "",
                                    EsTabla = elem.GetProperty("is_table").GetBoolean(),
                                    EsAdjunto = isAtt,
                                    EsRelacion = isRel,
                                    TablaOrigen = origin,
                                    TablaDestino = destination,
                                    Cardinalidad = card,
                                    RutaGdb = gdbPath
                                });
                            }
                            if (capas.Count > 0) return capas;
                        }
                    }
                    catch { }
                }
            }

            // 4. Fallback: inspección directa del archivo GeoPackage mediante ArcGIS Runtime
            try
            {
                if (File.Exists(cachePath))
                {
                    var gpkg = await GeoPackage.OpenAsync(cachePath);
                    try
                    {
                        foreach (var table in gpkg.GeoPackageFeatureTables)
                        {
                            long featureCount = 0;
                            try
                            {
                                featureCount = await table.QueryFeatureCountAsync(new QueryParameters());
                            }
                            catch { }

                            string crsNombre = "WGS 84";
                            if (table.SpatialReference != null)
                            {
                                crsNombre = table.SpatialReference.Wkid != 0 ? $"EPSG:{table.SpatialReference.Wkid}" : "Personalizado";
                            }

                            capas.Add(new GdbCapaInfo
                            {
                                Nombre = table.TableName,
                                TipoGeometria = table.GeometryType.ToString(),
                                CantidadElementos = featureCount,
                                CrsNombre = crsNombre,
                                EsTabla = false,
                                RutaGdb = gdbPath
                            });
                        }

                        int rasterIndex = 1;
                        foreach (var _ in gpkg.GeoPackageRasters)
                        {
                            capas.Add(new GdbCapaInfo
                            {
                                Nombre = $"Ráster {rasterIndex}",
                                TipoGeometria = "Ráster",
                                CantidadElementos = 1,
                                CrsNombre = "",
                                EsTabla = false,
                                RutaGdb = gdbPath
                            });
                            rasterIndex++;
                        }
                    }
                    finally
                    {
                        gpkg.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error leyendo metadatos de GeoPackage '{cachePath}': {ex.Message}");
            }

            return capas;
        }

        public async Task<IReadOnlyList<AdjuntoFotoInfo>> ObtenerAdjuntosElementoAsync(string gdbPath, string globalId, CancellationToken ct = default)
        {
            var adjuntos = new List<AdjuntoFotoInfo>();
            if (string.IsNullOrWhiteSpace(gdbPath) || string.IsNullOrWhiteSpace(globalId))
                return adjuntos;

            string cleanGid = globalId.Trim().Trim('{', '}').ToLowerInvariant();
            if (string.IsNullOrEmpty(cleanGid))
                return adjuntos;

            string nombreGdb = Path.GetFileNameWithoutExtension(gdbPath);
            using var sha = SHA256.Create();
            string gdbHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(gdbPath.ToLowerInvariant())))[..12].ToLowerInvariant();

            string attachmentsDir = Path.Combine(_cacheDirectory, "Attachments", $"{nombreGdb}_{gdbHash}", cleanGid);
            string metaJsonPath = Path.Combine(attachmentsDir, "attachments.json");

            // 1. Si ya se extrajeron previamente a la caché local, devolver al instante (<1ms)
            if (Directory.Exists(attachmentsDir) && File.Exists(metaJsonPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(metaJsonPath, ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            string localPath = el.GetProperty("local_path").GetString() ?? "";
                            if (File.Exists(localPath))
                            {
                                adjuntos.Add(new AdjuntoFotoInfo
                                {
                                    AttachmentId = el.GetProperty("attachment_id").GetInt32(),
                                    Nombre = el.GetProperty("name").GetString() ?? "",
                                    ContentType = el.GetProperty("content_type").GetString() ?? "image/jpeg",
                                    TamanoBytes = el.GetProperty("size").GetInt64(),
                                    RutaArchivoLocal = localPath
                                });
                            }
                        }
                        if (adjuntos.Count > 0) return adjuntos;
                    }
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.Log($"[FileGdbImporter] Error leyendo caché de adjuntos '{metaJsonPath}': {ex.Message}");
                }
            }

            // 2. Extraer bajo demanda usando ArcPy (~70ms)
            if (!IsArcPyAvailable || !Directory.Exists(gdbPath))
                return adjuntos;

            string tempScript = Path.Combine(Path.GetTempPath(), $"gdb_att_get_{Guid.NewGuid():N}.py");
            try
            {
                Directory.CreateDirectory(attachmentsDir);
                string scriptContent = GenerarScriptExtraccionAdjunto();
                await File.WriteAllTextAsync(tempScript, scriptContent, Encoding.UTF8, ct);

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExecutablePath!,
                    Arguments = $"\"{tempScript}\" \"{gdbPath}\" \"{cleanGid}\" \"{attachmentsDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));

                await process.WaitForExitAsync(timeoutCts.Token);

                string stdout = outputBuilder.ToString().Trim();
                if (!string.IsNullOrEmpty(stdout))
                {
                    using var doc = JsonDocument.Parse(stdout);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            string localPath = el.GetProperty("local_path").GetString() ?? "";
                            if (File.Exists(localPath))
                            {
                                adjuntos.Add(new AdjuntoFotoInfo
                                {
                                    AttachmentId = el.GetProperty("attachment_id").GetInt32(),
                                    Nombre = el.GetProperty("name").GetString() ?? "",
                                    ContentType = el.GetProperty("content_type").GetString() ?? "image/jpeg",
                                    TamanoBytes = el.GetProperty("size").GetInt64(),
                                    RutaArchivoLocal = localPath
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error en extracción de adjuntos para '{cleanGid}': {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
            }

            return adjuntos;
        }

        private async Task GenerarMetadatosGdbAsync(string gdbPath, string metaJsonPath, CancellationToken ct)
        {
            if (!IsArcPyAvailable || !Directory.Exists(gdbPath)) return;

            string tempScript = Path.Combine(Path.GetTempPath(), $"gdb_meta_{Guid.NewGuid():N}.py");
            try
            {
                string scriptContent = GenerarScriptMetadatos();
                await File.WriteAllTextAsync(tempScript, scriptContent, Encoding.UTF8, ct);

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExecutablePath!,
                    Arguments = $"\"{tempScript}\" \"{gdbPath}\" \"{metaJsonPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error generando metadatos rápidos para '{gdbPath}': {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
            }
        }

        public void LimpiarCache()
        {
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.gpkg"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.meta.json"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    string attDir = Path.Combine(_cacheDirectory, "Attachments");
                    if (Directory.Exists(attDir))
                    {
                        Directory.Delete(attDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error al limpiar caché: {ex.Message}");
            }
        }

        private static string GenerarScriptPython()
        {
            return @"# -*- coding: utf-8 -*-
import arcpy, os, sys, json

def export_all(gdb_path, out_gpkg):
    try:
        arcpy.env.overwriteOutput = True
        out_dir = os.path.dirname(out_gpkg)
        if out_dir and not os.path.exists(out_dir):
            os.makedirs(out_dir, exist_ok=True)

        if os.path.exists(out_gpkg):
            try:
                os.remove(out_gpkg)
            except:
                pass

        arcpy.management.CreateSQLiteDatabase(out_gpkg, 'GEOPACKAGE')

        arcpy.env.workspace = gdb_path
        standalone_fcs = [(None, fc) for fc in (arcpy.ListFeatureClasses() or [])]
        dataset_fcs = []
        for ds in (arcpy.ListDatasets(feature_type='feature') or []):
            arcpy.env.workspace = os.path.join(gdb_path, ds)
            for fc in (arcpy.ListFeatureClasses() or []):
                dataset_fcs.append((ds, fc))

        all_fcs = standalone_fcs + dataset_fcs
        exported = []
        layers_meta = []
        existing_names = set()

        for ds, fc in all_fcs:
            src = os.path.join(gdb_path, ds, fc) if ds else os.path.join(gdb_path, fc)
            target_name = f'{ds}_{fc}' if ds else fc
            target_name = ''.join(c if (c.isalnum() or c == '_') else '_' for c in target_name)
            dst = os.path.join(out_gpkg, target_name)
            arcpy.conversion.ExportFeatures(src, dst)
            exported.append(target_name)
            existing_names.add(fc if not ds else f'{ds}/{fc}')
            try:
                desc = arcpy.Describe(src)
                cnt = int(arcpy.GetCount_management(src)[0])
                layers_meta.append({
                    'name': target_name,
                    'geometry_type': desc.shapeType,
                    'count': cnt,
                    'crs': desc.spatialReference.name if (desc.spatialReference and desc.spatialReference.name) else 'WGS 84',
                    'is_table': False,
                    'is_attachment': False,
                    'is_relationship': False
                })
            except:
                pass

        # Inspeccionar elementos con arcpy.Describe(gdb_path).children (exactitud ArcGIS Pro)
        desc_gdb = arcpy.Describe(gdb_path)
        for c in (desc_gdb.children or []):
            c_name = c.name
            if c_name in existing_names or any(m['name'] == c_name for m in layers_meta):
                continue

            src = os.path.join(gdb_path, c_name)
            if c.dataType == 'Table':
                is_att = c_name.endswith('__ATTACH')
                try:
                    cnt = int(arcpy.GetCount_management(src)[0])
                except:
                    cnt = 0

                # Tablas regulares se exportan a GPKG; tablas de adjuntos conservan almacenamiento BLOB nativo
                if not is_att:
                    target_name = ''.join(ch if (ch.isalnum() or ch == '_') else '_' for ch in c_name)
                    dst = os.path.join(out_gpkg, target_name)
                    try:
                        arcpy.conversion.ExportTable(src, dst)
                        exported.append(target_name)
                    except:
                        pass

                layers_meta.append({
                    'name': c_name,
                    'geometry_type': 'None',
                    'count': cnt,
                    'crs': '',
                    'is_table': True,
                    'is_attachment': is_att,
                    'is_relationship': False
                })
                existing_names.add(c_name)

            elif c.dataType == 'RelationshipClass':
                layers_meta.append({
                    'name': c_name,
                    'geometry_type': 'None',
                    'count': 0,
                    'crs': '',
                    'is_table': False,
                    'is_attachment': False,
                    'is_relationship': True,
                    'cardinality': getattr(c, 'cardinality', ''),
                    'origin': getattr(c, 'originClassNames', []),
                    'destination': getattr(c, 'destinationClassNames', [])
                })
                existing_names.add(c_name)

        arcpy.env.workspace = gdb_path
        for tbl in (arcpy.ListTables() or []):
            if tbl in existing_names or any(m['name'] == tbl for m in layers_meta):
                continue
            src = os.path.join(gdb_path, tbl)
            is_att = tbl.endswith('__ATTACH')
            try:
                cnt = int(arcpy.GetCount_management(src)[0])
            except:
                cnt = 0
            if not is_att:
                target_name = ''.join(ch if (ch.isalnum() or ch == '_') else '_' for ch in tbl)
                dst = os.path.join(out_gpkg, target_name)
                try:
                    arcpy.conversion.ExportTable(src, dst)
                    exported.append(target_name)
                except:
                    pass
            layers_meta.append({
                'name': tbl,
                'geometry_type': 'None',
                'count': cnt,
                'crs': '',
                'is_table': True,
                'is_attachment': is_att,
                'is_relationship': False
            })
            existing_names.add(tbl)

        meta_path = out_gpkg + '.meta.json'
        try:
            with open(meta_path, 'w', encoding='utf-8') as f:
                json.dump(layers_meta, f, ensure_ascii=False, indent=2)
        except:
            pass

        print(json.dumps({'success': True, 'exported': exported, 'layers': layers_meta}))
    except Exception as ex:
        print(json.dumps({'success': False, 'error': str(ex)}))
        sys.exit(1)

if __name__ == '__main__':
    if len(sys.argv) < 3:
        print(json.dumps({'success': False, 'error': 'Argumentos insuficientes'}))
        sys.exit(1)
    export_all(sys.argv[1], sys.argv[2])
";
        }

        private static string GenerarScriptMetadatos()
        {
            return @"# -*- coding: utf-8 -*-
import arcpy, os, sys, json

def get_meta(gdb_path, out_meta):
    try:
        desc = arcpy.Describe(gdb_path)
        meta = []
        existing = set()

        for c in (desc.children or []):
            src = os.path.join(gdb_path, c.name)
            if c.dataType == 'FeatureClass':
                try:
                    cnt = int(arcpy.GetCount_management(src)[0])
                except:
                    cnt = 0
                crs = c.spatialReference.name if (hasattr(c, 'spatialReference') and c.spatialReference) else 'WGS 84'
                meta.append({
                    'name': c.name,
                    'geometry_type': getattr(c, 'shapeType', 'Point'),
                    'count': cnt,
                    'crs': crs,
                    'is_table': False,
                    'is_attachment': False,
                    'is_relationship': False
                })
                existing.add(c.name)
            elif c.dataType == 'Table':
                is_att = c.name.endswith('__ATTACH')
                try:
                    cnt = int(arcpy.GetCount_management(src)[0])
                except:
                    cnt = 0
                meta.append({
                    'name': c.name,
                    'geometry_type': 'None',
                    'count': cnt,
                    'crs': '',
                    'is_table': True,
                    'is_attachment': is_att,
                    'is_relationship': False
                })
                existing.add(c.name)
            elif c.dataType == 'RelationshipClass':
                meta.append({
                    'name': c.name,
                    'geometry_type': 'None',
                    'count': 0,
                    'crs': '',
                    'is_table': False,
                    'is_attachment': False,
                    'is_relationship': True,
                    'cardinality': getattr(c, 'cardinality', ''),
                    'origin': getattr(c, 'originClassNames', []),
                    'destination': getattr(c, 'destinationClassNames', [])
                })
                existing.add(c.name)

        arcpy.env.workspace = gdb_path
        for fc in (arcpy.ListFeatureClasses() or []):
            if fc not in existing:
                src = os.path.join(gdb_path, fc)
                cnt = int(arcpy.GetCount_management(src)[0])
                meta.append({'name': fc, 'geometry_type': 'Point', 'count': cnt, 'crs': 'WGS 84', 'is_table': False, 'is_attachment': False, 'is_relationship': False})
                existing.add(fc)

        for tbl in (arcpy.ListTables() or []):
            if tbl not in existing:
                src = os.path.join(gdb_path, tbl)
                cnt = int(arcpy.GetCount_management(src)[0])
                meta.append({'name': tbl, 'geometry_type': 'None', 'count': cnt, 'crs': '', 'is_table': True, 'is_attachment': tbl.endswith('__ATTACH'), 'is_relationship': False})
                existing.add(tbl)

        out_dir = os.path.dirname(out_meta)
        if out_dir and not os.path.exists(out_dir):
            os.makedirs(out_dir, exist_ok=True)

        with open(out_meta, 'w', encoding='utf-8') as f:
            json.dump(meta, f, ensure_ascii=False, indent=2)
        print(json.dumps(meta))
    except Exception as ex:
        print(json.dumps([]))

if __name__ == '__main__':
    if len(sys.argv) >= 3:
        get_meta(sys.argv[1], sys.argv[2])
    else:
        print(json.dumps([]))
";
        }

        private static string GenerarScriptExtraccionAdjunto()
        {
            return @"# -*- coding: utf-8 -*-
import arcpy, os, sys, json

def extract(gdb_path, gid, out_dir):
    try:
        os.makedirs(out_dir, exist_ok=True)
        results = []
        gid_clean = gid.strip().strip('{}')
        gids = [f'{{{gid_clean}}}', gid_clean]

        arcpy.env.workspace = gdb_path
        attach_tables = [t for t in (arcpy.ListTables() or []) if t.endswith('__ATTACH')]
        if not attach_tables:
            desc = arcpy.Describe(gdb_path)
            attach_tables = [c.name for c in (desc.children or []) if c.dataType == 'Table' and c.name.endswith('__ATTACH')]

        for tbl in attach_tables:
            src = os.path.join(gdb_path, tbl)
            fields = [f.name for f in arcpy.ListFields(src)]
            has_rel_gid = 'REL_GLOBALID' in fields
            has_rel_oid = 'REL_OBJECTID' in fields

            where = None
            if has_rel_gid:
                where = f""REL_GLOBALID IN ('{gids[0]}', '{gids[1]}')""
            elif has_rel_oid and gid_clean.isdigit():
                where = f""REL_OBJECTID = {gid_clean}""

            if not where:
                continue

            try:
                with arcpy.da.SearchCursor(src, ['ATTACHMENTID', 'ATT_NAME', 'CONTENT_TYPE', 'DATA_SIZE', 'DATA'], where) as cursor:
                    for row in cursor:
                        att_id = row[0]
                        att_name = row[1] or f'adjunto_{att_id}.jpg'
                        content_type = row[2] or 'image/jpeg'
                        data_size = row[3]
                        blob = bytes(row[4])

                        safe_name = ''.join(c if (c.isalnum() or c in '._-') else '_' for c in att_name)
                        file_path = os.path.join(out_dir, f'{att_id}_{safe_name}')
                        if not os.path.exists(file_path) or os.path.getsize(file_path) != len(blob):
                            with open(file_path, 'wb') as f:
                                f.write(blob)

                        results.append({
                            'attachment_id': att_id,
                            'name': att_name,
                            'content_type': content_type,
                            'size': len(blob),
                            'local_path': file_path
                        })
            except:
                pass

        meta_file = os.path.join(out_dir, 'attachments.json')
        try:
            with open(meta_file, 'w', encoding='utf-8') as mf:
                json.dump(results, mf, ensure_ascii=False, indent=2)
        except:
            pass

        print(json.dumps(results))
    except Exception as ex:
        print(json.dumps([]))

if __name__ == '__main__':
    if len(sys.argv) >= 4:
        extract(sys.argv[1], sys.argv[2], sys.argv[3])
    else:
        print(json.dumps([]))
";
        }

        private static string? DetectarPythonArcGisPro()
        {
            // 1. Variable de entorno personalizada
            string? envPath = Environment.GetEnvironmentVariable("ARCGISPRO_PYTHON");
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            // 2. Registro de Windows (HKLM\SOFTWARE\ESRI\ArcGISPro)
            try
            {
                using var view64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var proKey = view64.OpenSubKey(@"SOFTWARE\ESRI\ArcGISPro");
                if (proKey != null)
                {
                    string? condaRoot = proKey.GetValue("PythonCondaRoot") as string;
                    string? condaEnv = proKey.GetValue("PythonCondaEnv") as string;
                    if (!string.IsNullOrWhiteSpace(condaRoot) && !string.IsNullOrWhiteSpace(condaEnv))
                    {
                        string candidate = Path.Combine(condaRoot, "envs", condaEnv, "python.exe");
                        if (File.Exists(candidate)) return candidate;
                    }

                    string? installDir = proKey.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        string candidate = Path.Combine(installDir, "bin", "Python", "envs", "arcgispro-py3", "python.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[FileGdbImporter] Error consultando registro de ArcGIS Pro: {ex.Message}");
            }

            // 3. Rutas estándar habituales en Windows
            string[] rutasEstandar = new[]
            {
                @"C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe")
            };

            foreach (var r in rutasEstandar)
            {
                if (File.Exists(r)) return r;
            }

            return null;
        }
    }
}

