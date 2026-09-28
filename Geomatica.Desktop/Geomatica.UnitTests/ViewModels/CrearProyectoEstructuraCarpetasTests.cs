using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class CrearProyectoEstructuraCarpetasTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IProyectoRepository> _mockProyectoRepo;
    private readonly Mock<IMunicipioRepository> _mockMunicipioRepo;
    private readonly Mock<INotificationService> _mockNotifications;
    private readonly ProyectoArchivosService _archivosService;
    private readonly CrearProyectoUseCase _useCase;

    public CrearProyectoEstructuraCarpetasTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaCrearEstructuraTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _mockProyectoRepo = new Mock<IProyectoRepository>();
        _mockMunicipioRepo = new Mock<IMunicipioRepository>();
        _mockNotifications = new Mock<INotificationService>();
        _archivosService = new ProyectoArchivosService();
        _useCase = new CrearProyectoUseCase(_mockProyectoRepo.Object);
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
            // Ignorado
        }
    }

    [Fact]
    public async Task GuardarAsync_SiExistenCarpetasEnRuta_NoCreaEstructuraNiPregunta()
    {
        // Arrange
        string dirConCarpetas = Path.Combine(_tempDir, "ProyectoExistenteConCarpetas");
        Directory.CreateDirectory(Path.Combine(dirConCarpetas, "CapasExistentes"));

        var vm = new CrearProyectoViewModel(
            _useCase,
            _mockMunicipioRepo.Object,
            _archivosService,
            () => { },
            null,
            _mockNotifications.Object);

        vm.Titulo = "Proyecto con carpetas existentes";
        vm.SelectedMunicipio = new CrearProyectoViewModel.MunicipioItem("68001", "Bucaramanga");
        vm.Ruta = dirConCarpetas;

        bool handlerInvocado = false;
        vm.ConfirmarCreacionEstructuraHandler = ruta =>
        {
            handlerInvocado = true;
            return true;
        };

        // Act
        await vm.GuardarCommand.ExecuteAsync(null);

        // Assert
        handlerInvocado.Should().BeFalse("no debe preguntarse al usuario si ya existen carpetas");
        Directory.Exists(Path.Combine(dirConCarpetas, "Datos_Espaciales")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirConCarpetas, "Documentos")).Should().BeFalse();
        _mockProyectoRepo.Verify(r => r.InsertarAsync(
            "Proyecto con carpetas existentes",
            It.IsAny<string?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string?>(),
            dirConCarpetas,
            It.IsAny<string?>(),
            "68001",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()
        ), Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_SiNoExistenCarpetasYUsuarioAcepta_PreguntaYCreaEstructuraEstandar()
    {
        // Arrange: carpeta sin subcarpetas
        string dirVacio = Path.Combine(_tempDir, "ProyectoNuevoVacio");
        Directory.CreateDirectory(dirVacio);

        var vm = new CrearProyectoViewModel(
            _useCase,
            _mockMunicipioRepo.Object,
            _archivosService,
            () => { },
            null,
            _mockNotifications.Object);

        vm.Titulo = "Proyecto Nuevo";
        vm.SelectedMunicipio = new CrearProyectoViewModel.MunicipioItem("68001", "Bucaramanga");
        vm.Ruta = dirVacio;

        bool handlerInvocado = false;
        vm.ConfirmarCreacionEstructuraHandler = ruta =>
        {
            handlerInvocado = true;
            return true; // Usuario acepta crear estructura
        };

        // Act
        await vm.GuardarCommand.ExecuteAsync(null);

        // Assert
        handlerInvocado.Should().BeTrue("debe consultar al usuario al no existir subcarpetas");
        Directory.Exists(Path.Combine(dirVacio, "Datos_Espaciales")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirVacio, "Documentos")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirVacio, "Entregables")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirVacio, "Otros")).Should().BeTrue();
    }

    [Fact]
    public async Task GuardarAsync_SiNoExistenCarpetasYUsuarioRechaza_PreguntaYNoCreaSubcarpetas()
    {
        // Arrange: carpeta sin subcarpetas
        string dirVacio = Path.Combine(_tempDir, "ProyectoNuevoSinSubcarpetas");
        Directory.CreateDirectory(dirVacio);

        var vm = new CrearProyectoViewModel(
            _useCase,
            _mockMunicipioRepo.Object,
            _archivosService,
            () => { },
            null,
            _mockNotifications.Object);

        vm.Titulo = "Proyecto Sin Carpetas Estandar";
        vm.SelectedMunicipio = new CrearProyectoViewModel.MunicipioItem("68001", "Bucaramanga");
        vm.Ruta = dirVacio;

        bool handlerInvocado = false;
        vm.ConfirmarCreacionEstructuraHandler = ruta =>
        {
            handlerInvocado = true;
            return false; // Usuario rechaza crear estructura
        };

        // Act
        await vm.GuardarCommand.ExecuteAsync(null);

        // Assert
        handlerInvocado.Should().BeTrue("debe consultar al usuario al no existir subcarpetas");
        Directory.Exists(Path.Combine(dirVacio, "Datos_Espaciales")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirVacio, "Documentos")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirVacio, "Entregables")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirVacio, "Otros")).Should().BeFalse();
    }
}
