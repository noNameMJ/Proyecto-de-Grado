using System;
using System.IO;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Xunit;

namespace Geomatica.UnitTests.Services
{
    public class ShapefileValidatorTests : IDisposable
    {
        private readonly string _tempDir;

        public ShapefileValidatorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ShapefileValidatorTests_" + Guid.NewGuid().ToString("N"));
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
        public void Validar_ArchivoNoExiste_RetornaNoPuedeCargar()
        {
            // Arrange
            var shpPath = Path.Combine(_tempDir, "inexistente.shp");

            // Act
            var result = ShapefileValidator.Validar(shpPath);

            // Assert
            result.PuedeCargar.Should().BeFalse();
            result.MensajeError.Should().Contain("no existe");
        }

        [Fact]
        public void Validar_FaltaShx_RetornaIncompleto()
        {
            // Arrange
            var shpPath = Path.Combine(_tempDir, "capa.shp");
            var dbfPath = Path.Combine(_tempDir, "capa.dbf");
            File.WriteAllBytes(shpPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(dbfPath, new byte[] { 0, 1, 2 });
            // .shx falta

            // Act
            var result = ShapefileValidator.Validar(shpPath);

            // Assert
            result.PuedeCargar.Should().BeFalse();
            result.MensajeError.Should().Contain(".shx");
        }

        [Fact]
        public void Validar_FaltaDbf_RetornaIncompleto()
        {
            // Arrange
            var shpPath = Path.Combine(_tempDir, "capa.shp");
            var shxPath = Path.Combine(_tempDir, "capa.shx");
            File.WriteAllBytes(shpPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(shxPath, new byte[] { 0, 1, 2 });
            // .dbf falta

            // Act
            var result = ShapefileValidator.Validar(shpPath);

            // Assert
            result.PuedeCargar.Should().BeFalse();
            result.MensajeError.Should().Contain(".dbf");
        }

        [Fact]
        public void Validar_SinPrj_PermiteCargarConAdvertencia()
        {
            // Arrange
            var shpPath = Path.Combine(_tempDir, "capa.shp");
            var shxPath = Path.Combine(_tempDir, "capa.shx");
            var dbfPath = Path.Combine(_tempDir, "capa.dbf");
            File.WriteAllBytes(shpPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(shxPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(dbfPath, new byte[] { 0, 1, 2 });
            // .prj no existe

            // Act
            var result = ShapefileValidator.Validar(shpPath);

            // Assert
            result.PuedeCargar.Should().BeTrue();
            result.TienePrjValido.Should().BeFalse();
            result.MensajeAdvertencia.Should().Contain(".prj");
        }

        [Fact]
        public void Validar_ConPrjValidoWgs84_RetornaExitosoConCrs()
        {
            // Arrange
            var shpPath = Path.Combine(_tempDir, "predios.shp");
            var shxPath = Path.Combine(_tempDir, "predios.shx");
            var dbfPath = Path.Combine(_tempDir, "predios.dbf");
            var prjPath = Path.Combine(_tempDir, "predios.prj");

            File.WriteAllBytes(shpPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(shxPath, new byte[] { 0, 1, 2 });
            File.WriteAllBytes(dbfPath, new byte[] { 0, 1, 2 });

            var wktWgs84 = @"GEOGCS[""GCS_WGS_1984"",DATUM[""D_WGS_1984"",SPHEROID[""WGS_1984"",6378137.0,298.257223563]],PRIMEM[""Greenwich"",0.0],UNIT[""Degree"",0.0174532925199433],AUTHORITY[""EPSG"",4326]]";
            File.WriteAllText(prjPath, wktWgs84);

            // Act
            var result = ShapefileValidator.Validar(shpPath);

            // Assert
            result.PuedeCargar.Should().BeTrue();
            result.TienePrjValido.Should().BeTrue();
            result.SpatialReference.Should().NotBeNull();
            result.CrsNombre.Should().Contain("4326");
        }
    }
}

