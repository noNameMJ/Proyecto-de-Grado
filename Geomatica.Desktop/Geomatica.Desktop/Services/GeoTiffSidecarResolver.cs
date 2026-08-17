using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Rasters;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Geomatica.Desktop.Services;

public sealed record GeoTiffSidecarResolution(Envelope? Envelope, SpatialReference? SpatialReference, string? Error)
{
    public bool IsValid => Envelope != null && SpatialReference != null && string.IsNullOrEmpty(Error);
}

public static class GeoTiffSidecarResolver
{
    private static readonly Regex EpsgRegex = new(
        "(?:AUTHORITY|ID)\\s*\\[\\s*[\\\"']EPSG[\\\"']\\s*,\\s*[\\\"']?(?<wkid>\\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ObtenerRutaRasterCache(string tifPath)
        => Path.Combine(ObtenerDirectorioCacheRaster(tifPath), Path.GetFileName(tifPath));

    public static async Task AsegurarAuxXmlGeorreferenciadoAsync(string tifPath)
    {
        var directory = Path.GetDirectoryName(tifPath);
        var name = Path.GetFileNameWithoutExtension(tifPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name)) return;

        var prjPath = Path.Combine(directory, name + ".prj");
        var tfwPath = BuscarWorldFile(directory, name);
        if (!File.Exists(prjPath) || tfwPath == null) return;

        var wkt = await File.ReadAllTextAsync(prjPath);
        var values = await LeerCoeficientesAsync(tfwPath);
        var a = values[0];
        var d = values[1];
        var b = values[2];
        var e = values[3];
        var c = values[4];
        var f = values[5];

        var cacheRasterPath = ObtenerRutaRasterCache(tifPath);
        MaterializarCacheRaster(tifPath, cacheRasterPath, prjPath, tfwPath);

        var cacheAuxXmlPath = cacheRasterPath + ".aux.xml";
        var escapedWkt = SecurityElement.Escape(wkt) ?? string.Empty;
        var geoTransform = string.Join(", ", new[]
        {
            c, a, b, f, d, e
        }.Select(value => value.ToString("G17", CultureInfo.InvariantCulture)));
        var auxXml = $"<PAMDataset>\r\n  <SRS>{escapedWkt}</SRS>\r\n  <GeoTransform>{geoTransform}</GeoTransform>\r\n</PAMDataset>\r\n";

        await File.WriteAllTextAsync(cacheAuxXmlPath, auxXml);
    }

    private static void MaterializarCacheRaster(string tifPath, string cacheRasterPath, string prjPath, string tfwPath)
    {
        var cacheDirectory = Path.GetDirectoryName(cacheRasterPath);
        if (string.IsNullOrWhiteSpace(cacheDirectory))
            throw new InvalidOperationException("No se pudo determinar el directorio de caché del raster.");

        Directory.CreateDirectory(cacheDirectory);

        if (!File.Exists(cacheRasterPath) || NecesitaActualizarCache(tifPath, cacheRasterPath))
            File.Copy(tifPath, cacheRasterPath, true);

        CopiarArchivoSiExiste(prjPath, Path.Combine(cacheDirectory, Path.GetFileName(prjPath)));
        CopiarArchivoSiExiste(tfwPath, Path.Combine(cacheDirectory, Path.GetFileName(tfwPath)));
    }

    private static bool NecesitaActualizarCache(string sourcePath, string cacheRasterPath)
    {
        if (!File.Exists(cacheRasterPath))
            return true;

        var sourceInfo = new FileInfo(sourcePath);
        var cacheInfo = new FileInfo(cacheRasterPath);
        return sourceInfo.Length != cacheInfo.Length || sourceInfo.LastWriteTimeUtc > cacheInfo.LastWriteTimeUtc;
    }

