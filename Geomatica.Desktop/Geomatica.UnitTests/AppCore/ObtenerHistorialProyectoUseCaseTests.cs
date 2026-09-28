using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.AppCore;

public class ObtenerHistorialProyectoUseCaseTests
{
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly ObtenerHistorialProyectoUseCase _useCase;

    public ObtenerHistorialProyectoUseCaseTests()
    {
        _repoMock = new Mock<IProyectoRepository>();
        _useCase = new ObtenerHistorialProyectoUseCase(_repoMock.Object);
    }

    [Fact]
    public async Task EjecutarAsync_ConIdValido_RetornaHistorialDelRepositorio()
    {
        // Arrange
        int id = 5;
        var esperado = new List<AuditoriaProyectoDto>
        {
            new(1, 5, "Proyecto Test", "CREACION", "topografo", "PC1", DateTime.UtcNow.AddDays(-2), "Proyecto inicial"),
            new(2, 5, "Proyecto Test", "EDICION", "coordinador", "PC2", DateTime.UtcNow, "Modificación fecha")
        };
        _repoMock.Setup(r => r.ObtenerHistorialProyectoAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(esperado);

        // Act
        var resultado = await _useCase.EjecutarAsync(id);

        // Assert
        resultado.Should().HaveCount(2);
        resultado.Should().BeEquivalentTo(esperado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EjecutarAsync_ConIdInvalido_RetornaListaVaciaSinConsultarRepo(int idInvalido)
    {
        // Act
        var resultado = await _useCase.EjecutarAsync(idInvalido);

        // Assert
        resultado.Should().BeEmpty();
        _repoMock.Verify(r => r.ObtenerHistorialProyectoAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
