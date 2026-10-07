using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.AppCore;

public class ActualizarProyectoUseCaseTests
{
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly ActualizarProyectoUseCase _useCase;

    public ActualizarProyectoUseCaseTests()
    {
        _repoMock = new Mock<IProyectoRepository>();
        _useCase = new ActualizarProyectoUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task EjecutarAsync_ConParametrosValidos_LlamaAlRepositorioCorrectamente()
    {
        // Arrange
        int id = 42;
        var titulo = "Proyecto Actualizado";
        var descripcion = "Modificación de coordenadas";
        var fechaInicio = new DateTime(2026, 2, 1);
        var palabraClave = "catastro";
        var ruta = @"D:\Proyectos\Catastro";
        var geom = "POINT(-73.10 7.10)";
        var mpio = "68001";

        // Act
        await _useCase.EjecutarAsync(id, titulo, descripcion, fechaInicio, palabraClave, ruta, geom, mpio);

        // Assert
        _repoMock.Verify(r => r.ActualizarAsync(
            id,
            titulo,
            descripcion,
            fechaInicio,
            palabraClave,
            ruta,
            geom,
            mpio,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTime?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99)]
    public async Task EjecutarAsync_ConIdInvalido_LanzaArgumentException(int idInvalido)
    {
        // Act
        Func<Task> act = async () => await _useCase.EjecutarAsync(idInvalido, "Titulo", null, null, null, null, null, null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*identificador*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EjecutarAsync_SinTitulo_LanzaArgumentException(string? tituloInvalido)
    {
        // Act
        Func<Task> act = async () => await _useCase.EjecutarAsync(10, tituloInvalido!, null, null, null, null, null, null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*título*");
    }
}

