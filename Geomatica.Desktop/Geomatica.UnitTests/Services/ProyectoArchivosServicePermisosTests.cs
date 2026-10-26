using System;
using System.IO;
using FluentAssertions;
using Geomatica.Desktop.Services;
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
    public void TryObtenerPermisosCacheados_Y_InvalidarIndice_GestionanCachePermisosCorrectamente()
    {
        // Arrange: inicialmente no hay permisos cacheados para la carpeta
        _service.TryObtenerPermisosCacheados(_tempDir, out _).Should().BeFalse();

        // Act 1: Evaluar permisos almacena el resultado en caché con TTL
        var evaluado = _service.EvaluarPermisosCarpeta(_tempDir);
        _service.TryObtenerPermisosCacheados(_tempDir, out var cacheado).Should().BeTrue();
        cacheado.Should().Be(evaluado);

        // Act 2: InvalidarIndice limpia tanto el índice como la caché de permisos
        _service.InvalidarIndice(_tempDir);

        // Assert
        _service.TryObtenerPermisosCacheados(_tempDir, out _).Should().BeFalse();
    }
}

