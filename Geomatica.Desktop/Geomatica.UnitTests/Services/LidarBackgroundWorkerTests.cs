using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Esri.ArcGISRuntime.Geometry;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Xunit;

namespace Geomatica.UnitTests.Services
{
    public class LidarBackgroundWorkerTests : IDisposable
    {
        private readonly string _tempDir;

        public LidarBackgroundWorkerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LidarWorkerTests_" + Guid.NewGuid().ToString("N"));
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
        public async Task ProcesarNubeLidarAsync_ArchivoInexistente_LanzaFileNotFoundException()
        {
            // Arrange
            var rutaInexistente = Path.Combine(_tempDir, "no_existe.las");

            // Act
            Func<Task> act = async () => await LidarBackgroundWorker.ProcesarNubeLidarAsync(rutaInexistente);

            // Assert
            await act.Should().ThrowAsync<FileNotFoundException>();
        }

        [Fact]
        public async Task ProcesarNubeLidarAsync_LasSintetico_ProcesaEnSegundoPlanoYGeneraResultadosWgs84()
        {
            // Arrange
            var lasPath = Path.Combine(_tempDir, "muestra_bogota.las");
            CrearLasSintetico(lasPath, 15, minX: -74.1, maxX: -74.0, minY: 4.6, maxY: 4.7, minZ: 2550, maxZ: 2650);

            var reportes = new List<(int pct, string det)>();
            var progress = new Progress<(int pct, string det)>(p => reportes.Add(p));

            // Act
            var result = await LidarBackgroundWorker.ProcesarNubeLidarAsync(lasPath, null, maxPointsToSample: 100, progress: progress);

            // Assert
            result.Should().NotBeNull();
            result.TotalPoints.Should().Be(15);
            result.SampledPointsCount.Should().Be(15);
            result.PuntosMuestreadosWgs84.Should().HaveCount(15);
            result.EnvelopeWgs84.Should().NotBeNull();
            result.FootprintWgs84.Should().NotBeNull();
            result.CenterWgs84.Should().NotBeNull();
            result.TamanoPuntoRecomendado.Should().BeGreaterThan(0);
            result.InfoDetalle3D.Should().Contain("15 pts");
            reportes.Should().NotBeEmpty("el trabajador en segundo plano debe reportar progreso");
        }

        [Fact]
        public void CalcularRadioMetros_EnvelopeNulo_RetornaDefault150()
        {
            // Act
            double radio = MapaViewModel.CalcularRadioMetros(null);

            // Assert
            radio.Should().Be(150.0);
        }

        [Fact]
        public void CalcularRadioMetros_EnvelopeWgs84_CalculaMetrosCorrectamente()
        {
            // Arrange: Recuadro de 0.01 grados (~1113 metros en latitud)
            var env = new Envelope(-74.01, 4.60, -74.00, 4.61, SpatialReferences.Wgs84);

            // Act
            double radio = MapaViewModel.CalcularRadioMetros(env);

            // Assert
            radio.Should().BeGreaterThan(500.0);
            radio.Should().BeLessThan(1200.0);
        }

        [Fact]
        public void CalcularRadioMetros_EnvelopeMetrico_CalculaMetrosCorrectamente()
        {
            // Arrange: 600m de ancho x 800m de alto -> hipotenusa 1000m -> radio 500m
            var srMetrico = SpatialReference.Create(3116);
            var env = new Envelope(1000.0, 2000.0, 1600.0, 2800.0, srMetrico);

            // Act
            double radio = MapaViewModel.CalcularRadioMetros(env);

            // Assert
            radio.Should().BeApproximately(500.0, 1.0);
        }

