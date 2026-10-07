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
            It.IsAny<DateTime?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
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

    [Fact]
    public async Task EjecutarAsync_ConMetadatosIso19115_PasaMetadatosAlRepositorio()
    {
        // Arrange
        var titulo = "Proyecto Fotogramétrico 2026";
        var srid = "EPSG:9377 (MAGNA-SIRGAS Origen Nacional)";
        var formato = "Raster (GeoTIFF)";
        var linaje = "Vuelo dron DJI Mavic 3 Enterprise, procesamiento en Pix4D";

        // Act
        await _useCase.EjecutarAsync(
            titulo: titulo,
            descripcion: "Resumen técnico",
            fechaInicio: DateTime.Today,
            palabraClave: "ortomosaico",
            ruta: @"D:\Proyectos\Foto2026",
            geom: null,
            municipioCodigo: "68001",
            usuario: "operador",
            equipo: "PC-01",
            fechaFin: null,
            entidades: "UIS",
            representante: "Ing. Topógrafo",
            sistemaReferencia: srid,
            formatoDatos: formato,
            linaje: linaje);

        // Assert
        _repoMock.Verify(r => r.InsertarAsync(
            titulo,
            "Resumen técnico",
            DateTime.Today,
            "ortomosaico",
            @"D:\Proyectos\Foto2026",
            null,
            "68001",
            "operador",
            "PC-01",
            null,
            "UIS",
            "Ing. Topógrafo",
            srid,
            formato,
            linaje), Times.Once);
    }
}

