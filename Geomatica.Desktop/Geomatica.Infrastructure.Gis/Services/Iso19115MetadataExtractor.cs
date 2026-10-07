using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Rasters;
using Esri.ArcGISRuntime.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Geomatica.Infrastructure.Gis.Services
{
    public class Iso19115MetadataResult
    {
        public string? SistemaReferencia { get; set; }
        public string? FormatoDatos { get; set; }
        public string? WktGeometry { get; set; }
        public string? Linaje { get; set; }
        public int TotalArchivosEspaciales { get; set; }
        public List<string> SubcarpetasExploradas { get; set; } = new();
    }

    public class Iso19115MetadataExtractor
    {
        /// <summary>
        /// Realiza una inspección profunda y recursiva en todas las subcarpetas del proyecto
        /// (incluyendo 'Datos_Espaciales', 'Cartografia', etc.) para extraer metadatos ISO 19115.
        /// </summary>
        public async Task<Iso19115MetadataResult> ExtraerDesdeRutaAsync(string rutaDirectorio)
        {
            var result = new Iso19115MetadataResult();

            if (string.IsNullOrWhiteSpace(rutaDirectorio) || !Directory.Exists(rutaDirectorio))
                return result;

            try
            {
                var dirInfo = new DirectoryInfo(rutaDirectorio);
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
                };

                // 1. Recolección recursiva de archivos sin riesgo de excepciones por permisos
                var todosLosArchivos = dirInfo.EnumerateFiles("*.*", enumOptions).ToList();

                var shapefiles = todosLosArchivos
                    .Where(f => f.Extension.Equals(".shp", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var rasters = todosLosArchivos
                    .Where(f => f.Extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".img", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".dem", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".jp2", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".ecw", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var geopackages = todosLosArchivos
                    .Where(f => f.Extension.Equals(".gpkg", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var cadFiles = todosLosArchivos
                    .Where(f => f.Extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var lidarFiles = todosLosArchivos
                    .Where(f => f.Extension.Equals(".las", StringComparison.OrdinalIgnoreCase) ||
                                f.Extension.Equals(".laz", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Recolección recursiva de File Geodatabases (.gdb)
                var gdbDirs = new List<DirectoryInfo>();
                try
                {
                    gdbDirs = dirInfo.EnumerateDirectories("*.gdb", enumOptions).ToList();
                }
                catch { }

                int totalEspaciales = shapefiles.Count + rasters.Count + geopackages.Count + cadFiles.Count + lidarFiles.Count + gdbDirs.Count;
                result.TotalArchivosEspaciales = totalEspaciales;

                if (totalEspaciales == 0)
                {
                    result.FormatoDatos = "Sin datos espaciales detectados";
                    result.Linaje = "Exploración recursiva finalizada sin encontrar fuentes o capas espaciales en el directorio o sus subcarpetas.";
                    return result;
                }

                // 2. Construcción del Formato de Datos
                var formatos = new List<string>();
                if (shapefiles.Any()) formatos.Add($"Vectorial (Shapefile: {shapefiles.Count})");
                if (rasters.Any()) formatos.Add($"Raster/Ortofoto (GeoTIFF/Raster: {rasters.Count})");
                if (gdbDirs.Any()) formatos.Add($"File Geodatabase ({gdbDirs.Count} GDB)");
                if (geopackages.Any()) formatos.Add($"GeoPackage ({geopackages.Count})");
                if (lidarFiles.Any()) formatos.Add($"LiDAR/Nube de puntos ({lidarFiles.Count})");
                if (cadFiles.Any()) formatos.Add($"CAD/Planos ({cadFiles.Count})");

                result.FormatoDatos = string.Join(", ", formatos);

                // 3. Subdirectorios que contienen datos espaciales
                var carpetasConDatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in shapefiles.Concat(rasters).Concat(geopackages).Concat(cadFiles).Concat(lidarFiles))
                {
                    var rel = Path.GetRelativePath(rutaDirectorio, f.DirectoryName ?? rutaDirectorio);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                        carpetasConDatos.Add(rel.Replace('\\', '/'));
                }
                foreach (var g in gdbDirs)
                {
                    var rel = Path.GetRelativePath(rutaDirectorio, g.Parent?.FullName ?? rutaDirectorio);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                        carpetasConDatos.Add(rel.Replace('\\', '/'));
                }
                result.SubcarpetasExploradas = carpetasConDatos.OrderBy(c => c).ToList();

                // 4. Priorización de archivos para extracción del SRID y Extensión
                // Priorizar archivos en "Datos_Espaciales", "Cartografia", "Vectores", etc.
                var candidatosPriorizados = shapefiles
                    .OrderByDescending(f => EsSubcarpetaEspacialPrioritaria(f.FullName))
                    .Cast<FileSystemInfo>()
                    .Concat(rasters.OrderByDescending(f => EsSubcarpetaEspacialPrioritaria(f.FullName)))
                    .Concat(geopackages.OrderByDescending(f => EsSubcarpetaEspacialPrioritaria(f.FullName)))
                    .ToList();

                SpatialReference? srEncontrado = null;
                Envelope? extentEncontrado = null;
                string? archivoReferencia = null;

                // 5. Búsqueda exhaustiva del SRID en candidatos
                foreach (var candidato in candidatosPriorizados)
                {
                    if (candidato is FileInfo fi)
                    {
                        var ext = fi.Extension.ToLowerInvariant();

                        // A. SHAPEFILES: Primero intentar PRJ directo (rápido y fiable)
                        if (ext == ".shp")
                        {
                            var prjPath = Path.ChangeExtension(fi.FullName, ".prj");
                            if (File.Exists(prjPath))
                            {
                                srEncontrado = ExtraerSrDesdePrj(prjPath);
                            }

                            // Si no se obtuvo del PRJ, cargar con ArcGIS Runtime
                            if (srEncontrado == null || extentEncontrado == null)
                            {
                                try
                                {
                                    var shpTable = new ShapefileFeatureTable(fi.FullName);
                                    await shpTable.LoadAsync();
                                    if (srEncontrado == null && shpTable.SpatialReference != null)
                                        srEncontrado = shpTable.SpatialReference;
                                    if (extentEncontrado == null && shpTable.Extent != null)
                                        extentEncontrado = shpTable.Extent;
                                }
                                catch { }
                            }

                            if (srEncontrado != null)
                            {
                                archivoReferencia = Path.GetRelativePath(rutaDirectorio, fi.FullName).Replace('\\', '/');
                                break;
                            }
                        }
                        // B. RASTERS (GeoTIFF)
                        else if (ext == ".tif" || ext == ".tiff" || ext == ".img" || ext == ".jp2")
                        {
                            try
                            {
                                var raster = new Raster(fi.FullName);
                                var rasterLayer = new RasterLayer(raster);
                                await rasterLayer.LoadAsync();

                                if (rasterLayer.SpatialReference != null)
                                    srEncontrado = rasterLayer.SpatialReference;
                                if (rasterLayer.FullExtent != null)
                                    extentEncontrado = rasterLayer.FullExtent;
                            }
                            catch { }

                            if (srEncontrado != null)
                            {
                                archivoReferencia = Path.GetRelativePath(rutaDirectorio, fi.FullName).Replace('\\', '/');
                                break;
                            }
                        }
                        // C. GEOPACKAGES
                        else if (ext == ".gpkg")
                        {
                            try
                            {
                                var gpkg = await GeoPackage.OpenAsync(fi.FullName);
                                var featTable = gpkg.GeoPackageFeatureTables.FirstOrDefault();
                                if (featTable != null)
                                {
                                    await featTable.LoadAsync();
                                    srEncontrado = featTable.SpatialReference;
                                    extentEncontrado = featTable.Extent;
                                }
                            }
                            catch { }

                            if (srEncontrado != null)
                            {
                                archivoReferencia = Path.GetRelativePath(rutaDirectorio, fi.FullName).Replace('\\', '/');
                                break;
                            }
                        }
                    }
                }

                // 6. Formateo amigable del SRID conforme a estándares colombianos / EPSG
                if (srEncontrado != null)
                {
                    result.SistemaReferencia = FormatearNombreSrid(srEncontrado);
                }
                else
                {
                    result.SistemaReferencia = "No especificado / Sin archivo .prj";
                }

                // 7. Geometría WKT Bounding Box si se detectó extensión
                if (extentEncontrado != null)
                {
                    result.WktGeometry = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "POLYGON(({0} {1}, {2} {1}, {2} {3}, {0} {3}, {0} {1}))",
                        extentEncontrado.XMin, extentEncontrado.YMin,
                        extentEncontrado.XMax, extentEncontrado.YMax);
                }

                // 8. Construcción de Linaje completo y profesional (ISO 19115)
                var subcarpetasTexto = carpetasConDatos.Any()
                    ? string.Join(", ", carpetasConDatos.Take(4).Select(c => $"'{c}'")) + (carpetasConDatos.Count > 4 ? $" (+{carpetasConDatos.Count - 4} más)" : "")
                    : "raíz del proyecto";

                var refTexto = !string.IsNullOrWhiteSpace(archivoReferencia)
                    ? $"Sistema de referencia detectado a partir del insumo: '{archivoReferencia}'."
                    : "No se identificó archivo de proyección (.prj) en los insumos espaciales.";

                result.Linaje = $"Inspección profunda en subdirectorios del proyecto ({subcarpetasTexto}). " +
                                $"Identificadas {totalEspaciales} fuentes de datos espaciales. {refTexto} " +
                                "Documentación y linaje generados automáticamente conforme a la norma ISO 19115-1:2014.";
            }
            catch (Exception ex)
            {
                result.Linaje = $"Error durante la exploración recursiva de metadatos: {ex.Message}";
            }

            return result;
        }

        private static bool EsSubcarpetaEspacialPrioritaria(string fullPath)
        {
            return fullPath.IndexOf("Datos_Espaciales", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullPath.IndexOf("Cartografia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullPath.IndexOf("Vectores", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullPath.IndexOf("Rasters", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fullPath.IndexOf("Capas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SpatialReference? ExtraerSrDesdePrj(string prjPath)
        {
            try
            {
                var wkt = File.ReadAllText(prjPath).Trim();
                if (string.IsNullOrWhiteSpace(wkt)) return null;

                // 1. Extraer código EPSG explícito mediante Regex primero para resolver el WKID oficial
                var match = Regex.Match(wkt, @"(?:AUTHORITY|ID)\[""EPSG""\s*,\s*""?(\d+)""?\]", RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int epsg) && epsg > 0)
                {
                    try
                    {
                        var srEpsg = SpatialReference.Create(epsg);
                        if (srEpsg != null) return srEpsg;
                    }
                    catch { }
                }

                // 2. Si no se halló tag EPSG, crear desde WKT genérico
                try
                {
                    var sr = SpatialReference.Create(wkt);
                    if (sr != null && (sr.Wkid > 0 || !string.IsNullOrWhiteSpace(sr.WkText)))
                        return sr;
                }
                catch { }
            }
            catch { }

            return null;
        }

        private static string FormatearNombreSrid(SpatialReference sr)
        {
            int wkid = sr.Wkid;
            return wkid switch
            {
                9377 => "EPSG:9377 (MAGNA-SIRGAS / Origen Nacional)",
                3116 => "EPSG:3116 (MAGNA-SIRGAS / Colombia Bogotá)",
                3115 => "EPSG:3115 (MAGNA-SIRGAS / Colombia Oeste)",
                3117 => "EPSG:3117 (MAGNA-SIRGAS / Colombia Este)",
                3118 => "EPSG:3118 (MAGNA-SIRGAS / Colombia Este Este)",
                3114 => "EPSG:3114 (MAGNA-SIRGAS / Colombia Far West)",
                4326 => "EPSG:4326 (WGS 84 - Coordenadas Geográficas)",
                4686 => "EPSG:4686 (MAGNA-SIRGAS Geográficas)",
                3857 => "EPSG:3857 (WGS 84 / Pseudo-Mercator)",
                > 0 => $"EPSG:{wkid}",
                _ => !string.IsNullOrWhiteSpace(sr.WkText) ? sr.WkText : "Sistema de Coordenadas Personalizado"
            };
        }
    }
}
