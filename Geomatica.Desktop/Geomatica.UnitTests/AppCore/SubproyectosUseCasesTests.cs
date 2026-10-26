using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.UnitTests.AppCore;

public class SubproyectosUseCasesTests
{
    private readonly Mock<IProyectoRepository> _mockRepo;

    public SubproyectosUseCasesTests()
    {
        _mockRepo = new Mock<IProyectoRepository>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task ObtenerSubproyectos_ConIdInvalido_RetornaListaVaciaSinConsultarRepo(int idProyectoInvalido)
    {
        var useCase = new ObtenerSubproyectosUseCase(_mockRepo.Object);

        var resultado = await useCase.EjecutarAsync(idProyectoInvalido);

        resultado.Should().BeEmpty();
        _mockRepo.Verify(r => r.ObtenerSubproyectosAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerSubproyectos_ConIdValido_RetornaListaDesdeRepositorio()
    {
        var mockItems = new List<SubproyectoDto>
        {
            new(1, 10, "Embarcadero Puerto Olaya", "68081", "Barrancabermeja", "Puerto_Olaya", -73.85, 7.06, "Muelle principal"),
            new(2, 10, "Embarcadero Yondó 1", "05893", "Yondó", "Yondo_1", -73.91, 7.08, "Sitio auxiliar")
        };
        _mockRepo.Setup(r => r.ObtenerSubproyectosAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockItems);

        var useCase = new ObtenerSubproyectosUseCase(_mockRepo.Object);

        var resultado = await useCase.EjecutarAsync(10);

        resultado.Should().HaveCount(2);
        resultado[0].Nombre.Should().Be("Embarcadero Puerto Olaya");
        resultado[0].Subcarpeta.Should().Be("Puerto_Olaya");
        resultado[0].UbicacionMunicipioTexto.Should().Contain("Barrancabermeja");
        resultado[0].CoordenadasTexto.Should().Contain("7.06000° N");
        resultado[1].Nombre.Should().Be("Embarcadero Yondó 1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GuardarSubproyecto_ConIdInvalido_LanzaArgumentException(int idInvalido)
    {
        var useCase = new GuardarSubproyectoUseCase(_mockRepo.Object);

        var act = () => useCase.EjecutarAsync(idInvalido, "Embarcadero 1", "68081", "carpeta1", -73.8, 7.0);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ID de proyecto*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task GuardarSubproyecto_ConNombreInvalido_LanzaArgumentException(string? nombreInvalido)
    {
        var useCase = new GuardarSubproyectoUseCase(_mockRepo.Object);

        var act = () => useCase.EjecutarAsync(10, nombreInvalido!, "68081", "carpeta1", -73.8, 7.0);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nombre del subproyecto es obligatorio*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task GuardarSubproyecto_ConSubcarpetaInvalida_LanzaArgumentException(string? subcarpetaInvalida)
    {
        var useCase = new GuardarSubproyectoUseCase(_mockRepo.Object);

        var act = () => useCase.EjecutarAsync(10, "Embarcadero 1", "68081", subcarpetaInvalida!, -73.8, 7.0);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*subcarpeta del subproyecto es obligatoria*");
    }

    [Fact]
    public async Task GuardarSubproyecto_ConDatosValidos_NormalizaSubcarpetaYGuarda()
    {
        _mockRepo.Setup(r => r.InsertarSubproyectoAsync(
                10,
                "Embarcadero Fluvial",
                "68081",
                "Subcarpeta/Muelle_Norte",
                -73.85,
                7.06,
                "Descripción prueba",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var useCase = new GuardarSubproyectoUseCase(_mockRepo.Object);

        var nuevoId = await useCase.EjecutarAsync(
            10,
            "  Embarcadero Fluvial  ",
            " 68081 ",
            @" /Subcarpeta\Muelle_Norte/ ",
            -73.85,
            7.06,
            " Descripción prueba ");

        nuevoId.Should().Be(42);
        _mockRepo.Verify(r => r.InsertarSubproyectoAsync(
            10,
            "Embarcadero Fluvial",
            "68081",
            "Subcarpeta/Muelle_Norte",
            -73.85,
            7.06,
            "Descripción prueba",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

