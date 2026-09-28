using FluentAssertions;
using Geomatica.Desktop.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class ProyectoArchivosServiceZipTests : IDisposable
{
    private readonly ProyectoArchivosService _service;
    private readonly string _tempDir;

    public ProyectoArchivosServiceZipTests()
    {
        _service = new ProyectoArchivosService();
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaZipTests_" + Guid.NewGuid().ToString("N"));
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
        catch
        {
            // Ignorar errores de limpieza
        }
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_ComprimeCarpetaCompleta_CreaArchivoZipValido()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempDir, "ProyectoTest");
        Directory.CreateDirectory(Path.Combine(sourceDir, "Datos_Espaciales"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "Documentos"));

        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Datos_Espaciales", "parcelas.geojson"), "{\"type\": \"FeatureCollection\"}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "Documentos", "informe.txt"), "Informe técnico del proyecto");

        string zipDestino = Path.Combine(_tempDir, "Exportado", "ProyectoTest.zip");
        double ultimoProgreso = 0;
        var progress = new Progress<double>(p => ultimoProgreso = p);

        // Act
        bool resultado = await _service.EmpaquetarCarpetaZipAsync(sourceDir, zipDestino, progress);

        // Assert
        resultado.Should().BeTrue();
        File.Exists(zipDestino).Should().BeTrue();
        new FileInfo(zipDestino).Length.Should().BeGreaterThan(0);
        ultimoProgreso.Should().Be(100.0);

        using var archive = ZipFile.OpenRead(zipDestino);
        archive.Entries.Should().Contain(e => e.FullName == "Datos_Espaciales/parcelas.geojson");
        archive.Entries.Should().Contain(e => e.FullName == "Documentos/informe.txt");
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_RutaInexistente_LanzaDirectoryNotFoundException()
    {
        // Arrange
        string rutaInexistente = Path.Combine(_tempDir, "CarpetaFantasma_" + Guid.NewGuid());
        string zipDestino = Path.Combine(_tempDir, "salida.zip");

        // Act & Assert
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            _service.EmpaquetarCarpetaZipAsync(rutaInexistente, zipDestino));
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_RutaDestinoVacia_LanzaArgumentException()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempDir, "CarpetaValida");
        Directory.CreateDirectory(sourceDir);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.EmpaquetarCarpetaZipAsync(sourceDir, ""));
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_IgnoraArchivosProbeYLock()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempDir, "ProyectoLocks");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "capa.shp"), "shapefile-bytes");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, ".probe_123.tmp"), "temp-probe");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "capa.lock"), "lock-descriptor");

        string zipDestino = Path.Combine(_tempDir, "salida_locks.zip");

        // Act
        bool resultado = await _service.EmpaquetarCarpetaZipAsync(sourceDir, zipDestino);

        // Assert
        resultado.Should().BeTrue();
        using var archive = ZipFile.OpenRead(zipDestino);
        archive.Entries.Should().Contain(e => e.FullName == "capa.shp");
        archive.Entries.Should().NotContain(e => e.FullName.Contains(".probe_"));
        archive.Entries.Should().NotContain(e => e.FullName.EndsWith(".lock"));
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_ArchivoIndividual_CreaZipValido()
    {
        // Arrange
        string archivoOrigen = Path.Combine(_tempDir, "ortofoto.tif");
        await File.WriteAllBytesAsync(archivoOrigen, new byte[] { 0x49, 0x49, 0x2A, 0x00 });

        string zipDestino = Path.Combine(_tempDir, "ortofoto.zip");

        // Act
        bool resultado = await _service.EmpaquetarCarpetaZipAsync(archivoOrigen, zipDestino);

        // Assert
        resultado.Should().BeTrue();
        File.Exists(zipDestino).Should().BeTrue();
        using var archive = ZipFile.OpenRead(zipDestino);
        archive.Entries.Should().Contain(e => e.FullName == "ortofoto.tif");
    }

    [Fact]
    public async Task EmpaquetarCarpetaZipAsync_CancelacionEliminaArchivoTemporal()
    {
        // Arrange
        string sourceDir = Path.Combine(_tempDir, "ProyectoCancel");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "dato.txt"), "prueba de cancelacion");

        string zipDestino = Path.Combine(_tempDir, "salida_cancelada.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Token ya cancelado

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.EmpaquetarCarpetaZipAsync(sourceDir, zipDestino, ct: cts.Token));

        File.Exists(zipDestino).Should().BeFalse();
    }
}

