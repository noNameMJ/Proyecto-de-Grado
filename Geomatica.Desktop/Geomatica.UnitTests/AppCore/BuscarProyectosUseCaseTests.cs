using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.UnitTests.AppCore;

public class BuscarProyectosUseCaseTests
{
    private readonly Mock<IProyectoRepository> _proyectoRepositoryMock;
    private readonly BuscarProyectosUseCase _useCase;

    public BuscarProyectosUseCaseTests()
    {
        _proyectoRepositoryMock = new Mock<IProyectoRepository>();
        _useCase = new BuscarProyectosUseCase(_proyectoRepositoryMock.Object);
    }

    [Fact]
    public async Task EjecutarAsync_DebePasarParametrosCorrectamenteAlRepositorio()
    {
        // Arrange
        var texto = "Topografía Bucaramanga";
        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 6, 30);
        var dpto = "68";
        var mpio = "68001";
        double minX = -73.2, minY = 7.0, maxX = -73.0, maxY = 7.2;

        var proyectosEsperados = new List<ProyectoGeomatico>
        {
            new()
            {
                Id = 1,
                Titulo = "Levantamiento Fotogramétrico UIS",
                Fecha = new DateTime(2026, 3, 15),
                PalabrasClave = "dron, fotogrametria",
                Responsable = "Equipo Geomática",
                RutaArchivos = @"D:\Proyectos\UIS",
                TipoRecurso = "Orto",
                Longitud = -73.1198,
                Latitud = 7.1396,
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            }
        };

        _proyectoRepositoryMock
            .Setup(r => r.BuscarAsync(texto, desde, hasta, dpto, mpio, minX, minY, maxX, maxY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(proyectosEsperados);

        // Act
        var resultado = await _useCase.EjecutarAsync(texto, desde, hasta, dpto, mpio, minX, minY, maxX, maxY);

        // Assert
        resultado.Should().NotBeNull();
        resultado.Should().HaveCount(1);
        resultado[0].Id.Should().Be(1);
        resultado[0].Titulo.Should().Be("Levantamiento Fotogramétrico UIS");

        _proyectoRepositoryMock.Verify(r => r.BuscarAsync(
            texto,
            desde,
            hasta,
            dpto,
            mpio,
            minX,
            minY,
            maxX,
            maxY,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EjecutarAsync_ConParametrosNulos_DebeConsultarSinFiltros()
    {
        // Arrange
        _proyectoRepositoryMock
            .Setup(r => r.BuscarAsync(null, null, null, null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProyectoGeomatico>());

        // Act
        var resultado = await _useCase.EjecutarAsync(null, null, null);

        // Assert
        resultado.Should().BeEmpty();
        _proyectoRepositoryMock.Verify(r => r.BuscarAsync(
            null, null, null, null, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}

