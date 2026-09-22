using System;
using System.IO;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class ProyectoArchivosServicePermisosTests : IDisposable
{
    private readonly ProyectoArchivosService _service;
    private readonly string _tempDir;

    public ProyectoArchivosServicePermisosTests()
    {
        _service = new ProyectoArchivosService();
        _tempDir = Path.Combine(Path.GetTempPath(), $"geomatica_test_{Guid.NewGuid():N}");
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
            // Best effort cleanup
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EvaluarPermisosCarpeta_RutaNulaOVacia_RetornaEstadoSinRuta(string? ruta)
    {
        // Act
        var resultado = _service.EvaluarPermisosCarpeta(ruta);

        // Assert
        resultado.Should().NotBeNull();
        resultado.Estado.Should().Be(EstadoPermisoCarpeta.SinRuta);
        resultado.PuedeLeer.Should().BeFalse();
        resultado.PuedeEscribir.Should().BeTrue();
        resultado.BadgeTexto.Should().Be("Sin carpeta");
    }

    [Fact]
    public void EvaluarPermisosCarpeta_RutaInexistente_RetornaRutaNoEncontrada()
    {
        // Arrange
        var rutaInexistente = Path.Combine(_tempDir, "subcarpeta_que_no_existe_123");

        // Act
        var resultado = _service.EvaluarPermisosCarpeta(rutaInexistente);

        // Assert
        resultado.Should().NotBeNull();
        resultado.Estado.Should().Be(EstadoPermisoCarpeta.RutaNoEncontrada);
        resultado.PuedeLeer.Should().BeFalse();
        resultado.PuedeEscribir.Should().BeFalse();
        resultado.BadgeTexto.Should().Be("Ruta inaccesible");
    }

    [Fact]
    public void EvaluarPermisosCarpeta_RutaValidaConPermisosCompletos_RetornaLecturaEscritura()
    {
        // Act
        var resultado = _service.EvaluarPermisosCarpeta(_tempDir);

        // Assert
        resultado.Should().NotBeNull();
        resultado.Estado.Should().Be(EstadoPermisoCarpeta.LecturaEscritura);
        resultado.PuedeLeer.Should().BeTrue();
        resultado.PuedeEscribir.Should().BeTrue();
        resultado.BadgeTexto.Should().Be("📂 Vinculada (Escritura)");
        resultado.Mensaje.Should().Contain("lectura y escritura");
    }

    [Fact]
    public void FichaProyectoViewModel_ConRutaLecturaEscritura_PermiteEditarYEliminar()
    {
        // Arrange
        var dto = new ProyectoDetalleDto(
            Id: 1,
            Titulo: "Proyecto Test Permisos",
            Descripcion: "Descripción de prueba",
            FechaInicio: DateTime.Today,
            PalabraClave: "test, permisos",
            RutaArchivos: _tempDir,
            Lon: -73.12,
            Lat: 7.14,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );

        // Act
        var vm = new FichaProyectoViewModel(
            dto,
            volverAction: () => { },
            eliminarProyectoUseCase: null,
            onProyectoEliminado: null,
            archivosService: _service,
            notifications: null);

        // Assert
        vm.PuedeEditar.Should().BeTrue();
        vm.PuedeEliminar.Should().BeTrue();
        vm.PuedeLeerArchivos.Should().BeTrue();
        vm.EsAccesoRestringido.Should().BeFalse();
        vm.EsSoloLectura.Should().BeFalse();
        vm.TooltipEdicion.Should().Be("Editar detalles del proyecto");
    }

    [Fact]
    public void FichaProyectoViewModel_ConRutaSinAsignar_PermiteEditarMetadatosPeroNoAbrirCarpeta()
    {
        // Arrange
        var dto = new ProyectoDetalleDto(
            Id: 2,
            Titulo: "Proyecto Sin Carpeta",
            Descripcion: null,
            FechaInicio: null,
            PalabraClave: null,
            RutaArchivos: null,
            Lon: -73.12,
            Lat: 7.14,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );

        // Act
        var vm = new FichaProyectoViewModel(
            dto,
            volverAction: () => { },
            eliminarProyectoUseCase: null,
            onProyectoEliminado: null,
            archivosService: _service,
            notifications: null);

        // Assert
        vm.PuedeEditar.Should().BeTrue();
        vm.PuedeEliminar.Should().BeTrue();
        vm.PuedeLeerArchivos.Should().BeFalse();
        vm.EsAccesoRestringido.Should().BeFalse();
    }
}

