using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.AppCore;

public class CrearProyectoUseCaseTests
{
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly CrearProyectoUseCase _useCase;

    public CrearProyectoUseCaseTests()
    {
        _repoMock = new Mock<IProyectoRepository>();
        _useCase = new CrearProyectoUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task EjecutarAsync_ConTituloValido_LlamaAlRepositorioCorrectamente()
    {
        // Arrange
        var titulo = "Proyecto Vial 2026";
        var descripcion = "Levantamiento topográfico";
        var fechaInicio = new DateTime(2026, 1, 15);
        var palabraClave = "topografía, vías";
        var ruta = @"D:\Proyectos\Vial";
        var geom = "POINT(-73.12 7.13)";
        var mpio = "68001";
        var usuario = "topografo1";
        var equipo = "WORKSTATION-01";

        // Act
        await _useCase.EjecutarAsync(titulo, descripcion, fechaInicio, palabraClave, ruta, geom, mpio, usuario, equipo);

        // Assert
        _repoMock.Verify(r => r.InsertarAsync(
            titulo,
            descripcion,
            fechaInicio,
            palabraClave,
            ruta,
            geom,
            mpio,
            usuario,
            equipo,
            null,
            null,
            null), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EjecutarAsync_SinTitulo_LanzaArgumentException(string? tituloInvalido)
    {
        // Act
        Func<Task> act = async () => await _useCase.EjecutarAsync(tituloInvalido!, null, null, null, null, null, null);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*título*");
    }
}

