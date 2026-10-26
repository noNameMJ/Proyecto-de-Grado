using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.UnitTests.ViewModels;

public class FiltrosViewModelTests
{
    private readonly Mock<IMunicipioRepository> _municipioRepoMock;

    public FiltrosViewModelTests()
    {
        _municipioRepoMock = new Mock<IMunicipioRepository>();
    }

    [Fact]
    public void NotificarErrorConexion_DebeActivarEstadoErrorYGuardarMensaje()
    {
        // Arrange
        var vm = new FiltrosViewModel(_municipioRepoMock.Object);

        // Act
        vm.NotificarErrorConexion("Fallo de red al conectar con PostgreSQL");

        // Assert
        vm.IsErrorConexionDb.Should().BeTrue();
        vm.MensajeErrorConexion.Should().Contain("Fallo de red al conectar con PostgreSQL");
        vm.NoHayResultados.Should().BeFalse(); // Si hay error de BD, no es simplemente "sin resultados"
    }

    [Fact]
    public async Task ReintentarConexionAsync_CuandoRecuperaConexion_DebeLimpiarErrorYRecargarDepartamentos()
    {
        // Arrange
        _municipioRepoMock
            .Setup(r => r.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto>
            {
                new("68", "Santander"),
                new("11", "Bogotá D.C.")
            });

        var vm = new FiltrosViewModel(_municipioRepoMock.Object);
        vm.NotificarErrorConexion("Error previo");
        vm.IsErrorConexionDb.Should().BeTrue();

        bool buscarDisparado = false;
        vm.BuscarSolicitado += (s, e) => buscarDisparado = true;

        // Act
        await vm.ReintentarConexionAsync();

        // Assert
        vm.IsErrorConexionDb.Should().BeFalse();
        vm.Departamentos.Should().HaveCount(3); // "— Todos —" + Santander + Bogotá
        vm.SelectedDepartamento.Should().Be(FiltrosViewModel.DepartamentoItem.Todos);
        buscarDisparado.Should().BeTrue();
    }

    [Fact]
    public async Task ReintentarConexionAsync_SiFallaNuevamente_DebeMantenerEstadoError()
    {
        // Arrange
        _municipioRepoMock
            .Setup(r => r.ListarDepartamentosAsync())
            .ThrowsAsync(new TimeoutException("Tiempo de espera agotado al conectar"));

        var vm = new FiltrosViewModel(_municipioRepoMock.Object);

        // Act
        await vm.ReintentarConexionAsync();

        // Assert
        vm.IsErrorConexionDb.Should().BeTrue();
        vm.MensajeErrorConexion.Should().Contain("Fallo al reconectar con PostgreSQL");
    }

    [Fact]
    public void HasResultados_Y_CanReintentarConexion_DebeCalcularseCorrectamente()
    {
        // Arrange
        var vm = new FiltrosViewModel(_municipioRepoMock.Object);

        // Assert inicial
        vm.HasResultados.Should().BeFalse();
        vm.CanReintentarConexion.Should().BeTrue();

        // Act: agregar resultado
        vm.ResultadosLista.Add(new FiltrosViewModel.ProyectoItem(5, "Proyecto de Prueba", -73.12, 7.14, "C:\\Ruta"));
        vm.NotificarResultadosCargados();

        // Assert
        vm.HasResultados.Should().BeTrue();
    }

    [Fact]
    public void ProyectoItem_FormateaPropiedadesVisualesYCoordenadasCorrectamente()
    {
        // Arrange & Act
        var itemConCoords = new FiltrosViewModel.ProyectoItem(
            12,
            "Proyecto Bucaramanga",
            -73.1234,
            7.1234,
            "C:\\Ruta",
            new DateTime(2026, 4, 15),
            "cuenca, rio",
            "Ing. María Gómez",
            -73.25, 7.10, -73.10, 7.20);
        var itemSinCoords = new FiltrosViewModel.ProyectoItem(3, "Proyecto Sin Coordenadas", 0, 0, null);

        // Assert
        itemConCoords.CodigoId.Should().Be("#PROY-0012");
        itemConCoords.CoordenadasTexto.Should().Contain("7.1234°N").And.Contain("-73.1234°W");
        itemConCoords.FechaTexto.Should().Be("15/04/2026");
        itemConCoords.HasPalabrasClave.Should().BeTrue();
        itemConCoords.TieneExtentValido.Should().BeTrue();

        itemSinCoords.CodigoId.Should().Be("#PROY-0003");
        itemSinCoords.CoordenadasTexto.Should().Be("Sin coordenadas");
        itemSinCoords.FechaTexto.Should().Be("Sin fecha");
        itemSinCoords.HasPalabrasClave.Should().BeFalse();
        itemSinCoords.TieneExtentValido.Should().BeFalse();
    }

