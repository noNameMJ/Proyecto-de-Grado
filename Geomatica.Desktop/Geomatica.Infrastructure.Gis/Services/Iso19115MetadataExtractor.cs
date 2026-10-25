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
using Geomatica.Domain.Catalogos;

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
        /// Si se provee <paramref name="archivosEspacialesFisicos"/> (por ejemplo, proveniente de un índice ya construido),
        /// se reutilizan directamente en memoria evitando volver a enumerar el sistema de archivos físico o SMB.
        /// </summary>
        public async Task<Iso19115MetadataResult> ExtraerDesdeRutaAsync(
            string rutaDirectorio,
            IEnumerable<string>? archivosEspacialesFisicos = null,
            IEnumerable<string>? directoriosGdbFisicos = null)
        {
            var result = new Iso19115MetadataResult();

            if (string.IsNullOrWhiteSpace(rutaDirectorio) || !Directory.Exists(rutaDirectorio))
                return result;

            try
            {
                List<string> listaArchivos;
                List<string> gdbDirs;

                if (archivosEspacialesFisicos != null)
                {
                    listaArchivos = archivosEspacialesFisicos.ToList();
                    gdbDirs = (directoriosGdbFisicos ?? Enumerable.Empty<string>()).ToList();
                }
                else
                {
                    var dirInfo = new DirectoryInfo(rutaDirectorio);
                    var enumOptions = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 6,
                        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System
                    };

                    listaArchivos = dirInfo.EnumerateFiles("*.*", enumOptions)
                        .Where(f => !f.FullName.Contains(".gdb" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                    !f.FullName.Contains(".gdb" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        .Select(f => f.FullName)
                        .ToList();

                    try
                    {
                        gdbDirs = dirInfo.EnumerateDirectories("*.gdb", enumOptions)
                            .Select(d => d.FullName)
                            .ToList();
                    }
                    catch { gdbDirs = new List<string>(); }
                }

                // 1. Clasificación canónica usando CatalogoFormatosGis
                var shapefiles = listaArchivos.Where(f => CatalogoFormatosGis.EsShapefile(Path.GetExtension(f))).ToList();
                var rasters = listaArchivos.Where(f => CatalogoFormatosGis.EsRaster(Path.GetExtension(f))).ToList();
                var geopackages = listaArchivos.Where(f => CatalogoFormatosGis.EsGeoPackage(Path.GetExtension(f))).ToList();
                var cadFiles = listaArchivos.Where(f => CatalogoFormatosGis.EsCad(Path.GetExtension(f))).ToList();
                var lidarFiles = listaArchivos.Where(f => CatalogoFormatosGis.EsLidar(Path.GetExtension(f))).ToList();

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
                    var dir = Path.GetDirectoryName(f) ?? rutaDirectorio;
                    var rel = Path.GetRelativePath(rutaDirectorio, dir);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                        carpetasConDatos.Add(rel.Replace('\\', '/'));
                }
                foreach (var g in gdbDirs)
                {
                    var dir = Path.GetDirectoryName(g) ?? rutaDirectorio;
                    var rel = Path.GetRelativePath(rutaDirectorio, dir);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                        carpetasConDatos.Add(rel.Replace('\\', '/'));
                }
                result.SubcarpetasExploradas = carpetasConDatos.OrderBy(c => c).ToList();

                // 4. Priorización de archivos para extracción del SRID y Extensión
                var candidatosPriorizados = shapefiles
                    .OrderByDescending(EsSubcarpetaEspacialPrioritaria)
                    .Concat(rasters.OrderByDescending(EsSubcarpetaEspacialPrioritaria))
                    .Concat(geopackages.OrderByDescending(EsSubcarpetaEspacialPrioritaria))
                    .ToList();

                SpatialReference? srEncontrado = null;
                Envelope? extentEncontrado = null;
                string? archivoReferencia = null;

                // 5. Búsqueda exhaustiva del SRID en candidatos
                foreach (var candidato in candidatosPriorizados)
                {
                    var ext = Path.GetExtension(candidato).ToLowerInvariant();

                    // A. SHAPEFILES: Primero intentar PRJ directo (rápido y fiable)
                    if (ext == ".shp")
                    {
                        var prjPath = Path.ChangeExtension(candidato, ".prj");
                        if (File.Exists(prjPath))
                        {
                            srEncontrado = ExtraerSrDesdePrj(prjPath);
                        }

                        // Si no se obtuvo del PRJ, cargar con ArcGIS Runtime
                        if (srEncontrado == null || extentEncontrado == null)
                        {
                            ShapefileFeatureTable? shpTable = null;
                            try
                            {
                                shpTable = new ShapefileFeatureTable(candidato);
                                await shpTable.LoadAsync();
                                if (srEncontrado == null && shpTable.SpatialReference != null)
                                    srEncontrado = shpTable.SpatialReference;
                                if (extentEncontrado == null && shpTable.Extent != null)
                                    extentEncontrado = shpTable.Extent;
                            }
                            catch { }
                            finally
                            {
                                try { shpTable?.Close(); } catch { }
                            }
                        }

                        if (srEncontrado != null)
                        {
                            archivoReferencia = Path.GetRelativePath(rutaDirectorio, candidato).Replace('\\', '/');
                            break;
                        }
                    }
                    // B. RASTERS (GeoTIFF)
                    else if (ext == ".tif" || ext == ".tiff" || ext == ".img" || ext == ".jp2")
                    {
                        try
                        {
                            var raster = new Raster(candidato);
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
                            archivoReferencia = Path.GetRelativePath(rutaDirectorio, candidato).Replace('\\', '/');
                            break;
                        }
                    }
                    // C. GEOPACKAGES
                    else if (ext == ".gpkg")
                    {
                        GeoPackage? gpkg = null;
                        try
                        {
                            gpkg = await GeoPackage.OpenAsync(candidato);
                            var featTable = gpkg.GeoPackageFeatureTables.FirstOrDefault();
                            if (featTable != null)
                            {
                                await featTable.LoadAsync();
                                srEncontrado = featTable.SpatialReference;
                                extentEncontrado = featTable.Extent;
                            }
                        }
                        catch { }
                        finally
                        {
                            try { gpkg?.Close(); } catch { }
                        }

                        if (srEncontrado != null)
                        {
                            archivoReferencia = Path.GetRelativePath(rutaDirectorio, candidato).Replace('\\', '/');
                            break;
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
