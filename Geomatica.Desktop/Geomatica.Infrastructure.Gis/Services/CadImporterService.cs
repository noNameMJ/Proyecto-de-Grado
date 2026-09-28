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
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using Geomatica.Desktop.Models;
using MaxRev.Gdal.Core;
using OSGeo.OGR;
using OSGeo.OSR;

namespace Geomatica.Desktop.Services
{
    public class CadImporterService : ICadImporterService
    {
        private static bool _gdalInitialized = false;
        private static readonly object _gdalLock = new();

        private static bool InicializarGdal()
        {
            if (_gdalInitialized) return true;
            lock (_gdalLock)
            {
                if (_gdalInitialized) return true;
                try
                {
                    GdalBase.ConfigureAll();
                    Ogr.RegisterAll();
                    _gdalInitialized = true;
                    return true;
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.Log($"[CadImporter] Error inicializando GDAL: {ex.Message}");
                    return false;
                }
            }
        }

        private static string? DetectarPythonArcGisPro()
        {
            try
            {
                string envPro = Environment.GetEnvironmentVariable("ARCGISPRO_PYTHON") ?? "";
                if (!string.IsNullOrEmpty(envPro) && File.Exists(envPro))
                {
                    return envPro;
                }

                string standardProPath = @"C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe";
                if (File.Exists(standardProPath))
                {
                    return standardProPath;
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userCondaPath = Path.Combine(localAppData, @"ESRI\conda\envs\arcgispro-py3\python.exe");
                if (File.Exists(userCondaPath))
                {
                    return userCondaPath;
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[CadImporter] Excepción detectando Python ArcGIS Pro: {ex.Message}");
            }
            return null;
        }

        private readonly string? _pythonExecutablePath;
        private readonly string _cacheDirectory;

        public bool IsArcPyAvailable => !string.IsNullOrEmpty(_pythonExecutablePath) && File.Exists(_pythonExecutablePath);
        public bool IsACadSharpAvailable => true;
        public string ProveedorActivo => IsArcPyAvailable ? "ArcPy (ArcGIS Pro)" : "ACadSharp (Autónomo)";
        public string? PythonExecutablePath => _pythonExecutablePath;

        public CadImporterService(string? customPythonPath = null, string? customCacheDirectory = null)
        {
            _pythonExecutablePath = customPythonPath ?? DetectarPythonArcGisPro();
            _cacheDirectory = customCacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Geomatica",
                "CadCache"
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
                RasterDiagnostics.Log($"[CadImporter] Error creando directorio de caché '{_cacheDirectory}': {ex.Message}");
            }
        }

        public string ObtenerRutaCache(string cadFilePath)
        {
            if (string.IsNullOrWhiteSpace(cadFilePath))
                throw new ArgumentException("La ruta del archivo CAD no puede ser nula ni vacía.", nameof(cadFilePath));

            string nombreCad = Path.GetFileNameWithoutExtension(cadFilePath);
            if (string.IsNullOrWhiteSpace(nombreCad))
                nombreCad = "cad_drawing";

            var sbNombre = new StringBuilder();
            foreach (char c in nombreCad)
            {
                sbNombre.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            }
            string nombreLimpio = sbNombre.ToString();

            long fileLength = 0;
            long ticks = 0;
            if (File.Exists(cadFilePath))
            {
                var fi = new FileInfo(cadFilePath);
                fileLength = fi.Length;
                ticks = fi.LastWriteTimeUtc.Ticks;
            }

            using var sha = SHA256.Create();
            string hashInput = $"{cadFilePath.ToLowerInvariant()}|{fileLength}|{ticks}";
            byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
            string hashHex = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();

            return Path.Combine(_cacheDirectory, $"{nombreLimpio}_{hashHex}.gpkg");
        }

        public void LimpiarCache()
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory)) return;
                var dir = new DirectoryInfo(_cacheDirectory);
                foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch (Exception ex)
            {
                RasterDiagnostics.Log($"[CadImporter] Error limpiando caché CAD: {ex.Message}");
            }
        }

        public async Task<CadImportResult> ImportarCadAsync(string cadFilePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cadFilePath) || !File.Exists(cadFilePath))
            {
                return new CadImportResult(false, null, Array.Empty<string>(), $"El archivo CAD no existe: {cadFilePath}");
            }

            string cacheGpkg = ObtenerRutaCache(cadFilePath);

            // 1. Nivel 0: Caché determinista
            if (File.Exists(cacheGpkg))
            {
                var fi = new FileInfo(cacheGpkg);
                if (fi.Length > 0)
                {
                    RasterDiagnostics.Log($"[CadImporter] Cargando CAD desde caché existente: {cacheGpkg} ({fi.Length / 1024.0:F1} KB)");
                    return new CadImportResult(true, cacheGpkg, Array.Empty<string>(), null, true, "Caché Local");
                }
            }

            // 2. Nivel 1: Prioridad ArcPy (si ArcGIS Pro está disponible)
            if (IsArcPyAvailable)
            {
                try
                {
                    RasterDiagnostics.Log($"[CadImporter] [Prioridad 1 - ArcPy] Iniciando conversión CAD: {cadFilePath}");
                    var resArcPy = await ExportarConArcPyAsync(cadFilePath, cacheGpkg, ct);
                    if (resArcPy.Success && File.Exists(cacheGpkg) && new FileInfo(cacheGpkg).Length > 0)
                    {
                        RasterDiagnostics.Log($"[CadImporter] ArcPy convirtió exitosamente CAD hacia '{cacheGpkg}'.");
                        return resArcPy;
                    }

                    RasterDiagnostics.Log($"[CadImporter] ArcPy no pudo convertir el CAD ({resArcPy.MensajeError}). Degradando a ACadSharp...");
                }
                catch (Exception exArcPy)
                {
                    RasterDiagnostics.Log($"[CadImporter] Excepción en ArcPy: {exArcPy.Message}. Degradando a ACadSharp...");
                }
            }

            // 3. Nivel 2: Motor Autónomo C# (ACadSharp - Graceful Degradation)
            try
            {
                RasterDiagnostics.Log($"[CadImporter] [Degradación Elegante - ACadSharp Autónomo] Convirtiendo CAD: {cadFilePath}");
                var resACad = await ExportarConACadSharpAsync(cadFilePath, cacheGpkg, ct);
                if (resACad.Success && File.Exists(cacheGpkg) && new FileInfo(cacheGpkg).Length > 0)
                {
                    RasterDiagnostics.Log($"[CadImporter] ACadSharp convirtió exitosamente {resACad.Capas.Count} capas CAD hacia '{cacheGpkg}'.");
                    return resACad;
                }

                return resACad;
            }
            catch (Exception exACad)
            {
                RasterDiagnostics.Log($"[CadImporter] Excepción en ACadSharp: {exACad.Message}");
                return new CadImportResult(false, null, Array.Empty<string>(), $"Error procesando archivo CAD: {exACad.Message}");
            }
        }

        public async Task<IReadOnlyList<CadCapaInfo>> ObtenerCapasCadAsync(string cadFilePath, CancellationToken ct = default)
        {
            var capas = new List<CadCapaInfo>();
            if (string.IsNullOrWhiteSpace(cadFilePath) || !File.Exists(cadFilePath))
                return capas;

            string cachePath = ObtenerRutaCache(cadFilePath);
            string metaPath = cachePath + ".meta.json";

            // 1. Nivel 0: Caché de metadatos JSON
            if (File.Exists(metaPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(metaPath, ct);
                    var cached = JsonSerializer.Deserialize<List<CadCapaInfo>>(json);
                    if (cached != null && cached.Count > 0) return cached;
                }
                catch { }
            }

            // 2. Nivel 1/2: Inspección directa con ACadSharp (<100ms)
            return await Task.Run(() =>
            {
                try
                {
                    var doc = LeerDocumentoCad(cadFilePath);
                    var capasDic = new Dictionary<string, CadCapaInfo>(StringComparer.OrdinalIgnoreCase);

                    // Registrar todas las capas definidas en el archivo CAD
                    foreach (var l in doc.Layers)
                    {
                        var col = l.Color;
                        string colorHex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
                        capasDic[l.Name] = new CadCapaInfo
                        {
                            Nombre = l.Name,
                            TipoGeometria = "Polyline",
                            CantidadElementos = 0,
                            ColorHex = colorHex,
                            EsVisible = l.Flags != ACadSharp.Tables.LayerFlags.Frozen,
                            RutaArchivoCad = cadFilePath
                        };
                    }

                    // Contar entidades por capa
                    var entidades = doc.Entities;
                    foreach (var ent in entidades)
                    {
                        ct.ThrowIfCancellationRequested();
                        string layerName = ent.Layer?.Name ?? "0";
                        if (!capasDic.TryGetValue(layerName, out var info))
                        {
                            info = new CadCapaInfo
                            {
                                Nombre = layerName,
                                TipoGeometria = "Polyline",
                                CantidadElementos = 0,
                                ColorHex = "#0D47A1",
                                EsVisible = true,
                                RutaArchivoCad = cadFilePath
                            };
                            capasDic[layerName] = info;
                        }

                        info.CantidadElementos++;
                        if (ent is Point or Insert) info.TipoGeometria = "Point";
                        else if (ent is TextEntity or MText) info.TipoGeometria = "Text";
                        else if (ent is LwPolyline lw && lw.IsClosed) info.TipoGeometria = "Polygon";
                    }

                    var listaFinal = capasDic.Values.OrderByDescending(c => c.CantidadElementos).ToList();

                    // Guardar en caché JSON
                    try
                    {
                        string json = JsonSerializer.Serialize(listaFinal, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metaPath, json);
                    }
                    catch { }

                    return (IReadOnlyList<CadCapaInfo>)listaFinal;
                }
                catch (Exception ex)
                {
                    RasterDiagnostics.Log($"[CadImporter] Error en inspección de capas CAD: {ex.Message}");
                    return capas;
                }
            }, ct);
        }

        private static CadDocument LeerDocumentoCad(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".dxf")
            {
                using var reader = new DxfReader(filePath);
                return reader.Read();
            }
            else
            {
                using var reader = new DwgReader(filePath);
                return reader.Read();
            }
        }

        private async Task<CadImportResult> ExportarConArcPyAsync(string cadFilePath, string cacheGpkg, CancellationToken ct)
        {
            if (!IsArcPyAvailable)
            {
                return new CadImportResult(false, null, Array.Empty<string>(), "ArcPy no está disponible en este entorno.");
            }

            return await Task.Run(async () =>
            {
                InicializarGdal();
                string tempScript = Path.Combine(Path.GetTempPath(), $"cad_exp_{Guid.NewGuid():N}.py");
                string tempDir = Path.GetDirectoryName(cacheGpkg) ?? Path.GetTempPath();
                string tempGdb = Path.Combine(tempDir, $"cad_temp_{Guid.NewGuid():N}.gdb");

                try
                {
                    string scriptContent = $@"
import arcpy, os, sys, json
arcpy.env.overwriteOutput = True
cad_path = sys.argv[1]
temp_gdb = sys.argv[2]

temp_dir = os.path.dirname(temp_gdb)
gdb_name = os.path.basename(temp_gdb)
if arcpy.Exists(temp_gdb):
    try: arcpy.management.Delete(temp_gdb)
    except: pass
arcpy.management.CreateFileGDB(temp_dir, gdb_name)

try:
    arcpy.conversion.CADToGeodatabase(cad_path, temp_gdb, 'CadFeatures', 1000)
    print(json.dumps({{'success': True, 'gdb': temp_gdb}}))
except Exception as e:
    print(json.dumps({{'success': False, 'error': str(e)}}))
";
                    await File.WriteAllTextAsync(tempScript, scriptContent, Encoding.UTF8, ct);

                    var psi = new ProcessStartInfo
                    {
                        FileName = _pythonExecutablePath!,
                        Arguments = $"\"{tempScript}\" \"{cadFilePath}\" \"{tempGdb}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        return new CadImportResult(false, null, Array.Empty<string>(), "No se pudo iniciar el proceso de Python para ArcPy.");
                    }

                    string stdout = await process.StandardOutput.ReadToEndAsync(ct);
                    string stderr = await process.StandardError.ReadToEndAsync(ct);
                    await process.WaitForExitAsync(ct);

                    if (!Directory.Exists(tempGdb))
                    {
                        return new CadImportResult(false, null, Array.Empty<string>(), $"Fallo en conversión ArcPy: {stderr}");
                    }

                    // Copiar capas desde tempGdb hacia cacheGpkg usando GDAL GPKG
                    var exportadas = new List<string>();
                    var gpkgDriver = Ogr.GetDriverByName("GPKG");
                    if (gpkgDriver == null)
                    {
                        return new CadImportResult(false, null, Array.Empty<string>(), "Driver GPKG de OGR no disponible.");
                    }

                    using (var srcDs = Ogr.Open(tempGdb, 0))
                    {
                        if (srcDs == null)
                        {
                            return new CadImportResult(false, null, Array.Empty<string>(), "No se pudo abrir la geodatabase temporal generada por ArcPy.");
                        }

                        if (File.Exists(cacheGpkg)) File.Delete(cacheGpkg);
                        using var destDs = gpkgDriver.CreateDataSource(cacheGpkg, null);
                        if (destDs == null)
                        {
                            return new CadImportResult(false, null, Array.Empty<string>(), "No se pudo crear el contenedor GeoPackage.");
                        }

                        int layerCount = srcDs.GetLayerCount();
                        for (int i = 0; i < layerCount; i++)
                        {
                            using var layer = srcDs.GetLayerByIndex(i);
                            if (layer == null) continue;
                            string name = layer.GetName();
                            if (layer.GetFeatureCount(1) > 0)
                            {
                                using var copied = destDs.CopyLayer(layer, name, null);
                                exportadas.Add(name);
                            }
                        }
                        destDs.FlushCache();
                    }

                    return new CadImportResult(true, cacheGpkg, exportadas, null, false, "ArcPy (ArcGIS Pro)");
                }
                finally
                {
                    try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
                    try { if (Directory.Exists(tempGdb)) Directory.Delete(tempGdb, true); } catch { }
                }
            }, ct);
        }

        private async Task<CadImportResult> ExportarConACadSharpAsync(string cadFilePath, string cacheGpkg, CancellationToken ct)
        {
            return await Task.Run(() =>
            {
                InicializarGdal();
                var gpkgDriver = Ogr.GetDriverByName("GPKG");
                if (gpkgDriver == null)
                {
                    return new CadImportResult(false, null, Array.Empty<string>(), "El driver OGR GPKG no está disponible.");
                }

                CadDocument doc;
                try
                {
                    doc = LeerDocumentoCad(cadFilePath);
                }
                catch (Exception ex)
                {
                    return new CadImportResult(false, null, Array.Empty<string>(), $"Error al leer el archivo CAD ({Path.GetExtension(cadFilePath)}): {ex.Message}");
                }

                // Configurar sistema de referencia espacial
                using var srs = new SpatialReference(null);
                string prjFile = Path.ChangeExtension(cadFilePath, ".prj");
                if (File.Exists(prjFile))
                {
                    try
                    {
                        string wkt = File.ReadAllText(prjFile);
                        srs.ImportFromWkt(ref wkt);
                    }
                    catch
                    {
                        srs.ImportFromEPSG(9377); // MAGNA-SIRGAS Origen Nacional
                    }
                }
                else
                {
                    srs.ImportFromEPSG(9377);
                }

                string tempGpkg = cacheGpkg + $".tmp_{Guid.NewGuid():N}.gpkg";
                if (File.Exists(tempGpkg)) File.Delete(tempGpkg);

                var capasExportadas = new List<string>();

                using (var destDs = gpkgDriver.CreateDataSource(tempGpkg, null))
                {
                    if (destDs == null)
                    {
                        return new CadImportResult(false, null, Array.Empty<string>(), "No se pudo inicializar la fuente de datos GeoPackage.");
                    }

                    // 1. Capa de Líneas (Line, Polyline, Circle, Arc, Spline)
                    using var layerLineas = destDs.CreateLayer("CAD_Lineas", srs, wkbGeometryType.wkbLineString, null);
                    using var fldLyrLine = new FieldDefn("Capa", FieldType.OFTString); fldLyrLine.SetWidth(64); layerLineas.CreateField(fldLyrLine, 1);
                    using var fldColLine = new FieldDefn("Color", FieldType.OFTString); fldColLine.SetWidth(16); layerLineas.CreateField(fldColLine, 1);
                    using var fldTipLine = new FieldDefn("Tipo", FieldType.OFTString); fldTipLine.SetWidth(32); layerLineas.CreateField(fldTipLine, 1);
                    using var fldHndLine = new FieldDefn("Handle", FieldType.OFTString); fldHndLine.SetWidth(32); layerLineas.CreateField(fldHndLine, 1);

                    // 2. Capa de Polígonos (Polilíneas cerradas, Hatches, Solids)
                    using var layerPoligonos = destDs.CreateLayer("CAD_Poligonos", srs, wkbGeometryType.wkbPolygon, null);
                    using var fldLyrPoly = new FieldDefn("Capa", FieldType.OFTString); fldLyrPoly.SetWidth(64); layerPoligonos.CreateField(fldLyrPoly, 1);
                    using var fldColPoly = new FieldDefn("Color", FieldType.OFTString); fldColPoly.SetWidth(16); layerPoligonos.CreateField(fldColPoly, 1);
                    using var fldTipPoly = new FieldDefn("Tipo", FieldType.OFTString); fldTipPoly.SetWidth(32); layerPoligonos.CreateField(fldTipPoly, 1);
                    using var fldHndPoly = new FieldDefn("Handle", FieldType.OFTString); fldHndPoly.SetWidth(32); layerPoligonos.CreateField(fldHndPoly, 1);

                    // 3. Capa de Puntos (Point, Insert)
                    using var layerPuntos = destDs.CreateLayer("CAD_Puntos", srs, wkbGeometryType.wkbPoint, null);
                    using var fldLyrPt = new FieldDefn("Capa", FieldType.OFTString); fldLyrPt.SetWidth(64); layerPuntos.CreateField(fldLyrPt, 1);
                    using var fldColPt = new FieldDefn("Color", FieldType.OFTString); fldColPt.SetWidth(16); layerPuntos.CreateField(fldColPt, 1);
                    using var fldTipPt = new FieldDefn("Tipo", FieldType.OFTString); fldTipPt.SetWidth(32); layerPuntos.CreateField(fldTipPt, 1);
                    using var fldHndPt = new FieldDefn("Handle", FieldType.OFTString); fldHndPt.SetWidth(32); layerPuntos.CreateField(fldHndPt, 1);

                    // 4. Capa de Anotaciones / Texto (TextEntity, MText)
                    using var layerTexto = destDs.CreateLayer("CAD_Anotaciones", srs, wkbGeometryType.wkbPoint, null);
                    using var fldLyrTxt = new FieldDefn("Capa", FieldType.OFTString); fldLyrTxt.SetWidth(64); layerTexto.CreateField(fldLyrTxt, 1);
                    using var fldColTxt = new FieldDefn("Color", FieldType.OFTString); fldColTxt.SetWidth(16); layerTexto.CreateField(fldColTxt, 1);
                    using var fldValTxt = new FieldDefn("Texto", FieldType.OFTString); fldValTxt.SetWidth(255); layerTexto.CreateField(fldValTxt, 1);
                    using var fldAltTxt = new FieldDefn("Altura", FieldType.OFTReal); layerTexto.CreateField(fldAltTxt, 1);
                    using var fldHndTxt = new FieldDefn("Handle", FieldType.OFTString); fldHndTxt.SetWidth(32); layerTexto.CreateField(fldHndTxt, 1);

                    int countLineas = 0;
                    int countPoligonos = 0;
                    int countPuntos = 0;
                    int countTexto = 0;

                    foreach (var ent in doc.Entities)
                    {
                        ct.ThrowIfCancellationRequested();
                        string capaNombre = ent.Layer?.Name ?? "0";
                        string colorHex = ObtenerColorHex(ent);
                        string handle = ent.Handle.ToString("X");

                        switch (ent)
                        {
                            case Line line:
                                {
                                    using var geom = new Geometry(wkbGeometryType.wkbLineString);
                                    geom.AddPoint_2D(line.StartPoint.X, line.StartPoint.Y);
                                    geom.AddPoint_2D(line.EndPoint.X, line.EndPoint.Y);
                                    InsertarFeature(layerLineas, geom, capaNombre, colorHex, "Line", handle);
                                    countLineas++;
                                    break;
                                }

                            case LwPolyline lw:
                                {
                                    if (lw.Vertices.Count >= 2)
                                    {
                                        if (lw.IsClosed && lw.Vertices.Count >= 3)
                                        {
                                            using var ring = new Geometry(wkbGeometryType.wkbLinearRing);
                                            foreach (var v in lw.Vertices)
                                            {
                                                ring.AddPoint_2D(v.Location.X, v.Location.Y);
                                            }
                                            ring.AddPoint_2D(lw.Vertices[0].Location.X, lw.Vertices[0].Location.Y);

                                            using var geomPoly = new Geometry(wkbGeometryType.wkbPolygon);
                                            geomPoly.AddGeometry(ring);
                                            InsertarFeature(layerPoligonos, geomPoly, capaNombre, colorHex, "LwPolyline", handle);
                                            countPoligonos++;
                                        }
                                        else
                                        {
                                            using var geomLine = new Geometry(wkbGeometryType.wkbLineString);
                                            foreach (var v in lw.Vertices)
                                            {
                                                geomLine.AddPoint_2D(v.Location.X, v.Location.Y);
                                            }
                                            InsertarFeature(layerLineas, geomLine, capaNombre, colorHex, "LwPolyline", handle);
                                            countLineas++;
                                        }
                                    }
                                    break;
                                }

                            case Polyline2D p2d:
                                {
                                    var verts = p2d.Vertices.ToList();
                                    if (verts.Count >= 2)
                                    {
                                        if (p2d.IsClosed && verts.Count >= 3)
                                        {
                                            using var ring = new Geometry(wkbGeometryType.wkbLinearRing);
                                            foreach (var v in verts)
                                            {
                                                ring.AddPoint_2D(v.Location.X, v.Location.Y);
                                            }
                                            ring.AddPoint_2D(verts[0].Location.X, verts[0].Location.Y);

                                            using var geomPoly = new Geometry(wkbGeometryType.wkbPolygon);
                                            geomPoly.AddGeometry(ring);
                                            InsertarFeature(layerPoligonos, geomPoly, capaNombre, colorHex, "Polyline2D", handle);
                                            countPoligonos++;
                                        }
                                        else
                                        {
                                            using var geomLine = new Geometry(wkbGeometryType.wkbLineString);
                                            foreach (var v in verts)
                                            {
                                                geomLine.AddPoint_2D(v.Location.X, v.Location.Y);
                                            }
                                            InsertarFeature(layerLineas, geomLine, capaNombre, colorHex, "Polyline2D", handle);
                                            countLineas++;
                                        }
                                    }
                                    break;
                                }

                            case Arc arc:
                                {
                                    using var geomArc = new Geometry(wkbGeometryType.wkbLineString);
                                    int segments = 24;
                                    double startAngle = arc.StartAngle;
                                    double endAngle = arc.EndAngle;
                                    if (endAngle < startAngle) endAngle += 2 * Math.PI;
                                    double sweep = endAngle - startAngle;
                                    for (int i = 0; i <= segments; i++)
                                    {
                                        double angle = startAngle + sweep * (i / (double)segments);
                                        double x = arc.Center.X + arc.Radius * Math.Cos(angle);
                                        double y = arc.Center.Y + arc.Radius * Math.Sin(angle);
                                        geomArc.AddPoint_2D(x, y);
                                    }
                                    InsertarFeature(layerLineas, geomArc, capaNombre, colorHex, "Arc", handle);
                                    countLineas++;
                                    break;
                                }

                            case Circle circle:
                                {
                                    using var geomCircle = new Geometry(wkbGeometryType.wkbLineString);
                                    int segments = 36;
                                    for (int i = 0; i <= segments; i++)
                                    {
                                        double angle = 2 * Math.PI * i / segments;
                                        double x = circle.Center.X + circle.Radius * Math.Cos(angle);
                                        double y = circle.Center.Y + circle.Radius * Math.Sin(angle);
                                        geomCircle.AddPoint_2D(x, y);
                                    }
                                    InsertarFeature(layerLineas, geomCircle, capaNombre, colorHex, "Circle", handle);
                                    countLineas++;
                                    break;
                                }

                            case Point pt:
                                {
                                    using var geomPt = new Geometry(wkbGeometryType.wkbPoint);
                                    geomPt.AddPoint_2D(pt.Location.X, pt.Location.Y);
                                    InsertarFeature(layerPuntos, geomPt, capaNombre, colorHex, "Point", handle);
                                    countPuntos++;
                                    break;
                                }

                            case Insert ins:
                                {
                                    using var geomIns = new Geometry(wkbGeometryType.wkbPoint);
                                    geomIns.AddPoint_2D(ins.InsertPoint.X, ins.InsertPoint.Y);
                                    InsertarFeature(layerPuntos, geomIns, capaNombre, colorHex, $"Block:{ins.Block?.Name ?? "Block"}", handle);
                                    countPuntos++;
                                    break;
                                }

                            case TextEntity txt:
                                {
                                    using var geomTxt = new Geometry(wkbGeometryType.wkbPoint);
                                    geomTxt.AddPoint_2D(txt.InsertPoint.X, txt.InsertPoint.Y);
                                    InsertarTextoFeature(layerTexto, geomTxt, capaNombre, colorHex, txt.Value, txt.Height, handle);
                                    countTexto++;
                                    break;
                                }

                            case MText mtxt:
                                {
                                    using var geomMTxt = new Geometry(wkbGeometryType.wkbPoint);
                                    geomMTxt.AddPoint_2D(mtxt.InsertPoint.X, mtxt.InsertPoint.Y);
                                    InsertarTextoFeature(layerTexto, geomMTxt, capaNombre, colorHex, mtxt.Value, mtxt.Height, handle);
                                    countTexto++;
                                    break;
                                }
                        }
                    }

                    if (countLineas > 0) capasExportadas.Add("CAD_Lineas");
                    if (countPoligonos > 0) capasExportadas.Add("CAD_Poligonos");
                    if (countPuntos > 0) capasExportadas.Add("CAD_Puntos");
                    if (countTexto > 0) capasExportadas.Add("CAD_Anotaciones");

                    destDs.FlushCache();
                }

                if (File.Exists(cacheGpkg))
                {
                    try { File.Delete(cacheGpkg); } catch { }
                }
                File.Move(tempGpkg, cacheGpkg);

                return new CadImportResult(true, cacheGpkg, capasExportadas, null, false, "ACadSharp (Autónomo)");
            });
        }

        private static void InsertarFeature(Layer layer, Geometry geom, string capa, string color, string tipo, string handle)
        {
            using var defn = layer.GetLayerDefn();
            using var feat = new Feature(defn);
            feat.SetGeometry(geom);
            feat.SetField("Capa", capa);
            feat.SetField("Color", color);
            feat.SetField("Tipo", tipo);
            feat.SetField("Handle", handle);
            layer.CreateFeature(feat);
        }

        private static void InsertarTextoFeature(Layer layer, Geometry geom, string capa, string color, string texto, double altura, string handle)
        {
            using var defn = layer.GetLayerDefn();
            using var feat = new Feature(defn);
            feat.SetGeometry(geom);
            feat.SetField("Capa", capa);
            feat.SetField("Color", color);
            feat.SetField("Texto", texto);
            feat.SetField("Altura", altura);
            feat.SetField("Handle", handle);
            layer.CreateFeature(feat);
        }

        private static string ObtenerColorHex(Entity ent)
        {
            var col = ent.Color;
            if (col.IsByLayer && ent.Layer != null)
            {
                col = ent.Layer.Color;
            }
            return $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        }
    }
}
