using Geomatica.Data.Repositories;
using Geomatica.Desktop.ViewModels;

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
}

