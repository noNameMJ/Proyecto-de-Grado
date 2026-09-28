using FluentAssertions;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Moq;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class FichaProyectoZipExportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<INotificationService> _mockNotifications;

    public FichaProyectoZipExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaFichaZipTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _mockNotifications = new Mock<INotificationService>();
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
            // Limpieza ignorada
        }
    }

    private static ProyectoDetalleDto CrearDto(string? rutaArchivos = null, string titulo = "Proyecto Levantamiento")
    {
        return new ProyectoDetalleDto(
            Id: 42,
            Titulo: titulo,
            Descripcion: "Descripción del proyecto para exportación",
            FechaInicio: new DateTime(2024, 1, 15),
            PalabraClave: "topografia, satelital",
            RutaArchivos: rutaArchivos,
            Lon: -73.12,
            Lat: 7.14,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );
    }

    [Fact]
    public void PuedeDescargarZip_ConPermisosDeLecturaYRutaValida_RetornaTrue()
    {
        // Arrange
        string projectPath = Path.Combine(_tempDir, "ProyectoActivo");
        Directory.CreateDirectory(projectPath);

        var dto = CrearDto(rutaArchivos: projectPath);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { }, notifications: _mockNotifications.Object);

        // Assert
        vm.PuedeDescargarZip.Should().BeTrue();
        vm.DescargarZipCommand.CanExecute(null).Should().BeTrue();
        vm.TooltipDescargarZip.Should().Contain("Descargar y empaquetar");
    }

    [Fact]
    public void PuedeDescargarZip_SinRuta_RetornaFalse()
    {
        // Arrange
        var dto = CrearDto(rutaArchivos: null);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { }, notifications: _mockNotifications.Object);

        // Assert
        vm.PuedeDescargarZip.Should().BeFalse();
        vm.DescargarZipCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task DescargarZipCommand_ConSaveFileDialogPersonalizado_EmpaquetaYNotificaExito()
    {
        // Arrange
        string projectPath = Path.Combine(_tempDir, "ProyectoConArchivos");
        Directory.CreateDirectory(Path.Combine(projectPath, "Entregables"));
        await File.WriteAllTextAsync(Path.Combine(projectPath, "Entregables", "plano.dwg"), "dwg-content");
        await File.WriteAllTextAsync(Path.Combine(projectPath, "metadatos.xml"), "<metadata/>");

        string targetZip = Path.Combine(_tempDir, "Salida_Empaquetada.zip");

        var dto = CrearDto(rutaArchivos: projectPath, titulo: "Levantamiento Topográfico UIS");
        var vm = new FichaProyectoViewModel(dto, () => { }, notifications: _mockNotifications.Object);

        vm.SaveFileDialogCustomHandler = (nombreSugerido, filtro) =>
        {
            nombreSugerido.Should().Contain("#PROY-0042");
            return targetZip;
        };

        // Act
        await vm.DescargarZipCommand.ExecuteAsync(null);

        // Assert
        File.Exists(targetZip).Should().BeTrue();
        using (var archive = ZipFile.OpenRead(targetZip))
        {
            archive.Entries.Should().Contain(e => e.FullName == "Entregables/plano.dwg");
            archive.Entries.Should().Contain(e => e.FullName == "metadatos.xml");
        }

        vm.IsDescargandoZip.Should().BeFalse();
        vm.ProgresoDescargaZip.Should().Be(0);

        _mockNotifications.Verify(n => n.ShowSuccess(
            It.Is<string>(s => s.Contains("empaquetado y descargado exitosamente")),
            It.Is<string>(t => t.Contains("Empaquetado ZIP")),
            It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DescargarZipCommand_CuandoUsuarioCancelaDialogo_NoGeneraZip()
    {
        // Arrange
        string projectPath = Path.Combine(_tempDir, "ProyectoCancelDialog");
        Directory.CreateDirectory(projectPath);
        await File.WriteAllTextAsync(Path.Combine(projectPath, "archivo.txt"), "datos");

        var dto = CrearDto(rutaArchivos: projectPath);
        var vm = new FichaProyectoViewModel(dto, () => { }, notifications: _mockNotifications.Object);

        // Simular que el usuario hace clic en "Cancelar" en el diálogo de guardado
        vm.SaveFileDialogCustomHandler = (s, f) => null;

        // Act
        await vm.DescargarZipCommand.ExecuteAsync(null);

        // Assert
        _mockNotifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _mockNotifications.Verify(n => n.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ExportarZipDirectoAsync_RutaProyectoInexistente_RetornaFalse()
    {
        // Arrange
        string rutaInexistente = Path.Combine(_tempDir, "RutaQueNoExiste");
        var dto = CrearDto(rutaArchivos: rutaInexistente);
        var vm = new FichaProyectoViewModel(dto, () => { }, notifications: _mockNotifications.Object);

        string targetZip = Path.Combine(_tempDir, "invalido.zip");

        // Act
        bool exito = await vm.ExportarZipDirectoAsync(targetZip);

        // Assert
        exito.Should().BeFalse();
        File.Exists(targetZip).Should().BeFalse();
    }
}
