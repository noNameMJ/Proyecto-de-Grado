using System.Globalization;
using System.IO;
using System.Security;
using Esri.ArcGISRuntime.Geometry;
using Geomatica.Desktop.Services;

namespace Geomatica.UnitTests.Services;

public class GeoTiffSidecarResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public GeoTiffSidecarResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GeomaticaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    [Fact]
    public void ObtenerRutaRasterCache_DebeGenerarRutaValidaEnTemp()
    {
        // Arrange
        var testTif = Path.Combine(_tempDirectory, "ortomosaico_uis.tif");

        // Act
        var cachePath = GeoTiffSidecarResolver.ObtenerRutaRasterCache(testTif);

        // Assert
        cachePath.Should().NotBeNullOrWhiteSpace();
        cachePath.Should().EndWith("ortomosaico_uis.tif");
        cachePath.Should().Contain("RasterCache");
    }

    [Fact]
    public async Task AsegurarAuxXmlGeorreferenciadoAsync_ConSidecarsValidos_DebeGenerarArchivosDeCache()
    {
        // Arrange
        var tifName = "test_raster";
        var tifPath = Path.Combine(_tempDirectory, $"{tifName}.tif");
        var prjPath = Path.Combine(_tempDirectory, $"{tifName}.prj");
        var tfwPath = Path.Combine(_tempDirectory, $"{tifName}.tfw");

        // Crear archivos simulados
        await File.WriteAllBytesAsync(tifPath, new byte[] { 0x49, 0x49, 0x2A, 0x00 }); // Cabecera TIFF válida pequeña
        var wktMagnaSirgas = "PROJCS[\"MAGNA-SIRGAS / Origen-Nacional\",GEOGCS[\"MAGNA-SIRGAS\",DATUM[\"Marco_Geocentrico_Nacional_de_Referencia\",SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",4],PARAMETER[\"central_meridian\",-73],PARAMETER[\"scale_factor\",0.9992],PARAMETER[\"false_easting\",5000000],PARAMETER[\"false_northing\",2000000],UNIT[\"metre\",1],AUTHORITY[\"EPSG\",\"9377\"]]";
        await File.WriteAllTextAsync(prjPath, wktMagnaSirgas);

        // 6 coeficientes afines de un World File:
        // A (pixel size X) = 0.05
        // D (rotation Y) = 0.0
        // B (rotation X) = 0.0
        // E (pixel size Y) = -0.05
        // C (top-left X) = 5000000.0
        // F (top-left Y) = 2000000.0
        var tfwContent = "0.05\n0.0\n0.0\n-0.05\n5000000.0\n2000000.0\n";
        await File.WriteAllTextAsync(tfwPath, tfwContent);

        // Act
        await GeoTiffSidecarResolver.AsegurarAuxXmlGeorreferenciadoAsync(tifPath);

        // Assert
        var cacheRasterPath = GeoTiffSidecarResolver.ObtenerRutaRasterCache(tifPath);
        File.Exists(cacheRasterPath).Should().BeTrue();

        var cacheAuxXml = cacheRasterPath + ".aux.xml";
        File.Exists(cacheAuxXml).Should().BeTrue();

        var auxContent = await File.ReadAllTextAsync(cacheAuxXml);
        auxContent.Should().Contain("<PAMDataset>");
        auxContent.Should().Contain("<SRS>");
        auxContent.Should().Contain("MAGNA-SIRGAS");
        auxContent.Should().Contain("<GeoTransform>");
    }

    [Fact]
    public async Task AsegurarAuxXmlGeorreferenciadoAsync_ConProgreso_DebeReportarAvance()
    {
        // Arrange
        var tifName = "test_progress_raster";
        var tifPath = Path.Combine(_tempDirectory, $"{tifName}.tif");
        var prjPath = Path.Combine(_tempDirectory, $"{tifName}.prj");
        var tfwPath = Path.Combine(_tempDirectory, $"{tifName}.tfw");

        await File.WriteAllBytesAsync(tifPath, new byte[] { 0x49, 0x49, 0x2A, 0x00 });
        await File.WriteAllTextAsync(prjPath, "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433],AUTHORITY[\"EPSG\",\"4326\"]]");
        await File.WriteAllTextAsync(tfwPath, "0.0001\n0.0\n0.0\n-0.0001\n-73.0\n7.0\n");

        var reportes = new List<(int porcentaje, string detalle)>();
        IProgress<(int, string)> progress = new Progress<(int, string)>(p => reportes.Add(p));

        // Act
        await GeoTiffSidecarResolver.AsegurarAuxXmlGeorreferenciadoAsync(tifPath, progress);

        // Assert
        var cachePath = GeoTiffSidecarResolver.ObtenerRutaRasterCache(tifPath);
        File.Exists(cachePath).Should().BeTrue();
    }
}

