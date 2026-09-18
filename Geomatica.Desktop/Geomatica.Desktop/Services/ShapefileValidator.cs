using System;
using System.IO;
using Esri.ArcGISRuntime.Geometry;

namespace Geomatica.Desktop.Services
{
    /// <summary>
    /// Resultado de la validación estructural y espacial de un archivo Shapefile.
    /// </summary>
    public class ShapefileValidationResult
    {
        public bool PuedeCargar { get; set; }
        public bool TienePrjValido { get; set; }
        public string? MensajeError { get; set; }
        public string? MensajeAdvertencia { get; set; }
        public SpatialReference? SpatialReference { get; set; }
        public string CrsNombre { get; set; } = "Desconocido";
    }

    /// <summary>
    /// Validador estricto para archivos Shapefile (.shp) y sus sidecars obligatorios (.shx, .dbf, .prj).
    /// </summary>
    public static class ShapefileValidator
    {
        public static ShapefileValidationResult Validar(string shpPath)
        {
            var result = new ShapefileValidationResult();

            if (string.IsNullOrWhiteSpace(shpPath) || !File.Exists(shpPath))
            {
                result.PuedeCargar = false;
                result.MensajeError = $"El archivo Shapefile principal no existe: {shpPath}";
                return result;
            }

            var dir = Path.GetDirectoryName(shpPath) ?? "";
            var nameWithoutExt = Path.GetFileNameWithoutExtension(shpPath);

            var shxPath = Path.Combine(dir, nameWithoutExt + ".shx");
            var dbfPath = Path.Combine(dir, nameWithoutExt + ".dbf");
            var prjPath = Path.Combine(dir, nameWithoutExt + ".prj");

            // 1. Validar componentes binarios indispensables de la especificación ESRI Shapefile
            bool tieneShx = File.Exists(shxPath);
            bool tieneDbf = File.Exists(dbfPath);

            if (!tieneShx || !tieneDbf)
            {
                result.PuedeCargar = false;
                var faltantes = (!tieneShx && !tieneDbf) ? "archivos de índice (.shx) y atributos (.dbf)"
                              : (!tieneShx) ? "archivo de índice geométrico (.shx)"
                              : "archivo de atributos (.dbf)";
                result.MensajeError = $"El Shapefile '{nameWithoutExt}.shp' está incompleto. Falta el {faltantes}.";
                return result;
            }

            // 2. Validación estricta del archivo de proyección (.prj)
            bool tienePrj = File.Exists(prjPath);
            if (!tienePrj)
            {
                result.PuedeCargar = true; // Puede abrirse, pero sin CRS conocido
                result.TienePrjValido = false;
                result.MensajeAdvertencia = $"El archivo Shapefile '{nameWithoutExt}.shp' no contiene archivo de proyección (.prj). " +
                                            "El visor no puede garantizar que las geometrías se ubiquen en el lugar correcto del mapa.";
                return result;
            }

            try
            {
                var wkt = File.ReadAllText(prjPath).Trim();
                if (string.IsNullOrWhiteSpace(wkt))
                {
                    result.PuedeCargar = true;
                    result.TienePrjValido = false;
                    result.MensajeAdvertencia = $"El archivo de proyección '{nameWithoutExt}.prj' está vacío. " +
                                                "El visor no puede determinar el sistema de coordenadas de la capa.";
                    return result;
                }

                SpatialReference? sr = null;
                try
                {
                    sr = SpatialReference.Create(wkt);
                }
                catch { }

                if (sr == null)
                {
                    // Intentar extraer código EPSG del texto: AUTHORITY["EPSG","4326"] o ID["EPSG",4326]
                    var matches = System.Text.RegularExpressions.Regex.Matches(
                        wkt,
                        @"(?:AUTHORITY|ID)\[""EPSG""\s*,\s*""?(\d+)""?\]",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    for (int i = matches.Count - 1; i >= 0; i--)
                    {
                        if (int.TryParse(matches[i].Groups[1].Value, out int epsg) && epsg > 0)
                        {
                            try
                            {
                                sr = SpatialReference.Create(epsg);
                                if (sr != null) break;
                            }
                            catch { }
                        }
                    }
                }

                if (sr != null)
                {
                    result.PuedeCargar = true;
                    result.TienePrjValido = true;
                    result.SpatialReference = sr;
                    result.CrsNombre = sr.Wkid != 0 ? $"EPSG:{sr.Wkid}" : "WKT Personalizado";
                }
                else
                {
                    result.PuedeCargar = true;
                    result.TienePrjValido = false;
                    result.MensajeAdvertencia = $"No se pudo interpretar el sistema de coordenadas en '{nameWithoutExt}.prj'. " +
                                                "La definición WKT no fue reconocida por el motor cartográfico.";
                }
            }
            catch (Exception ex)
            {
                result.PuedeCargar = true;
                result.TienePrjValido = false;
                result.MensajeAdvertencia = $"Error al leer el archivo de proyección '{nameWithoutExt}.prj': {ex.Message}";
            }

            return result;
        }
    }
}