    [Fact]
    public void ProyectoItem_Subproyectos_CalculaPropiedadesVisualesYBadgeCorrectamente()
    {
        // Arrange
        var sub1 = new SubproyectoDto(1, 10, "Embarcadero Norte", "68081", "Barrancabermeja", "embarcadero_norte", -73.85, 7.06);
        var sub2 = new SubproyectoDto(2, 10, "Embarcadero Sur", "68081", "Barrancabermeja", "embarcadero_sur", -73.84, 7.05);

        var item = new FiltrosViewModel.ProyectoItem(10, "Proyecto Río Magdalena", -73.85, 7.06, @"D:\Proyectos\Rio");

        // Assert inicial (sin subproyectos)
        item.HasSubproyectos.Should().BeFalse();
        item.TotalSubproyectos.Should().Be(0);
        item.SubproyectosBadgeTexto.Should().Be("0 subproyectos / frentes");
        item.EstaExpandido.Should().BeFalse();

        // Act: agregar 1 subproyecto vía ActualizarSubproyectos
        item.ActualizarSubproyectos(new[] { sub1 });

        // Assert 1 subproyecto
        item.HasSubproyectos.Should().BeTrue();
        item.TotalSubproyectos.Should().Be(1);
        item.SubproyectosBadgeTexto.Should().Be("1 subproyecto / frente");

        // Act: agregar segundo subproyecto y expandir
        item.ActualizarSubproyectos(new[] { sub1, sub2 });
        item.EstaExpandido = true;

        // Assert múltiples subproyectos
        item.HasSubproyectos.Should().BeTrue();
        item.TotalSubproyectos.Should().Be(2);
        item.SubproyectosBadgeTexto.Should().Be("2 subproyectos / frentes");
        item.EstaExpandido.Should().BeTrue();
        item.Subproyectos.Should().Contain(sub1).And.Contain(sub2);
    }

    [Fact]
    public void UbicarSubproyectoCommand_ConCoordenadasValidas_DisparaSubproyectoSeleccionadoEnMapa()
    {
        // Arrange
        var vm = new FiltrosViewModel(_municipioRepoMock.Object);
        var subValido = new SubproyectoDto(1, 10, "Sitio A", "68081", "Barrancabermeja", "sitio_a", -73.85, 7.06);
        var subSinCoords = new SubproyectoDto(2, 10, "Sitio B", "68081", "Barrancabermeja", "sitio_b", 0, 0);

        SubproyectoDto? recibido = null;
        vm.SubproyectoSeleccionadoEnMapa += (s, e) => recibido = e;

        // Act & Assert para subproyecto con coordenadas
        vm.UbicarSubproyectoCommand.Execute(subValido);
        recibido.Should().NotBeNull();
        recibido!.IdSubproyecto.Should().Be(1);
        recibido.Nombre.Should().Be("Sitio A");

        // Act & Assert para subproyecto sin coordenadas (0,0) - no debe disparar
        recibido = null;
        vm.UbicarSubproyectoCommand.Execute(subSinCoords);
        recibido.Should().BeNull();
    }

    [Fact]
    public void AbrirArchivosSubproyectoCommand_DisparaNavegacionSubcarpetaSolicitada()
    {
        // Arrange
        var vm = new FiltrosViewModel(_municipioRepoMock.Object);
        var sub = new SubproyectoDto(1, 10, "Embarcadero El Llanito", "68081", "Barrancabermeja", "el_llanito", -73.85, 7.06);

        SubproyectoDto? subRecibido = null;
        vm.NavegacionSubcarpetaSolicitada += (s, e) => subRecibido = e;

        // Act
        vm.AbrirArchivosSubproyectoCommand.Execute(sub);

        // Assert
        subRecibido.Should().NotBeNull();
        subRecibido!.Subcarpeta.Should().Be("el_llanito");
    }

    [Fact]
    public async Task InicializarFiltrosAsync_CargaDepartamentosYMunicipiosSinDispararBusquedaRedundante()
    {
        // Arrange
        _municipioRepoMock.Setup(r => r.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto> { new("68", "Santander") });
        _municipioRepoMock.Setup(r => r.ListarTodosMunicipiosAsync())
            .ReturnsAsync(new List<MunicipioDto> { new("68001", "Bucaramanga") });

        var vm = new FiltrosViewModel(_municipioRepoMock.Object);
        bool buscarDisparado = false;
        vm.BuscarSolicitado += (s, e) => buscarDisparado = true;

        // Act
        await vm.InicializarFiltrosAsync();

        // Assert
        vm.Departamentos.Should().HaveCount(2); // "— Todos —" + Santander
        vm.Areas.Should().HaveCount(2); // "— Todos —" + Bucaramanga
        vm.SelectedDepartamento.Should().Be(FiltrosViewModel.DepartamentoItem.Todos);
        vm.AreaInteres.Should().Be(FiltrosViewModel.MunicipioItem.Todos);
        vm.IsErrorConexionDb.Should().BeFalse();
        buscarDisparado.Should().BeFalse(); // No debe generar rebotes de búsqueda durante inicialización
    }
}
