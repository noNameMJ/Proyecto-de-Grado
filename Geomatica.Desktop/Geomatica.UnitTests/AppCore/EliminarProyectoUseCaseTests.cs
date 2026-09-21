using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.AppCore;

public class EliminarProyectoUseCaseTests
{
    private readonly Mock<IProyectoRepository> _proyectoRepositoryMock;
    private readonly EliminarProyectoUseCase _useCase;

    public EliminarProyectoUseCaseTests()
    {
        _proyectoRepositoryMock = new Mock<IProyectoRepository>();
        _useCase = new EliminarProyectoUseCase(_proyectoRepositoryMock.Object);
    }

    [Fact]
    public void Constructor_ConRepositorioNulo_DebeLanzarArgumentNullException()
    {
        // Act
        var act = () => new EliminarProyectoUseCase(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("proyectoRepository");
    }

    [Fact]
    public async Task EjecutarAsync_ConIdValido_DebeInvocarEliminarEnRepositorio()
    {
        // Arrange
        const int idValido = 42;
        var cancellationToken = new CancellationToken();

        _proyectoRepositoryMock
            .Setup(r => r.EliminarAsync(idValido, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _useCase.EjecutarAsync(idValido, cancellationToken);

        // Assert
        _proyectoRepositoryMock.Verify(r => r.EliminarAsync(idValido, cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public async Task EjecutarAsync_ConIdInvalido_DebeLanzarArgumentException(int idInvalido)
    {
        // Act
        var act = () => _useCase.EjecutarAsync(idInvalido);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("idProyecto");

        _proyectoRepositoryMock.Verify(r => r.EliminarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EjecutarAsync_CuandoRepositorioFalla_DebePropagarExcepcion()
    {
        // Arrange
        const int idValido = 10;
        _proyectoRepositoryMock
            .Setup(r => r.EliminarAsync(idValido, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fallo en la base de datos PostgreSQL"));

        // Act
        var act = () => _useCase.EjecutarAsync(idValido);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Fallo en la base de datos PostgreSQL");

        _proyectoRepositoryMock.Verify(r => r.EliminarAsync(idValido, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EjecutarAsync_ConUsuarioYEquipo_DebeInvocarEliminarConMetadatosAuditoria()
    {
        // Arrange
        const int idValido = 99;
        const string usuario = @"GEOMATICAAD\marbin.arevalo";
        const string equipo = "UIS-WORKSTATION-01";
        var cancellationToken = new CancellationToken();

        _proyectoRepositoryMock
            .Setup(r => r.EliminarAsync(idValido, usuario, equipo, cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _useCase.EjecutarAsync(idValido, usuario, equipo, cancellationToken);

        // Assert
        _proyectoRepositoryMock.Verify(r => r.EliminarAsync(idValido, usuario, equipo, cancellationToken), Times.Once);
    }
}