        [Fact]
        public async Task ProcesarNubeLidarAsync_LasConZRelativaYAnclajeElevado_AplicaOffsetParaEvitarEstarBajoTierra()
        {
            // Arrange: Nube proyectada UTM 18N con coordenadas métricas y Z relativo de vuelo (0 a 30 metros)
            var lasPath = Path.Combine(_tempDir, "vuelo_uav_relativo.las");
            CrearLasSintetico(lasPath, 10, minX: 550_000, maxX: 550_100, minY: 1_000_000, maxY: 1_000_100, minZ: 5, maxZ: 35);

            // Anclaje en Bucaramanga: altitud 960m
            var anclaje = (-73.12, 7.14, 960.0);

            // Act
            var result = await LidarBackgroundWorker.ProcesarNubeLidarAsync(lasPath, anclaje, maxPointsToSample: 100);

            // Assert: Z del resultado debe estar posado sobre el terreno (> 950m), no en Z=20m bajo tierra
            result.CentroZWgs84.Should().BeGreaterThan(950.0);
            result.PuntosMuestreadosWgs84.Should().NotBeEmpty();
            result.PuntosMuestreadosWgs84.First().PtWgs84.Z.Should().BeGreaterThan(950.0);
        }

        private static void CrearLasSintetico(string path, int numPoints, double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // 1. Firma 'LASF' (4 bytes)
            bw.Write(Encoding.ASCII.GetBytes("LASF"));

            // Rellenar hasta byte 24
            bw.Write(new byte[20]);

            // Version 1.2 (bytes 24 y 25)
            bw.Write((byte)1);
            bw.Write((byte)2);

            // Rellenar hasta byte 94
            bw.Write(new byte[68]);

            // Offset 94: Header Size (ushort) = 227
            ushort headerSize = 227;
            bw.Write(headerSize);

            // Offset 96: Offset to point data (uint) = 227
            uint offsetToPoints = 227;
            bw.Write(offsetToPoints);

            // Offset 100: Number of VLRs (uint) = 0
            bw.Write((uint)0);

            // Offset 104: Point Data Format = 0 (byte)
            bw.Write((byte)0);

            // Offset 105: Point Record Length = 20 (ushort)
            ushort pointRecordLen = 20;
            bw.Write(pointRecordLen);

            // Offset 107: Legacy Number of point records (uint)
            bw.Write((uint)numPoints);

            // Offset 111: Number of points by return (5 uints = 20 bytes)
            bw.Write(new byte[20]);

            // Offset 131: Escalas X, Y, Z y Desfases X, Y, Z (6 doubles = 48 bytes)
            bw.Write(0.0001); // scaleX
            bw.Write(0.0001); // scaleY
            bw.Write(0.01);   // scaleZ
            bw.Write(0.0);    // offsetX
            bw.Write(0.0);    // offsetY
            bw.Write(0.0);    // offsetZ

            // Offset 179: Max/Min Extents (6 doubles = 48 bytes: maxX, minX, maxY, minY, maxZ, minZ)
            bw.Write(maxX);
            bw.Write(minX);
            bw.Write(maxY);
            bw.Write(minY);
            bw.Write(maxZ);
            bw.Write(minZ);

            // Rellenar hasta llegar al byte 227
            while (fs.Position < offsetToPoints)
            {
                bw.Write((byte)0);
            }

            // Escribir registros de puntos (formato 0: 20 bytes cada uno)
            double stepX = (maxX - minX) / Math.Max(1, numPoints);
            double stepY = (maxY - minY) / Math.Max(1, numPoints);
            double stepZ = (maxZ - minZ) / Math.Max(1, numPoints);

            for (int i = 0; i < numPoints; i++)
            {
                double px = minX + i * stepX;
                double py = minY + i * stepY;
                double pz = minZ + i * stepZ;

                int rawX = (int)(px / 0.0001);
                int rawY = (int)(py / 0.0001);
                int rawZ = (int)(pz / 0.01);

                bw.Write(rawX);
                bw.Write(rawY);
                bw.Write(rawZ);
                bw.Write((ushort)100); // Intensity
                bw.Write((byte)1);     // Return bitfields
                bw.Write((byte)2);     // Classification (Ground)
                bw.Write((sbyte)0);    // Scan Angle
                bw.Write((byte)0);     // User Data
                bw.Write((ushort)1);   // Point Source ID
            }
        }
    }
}

