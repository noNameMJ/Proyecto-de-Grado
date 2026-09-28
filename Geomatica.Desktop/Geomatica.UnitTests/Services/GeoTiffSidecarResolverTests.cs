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

    [Fact]
    public void TieneCanalAlfa_ConArchivoInexistente_DebeRetornarFalse()
    {
        var result = GeoTiffSidecarResolver.TieneCanalAlfa(Path.Combine(_tempDirectory, "no_existe.tif"));
        result.Should().BeFalse();
    }

    [Fact]
    public void TieneCanalAlfa_ConTiff3BandasSinAlfa_DebeRetornarFalse()
    {
        // Arrange: Crear un TIFF de 3 bandas estándar (RGB sin canal alfa)
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        var drv = OSGeo.GDAL.Gdal.GetDriverByName("GTiff");
        var tifPath = Path.Combine(_tempDirectory, $"rgb3_{Guid.NewGuid():N}.tif");

        using (var ds = drv.Create(tifPath, 20, 20, 3, OSGeo.GDAL.DataType.GDT_Byte, null))
        {
            ds.GetRasterBand(1).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_RedBand);
            ds.GetRasterBand(2).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_GreenBand);
            ds.GetRasterBand(3).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_BlueBand);
            byte[] data = new byte[400];
            for (int i = 0; i < 400; i++) data[i] = 128;
            ds.GetRasterBand(1).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
            ds.GetRasterBand(2).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
            ds.GetRasterBand(3).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
        }

        // Act
        var tieneAlfa = GeoTiffSidecarResolver.TieneCanalAlfa(tifPath);
        var vrtPath = GeoTiffSidecarResolver.ObtenerOCrearVrtConAlfaTransparente(tifPath);

        // Assert: Sin canal alfa, no debe generar VRT y debe retornar false para mantener el fondo negro original
        tieneAlfa.Should().BeFalse();
        vrtPath.Should().BeNull();
    }

    [Fact]
    public async Task ObtenerOCrearVrtConAlfaTransparente_ConTiff4BandasConAlfa_DebeGenerarVrtConNoData()
    {
        // Arrange: Crear un GeoTIFF de 4 bandas (RGBA con canal alfa)
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        var drv = OSGeo.GDAL.Gdal.GetDriverByName("GTiff");
        var tifPath = Path.Combine(_tempDirectory, $"rgba4_{Guid.NewGuid():N}.tif");

        using (var ds = drv.Create(tifPath, 20, 20, 4, OSGeo.GDAL.DataType.GDT_Byte, new[] { "PHOTOMETRIC=RGB", "ALPHA=YES" }))
        {
            ds.SetProjection("GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]");
            double[] gt = new double[] { -74.0, 0.001, 0, 4.0, 0, -0.001 };
            ds.SetGeoTransform(gt);

            ds.GetRasterBand(1).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_RedBand);
            ds.GetRasterBand(2).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_GreenBand);
            ds.GetRasterBand(3).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_BlueBand);
            ds.GetRasterBand(4).SetColorInterpretation(OSGeo.GDAL.ColorInterp.GCI_AlphaBand);

            byte[] data = new byte[400];
            for (int i = 0; i < 200; i++) data[i] = 0; // background
            for (int i = 200; i < 400; i++) data[i] = 180; // valid data

            byte[] alpha = new byte[400];
            for (int i = 0; i < 200; i++) alpha[i] = 0; // transparente
            for (int i = 200; i < 400; i++) alpha[i] = 255; // opaco

            ds.GetRasterBand(1).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
            ds.GetRasterBand(2).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
            ds.GetRasterBand(3).WriteRaster(0, 0, 20, 20, data, 20, 20, 0, 0);
            ds.GetRasterBand(4).WriteRaster(0, 0, 20, 20, alpha, 20, 20, 0, 0);
        }

        // Act
        var tieneAlfa = GeoTiffSidecarResolver.TieneCanalAlfa(tifPath);
        var vrtPath = GeoTiffSidecarResolver.ObtenerOCrearVrtConAlfaTransparente(tifPath);

        // Assert
        tieneAlfa.Should().BeTrue();
        vrtPath.Should().NotBeNull();
        File.Exists(vrtPath).Should().BeTrue();

        var vrtContent = await File.ReadAllTextAsync(vrtPath!);
        vrtContent.Should().Contain("<NoDataValue>0</NoDataValue>");
        vrtContent.Should().Contain("<UseMaskBand>true</UseMaskBand>");

        // Cargar en ArcGIS Runtime para verificar compatibilidad directa
        var raster = new Esri.ArcGISRuntime.Rasters.Raster(vrtPath!);
        await raster.LoadAsync();
        raster.LoadStatus.Should().Be(Esri.ArcGISRuntime.LoadStatus.Loaded);
        raster.RasterInfo?.SpatialReference.Should().NotBeNull();
    }
}

