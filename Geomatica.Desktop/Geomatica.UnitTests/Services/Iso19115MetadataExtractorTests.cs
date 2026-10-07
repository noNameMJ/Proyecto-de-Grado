using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Geomatica.Infrastructure.Gis.Services;
using Xunit;

namespace Geomatica.UnitTests.Services
{
    public class Iso19115MetadataExtractorTests : IDisposable
    {
        private readonly string _tempDir;

        public Iso19115MetadataExtractorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Geomatica_Iso19115_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task ExtraerDesdeRutaAsync_DirectorioVacio_RetornaSinDatos()
        {
            var extractor = new Iso19115MetadataExtractor();
            var result = await extractor.ExtraerDesdeRutaAsync(_tempDir);

            result.Should().NotBeNull();
            result.TotalArchivosEspaciales.Should().Be(0);
            result.FormatoDatos.Should().Contain("Sin datos espaciales");
        }

        [Fact]
        public async Task ExtraerDesdeRutaAsync_ArchivosEnSubcarpetasProfundas_DetectaRecursivamente()
        {
            // Crear estructura profunda: Datos_Espaciales/Vectores/2024
            var subDirVectores = Path.Combine(_tempDir, "Datos_Espaciales", "Vectores", "2024");
            var subDirRasters = Path.Combine(_tempDir, "Datos_Espaciales", "Rasters");
            Directory.CreateDirectory(subDirVectores);
            Directory.CreateDirectory(subDirRasters);

            // Crear un shapefile simulado con su .prj en la subcarpeta profunda
            var shpPath = Path.Combine(subDirVectores, "predios_urbanos.shp");
            var prjPath = Path.Combine(subDirVectores, "predios_urbanos.prj");
            File.WriteAllText(shpPath, "dummy shapefile content");
            // WKT de Origen Nacional (EPSG:9377)
            File.WriteAllText(prjPath, "PROJCS[\"MAGNA-SIRGAS / Origen Nacional\",GEOGCS[\"MAGNA-SIRGAS\",DATUM[\"Marco_Geocentrico_Nacional_de_Referencia\",SPHEROID[\"GRS 1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",5000000.0],PARAMETER[\"False_Northing\",2000000.0],PARAMETER[\"Central_Meridian\",-73.0],PARAMETER[\"Scale_Factor\",0.9992],PARAMETER[\"Latitude_Of_Origin\",4.0],UNIT[\"Meter\",1.0],AUTHORITY[\"EPSG\",\"9377\"]]");

            // Crear un raster simulado en la subcarpeta de rasters
            var tifPath = Path.Combine(subDirRasters, "ortofoto_campus.tif");
            File.WriteAllText(tifPath, "dummy geotiff content");

            var extractor = new Iso19115MetadataExtractor();
            var result = await extractor.ExtraerDesdeRutaAsync(_tempDir);

            result.Should().NotBeNull();
            result.TotalArchivosEspaciales.Should().Be(2);
            result.FormatoDatos.Should().Contain("Shapefile");
            result.FormatoDatos.Should().Contain("Raster");
            result.SistemaReferencia.Should().Contain("9377");
            result.Linaje.Should().Contain("Datos_Espaciales");
            result.Linaje.Should().Contain("predios_urbanos.shp");
        }

        [Fact]
        public async Task ExtraerDesdeRutaAsync_ConFileGeodatabaseYSubcarpetas_DetectaGdbYLinaje()
        {
            // Estructura: Entregables/GIS/BaseDatos.gdb
            var gdbPath = Path.Combine(_tempDir, "Entregables", "GIS", "BaseDatos.gdb");
            Directory.CreateDirectory(gdbPath);

            var extractor = new Iso19115MetadataExtractor();
            var result = await extractor.ExtraerDesdeRutaAsync(_tempDir);

            result.Should().NotBeNull();
            result.TotalArchivosEspaciales.Should().Be(1);
            result.FormatoDatos.Should().Contain("File Geodatabase");
            result.SubcarpetasExploradas.Should().Contain(s => s.Contains("Entregables"));
        }
    }
}