    private static void CopiarArchivoSiExiste(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
            return;

        if (!File.Exists(destinationPath))
        {
            File.Copy(sourcePath, destinationPath, true);
            return;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var destinationInfo = new FileInfo(destinationPath);
        if (sourceInfo.Length != destinationInfo.Length || sourceInfo.LastWriteTimeUtc > destinationInfo.LastWriteTimeUtc)
            File.Copy(sourcePath, destinationPath, true);
    }

    private static string ObtenerDirectorioCacheRaster(string tifPath)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "Geomatica", "RasterCache");
        return Path.Combine(cacheRoot, ObtenerCacheKey(tifPath));
    }

    private static string ObtenerCacheKey(string tifPath)
    {
        var normalizedPath = Path.GetFullPath(tifPath).Trim().ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hash).Substring(0, 16);
    }

    public static GeoTiffSidecarResolution Resolve(string tiffPath, RasterInfo rasterInfo)
    {
        var cacheRasterPath = ObtenerRutaRasterCache(tiffPath);
        var directory = Path.GetDirectoryName(cacheRasterPath);
        var name = Path.GetFileNameWithoutExtension(cacheRasterPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name))
            return new(null, null, "No se pudo determinar el directorio del raster.");

        var prjPath = Path.Combine(directory, name + ".prj");
        var tfwPath = BuscarWorldFile(directory, name);
        if (!File.Exists(prjPath) || tfwPath == null)
            return new(null, null, "El TIFF requiere sidecars .prj y .tfw para resolver su georreferenciación.");

        try
        {
            if (!TryGetDimensions(rasterInfo, cacheRasterPath + ".aux.xml", out var width, out var height))
                return new(null, null, "ArcGIS Runtime no informó dimensiones de píxel y el archivo .aux.xml no las contiene.");

            var wkt = File.ReadAllText(prjPath);
            var wkidMatch = EpsgRegex.Matches(wkt).Cast<Match>().LastOrDefault();
            if (wkidMatch == null || !int.TryParse(wkidMatch.Groups["wkid"].Value, out var wkid))
                return new(null, null, "No se encontró un código EPSG/WKID válido en el archivo .prj.");

            var spatialReference = SpatialReference.Create(wkid);
            if (spatialReference == null)
                return new(null, null, $"ArcGIS Runtime no reconoce el WKID {wkid} definido en el .prj.");

            var coefficients = LeerCoeficientes(tfwPath);

            var envelope = CalcularEnvelope(coefficients, width, height, spatialReference);
            if (!EsEnvelopeValido(envelope) || EsEspacioDePixeles(envelope, width, height))
                return new(null, null, "El .tfw produce un extent inválido o en coordenadas de píxel local.");

            return new(envelope, spatialReference, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or System.Xml.XmlException)
        {
            return new(null, null, $"No se pudieron leer los sidecars del TIFF: {ex.Message}");
        }
    }

    private static bool TryGetDimensions(RasterInfo rasterInfo, string auxXmlPath, out int width, out int height)
    {
        width = 0;
        height = 0;
        var extent = rasterInfo.Extent;
        if (rasterInfo.SpatialReference == null && extent != null
            && extent.Width > 0 && extent.Height > 0
            && Math.Abs(extent.Width - Math.Round(extent.Width)) < 0.001
            && Math.Abs(extent.Height - Math.Round(extent.Height)) < 0.001)
        {
            width = (int)Math.Round(extent.Width);
            height = (int)Math.Round(extent.Height);
            return true;
        }

        if (!File.Exists(auxXmlPath)) return false;
        var document = XDocument.Load(auxXmlPath);
        var pamDataset = document.Descendants("PAMDataset").FirstOrDefault();
        var widthText = pamDataset?.Attribute("rasterXSize")?.Value ?? document.Descendants("RasterXSize").FirstOrDefault()?.Value;
        var heightText = pamDataset?.Attribute("rasterYSize")?.Value ?? document.Descendants("RasterYSize").FirstOrDefault()?.Value;
        return int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
            && width > 0
            && height > 0;
    }

    private static string? BuscarWorldFile(string directory, string name)
    {
        foreach (var extension in new[] { ".tfw", ".tifw", ".wld" })
        {
            var candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static double[] LeerCoeficientes(string tfwPath)
    {
        var coefficients = File.ReadAllLines(tfwPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => double.Parse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
        if (coefficients.Length != 6)
            throw new FormatException("El archivo .tfw debe contener exactamente seis coeficientes afines.");
        return coefficients;
    }

    private static async Task<double[]> LeerCoeficientesAsync(string tfwPath)
    {
        var coefficients = (await File.ReadAllLinesAsync(tfwPath))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => double.Parse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
        if (coefficients.Length != 6)
            throw new FormatException("El archivo .tfw debe contener exactamente seis coeficientes afines.");
        return coefficients;
    }

    private static Envelope CalcularEnvelope(double[] values, int width, int height, SpatialReference spatialReference)
    {
        var a = values[0];
        var d = values[1];
        var b = values[2];
        var e = values[3];
        var c = values[4];
        var f = values[5];
        var corners = new[] { (-0.5d, -0.5d), (width - 0.5d, -0.5d), (width - 0.5d, height - 0.5d), (-0.5d, height - 0.5d) };
        var points = corners.Select(pixel => new MapPoint(
            a * pixel.Item1 + b * pixel.Item2 + c,
            d * pixel.Item1 + e * pixel.Item2 + f,
            spatialReference));

        return new Envelope(points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y), spatialReference);
    }

    private static bool EsEnvelopeValido(Envelope envelope)
        => double.IsFinite(envelope.XMin)
           && double.IsFinite(envelope.YMin)
           && double.IsFinite(envelope.XMax)
           && double.IsFinite(envelope.YMax)
           && envelope.XMax > envelope.XMin
           && envelope.YMax > envelope.YMin;

    private static bool EsEspacioDePixeles(Envelope envelope, int width, int height)
        => Math.Abs(envelope.XMin + 0.5d) < 1d
           && Math.Abs(envelope.YMax + 0.5d) < 1d
           && Math.Abs(envelope.Width - width) < 1d
           && Math.Abs(envelope.Height - height) < 1d;
}
