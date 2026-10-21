using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class PermisosEliminacionProyectoTests
{
    private readonly Mock<ISeguridadService> _seguridadMock;
    private readonly Mock<INotificationService> _notificationsMock;
    private readonly Mock<IProyectoRepository> _proyectoRepoMock;
    private readonly Mock<IMunicipioRepository> _municipioRepoMock;
    private readonly ProyectoDetalleDto _proyectoDto;

    public PermisosEliminacionProyectoTests()
    {
        _seguridadMock = new Mock<ISeguridadService>();
        _notificationsMock = new Mock<INotificationService>();
        _proyectoRepoMock = new Mock<IProyectoRepository>();
        _municipioRepoMock = new Mock<IMunicipioRepository>();

        _municipioRepoMock
            .Setup(m => m.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto> { new DepartamentoDto("68", "Santander") });

        _municipioRepoMock
            .Setup(m => m.ListarTodosMunicipiosAsync())
            .ReturnsAsync(new List<MunicipioDto> { new MunicipioDto("68001", "Bucaramanga") });

        _proyectoDto = new ProyectoDetalleDto(
            Id: 42,
            Titulo: "Proyecto Geomatica Permisos Test",
            Descripcion: "Validación de permisos de eliminación por rol AD",
            FechaInicio: DateTime.Today,
            PalabraClave: "seguridad, ad, permisos",
            RutaArchivos: null,
            Lon: -73.12,
            Lat: 7.14,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );
    }

    [Fact]
    public void FichaProyectoViewModel_CuandoUsuarioEsAdministrador_PermiteEliminar()
    {
        // Arrange
        _seguridadMock.Setup(s => s.PuedeEliminarProyectos()).Returns(true);

        // Act
        var vm = new FichaProyectoViewModel(
            _proyectoDto,
            volverAction: () => { },
            eliminarProyectoUseCase: new EliminarProyectoUseCase(_proyectoRepoMock.Object),
            notifications: _notificationsMock.Object,
            seguridadService: _seguridadMock.Object);

        // Assert
        vm.PuedeEliminar.Should().BeTrue();
        vm.TooltipEliminacion.Should().Be("Eliminar este proyecto del sistema");
        vm.EliminarCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void FichaProyectoViewModel_CuandoUsuarioNoEsAdministrador_DeniegaEliminarConTooltipInformativo()
    {
        // Arrange
        _seguridadMock.Setup(s => s.PuedeEliminarProyectos()).Returns(false);

        // Act
        var vm = new FichaProyectoViewModel(
            _proyectoDto,
            volverAction: () => { },
            eliminarProyectoUseCase: new EliminarProyectoUseCase(_proyectoRepoMock.Object),
            notifications: _notificationsMock.Object,
            seguridadService: _seguridadMock.Object);

        // Assert
        vm.PuedeEliminar.Should().BeFalse();
        vm.TooltipEliminacion.Should().Contain("Acceso restringido: Solo los miembros del rol Administrador");
        vm.TooltipEliminacion.Should().Contain("G4_GEOMATICA_ADM");
        vm.EliminarCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void EditarProyectoViewModel_CuandoUsuarioEsAdministrador_HabilitaPuedeEliminar()
    {
        // Arrange
        _seguridadMock.Setup(s => s.PuedeEliminarProyectos()).Returns(true);
        var useCase = new ActualizarProyectoUseCase(_proyectoRepoMock.Object);
        var eliminarUseCase = new EliminarProyectoUseCase(_proyectoRepoMock.Object);

        // Act
        var vm = new EditarProyectoViewModel(
            useCase,
            _municipioRepoMock.Object,
            _proyectoDto,
            navigateBack: () => { },
            onProyectoEditado: null,
            eliminarProyectoUseCase: eliminarUseCase,
            notifications: _notificationsMock.Object,
            archivosService: null,
            seguridadService: _seguridadMock.Object);

        // Assert
        vm.EsModoEdicion.Should().BeTrue();
        vm.PuedeEliminar.Should().BeTrue();
        vm.TooltipEliminar.Should().Be("Eliminar este proyecto del sistema");
    }

    [Fact]
    public void EditarProyectoViewModel_CuandoUsuarioNoEsAdministrador_DeshabilitaPuedeEliminar()
    {
        // Arrange
        _seguridadMock.Setup(s => s.PuedeEliminarProyectos()).Returns(false);
        var useCase = new ActualizarProyectoUseCase(_proyectoRepoMock.Object);
        var eliminarUseCase = new EliminarProyectoUseCase(_proyectoRepoMock.Object);

        // Act
        var vm = new EditarProyectoViewModel(
            useCase,
            _municipioRepoMock.Object,
            _proyectoDto,
            navigateBack: () => { },
            onProyectoEditado: null,
            eliminarProyectoUseCase: eliminarUseCase,
            notifications: _notificationsMock.Object,
            archivosService: null,
            seguridadService: _seguridadMock.Object);

        // Assert
        vm.EsModoEdicion.Should().BeTrue();
        vm.PuedeEliminar.Should().BeFalse();
        vm.TooltipEliminar.Should().Contain("Acceso restringido: Solo los miembros del rol Administrador");
        vm.TooltipEliminar.Should().Contain("G4_GEOMATICA_ADM");
    }

    [Fact]
    public void CrearProyectoViewModel_EnModoCreacion_PuedeEliminarSiempreEsFalse()
    {
        // Arrange
        var crearUseCase = new CrearProyectoUseCase(_proyectoRepoMock.Object);

        // Act
        var vm = new CrearProyectoViewModel(
            crearUseCase,
            _municipioRepoMock.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { });

        // Assert
        vm.EsModoEdicion.Should().BeFalse();
        vm.PuedeEliminar.Should().BeFalse();
    }
}
