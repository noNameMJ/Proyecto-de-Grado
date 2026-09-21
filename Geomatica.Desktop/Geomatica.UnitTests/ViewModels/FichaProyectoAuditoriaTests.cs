using FluentAssertions;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class FichaProyectoAuditoriaTests
{
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly ProyectoDetalleDto _proyectoDto;

    public FichaProyectoAuditoriaTests()
    {
        _repoMock = new Mock<IProyectoRepository>();
        _proyectoDto = new ProyectoDetalleDto(
            Id: 5,
            Titulo: "Proyecto Auditoria UIS",
            Descripcion: "Descripcion del proyecto",
            Fecha: new DateTime(2026, 9, 1),
            PalabraClave: "geomatica, uis",
            RutaArchivos: null,
            Lon: -73.12,
            Lat: 7.14,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );
    }

    [Fact]
    public async Task CargarHistorialAuditoriaAsync_ConEventosCreacionYModificacion_CalculaMetadatosCorrectamente()
    {
        // Arrange
        var historial = new List<AuditoriaProyectoDto>
        {
            // El más reciente primero (ORDER BY fecha_hora DESC)
            new(
                IdAuditoria: 2,
                IdProyecto: 5,
                TituloProyecto: "Proyecto Auditoria UIS",
                Accion: "MODIFICACION",
                Usuario: @"GEOMATICAAD\editor.uis",
                Equipo: "WS-EDITOR",
                FechaHora: new DateTime(2026, 9, 15, 10, 30, 0),
                Detalles: "Proyecto actualizado."
            ),
            new(
                IdAuditoria: 1,
                IdProyecto: 5,
                TituloProyecto: "Proyecto Auditoria UIS",
                Accion: "CREACION",
                Usuario: @"GEOMATICAAD\creador.uis",
                Equipo: "WS-CREADOR",
                FechaHora: new DateTime(2026, 9, 1, 8, 0, 0),
                Detalles: "Proyecto creado en el sistema."
            )
        };

        _repoMock
            .Setup(r => r.ObtenerHistorialProyectoAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(historial);

        var vm = new FichaProyectoViewModel(
            proyecto: _proyectoDto,
            volverAction: () => { },
            eliminarProyectoUseCase: null,
            onProyectoEliminado: null,
            archivosService: null,
            notifications: null,
            proyectoRepository: _repoMock.Object
        );

        // Act
        await vm.CargarHistorialAuditoriaAsync();

        // Assert
        vm.HistorialAuditoria.Should().HaveCount(2);
        vm.HasHistorial.Should().BeTrue();
        vm.HasNoHistorial.Should().BeFalse();
        vm.HasCreadorInfo.Should().BeTrue();
        vm.CreadorInfo.Should().Contain(@"GEOMATICAAD\creador.uis");
        vm.CreadorInfo.Should().Contain("01/09/2026");
        vm.HasUltimaModificacionInfo.Should().BeTrue();
        vm.UltimaModificacionInfo.Should().Contain(@"GEOMATICAAD\editor.uis");
        vm.UltimaModificacionInfo.Should().Contain("15/09/2026 10:30");
        vm.TextoBotonHistorial.Should().Be("▼ Ver historial (2)");
    }

    [Fact]
    public void ToggleHistorial_DebeAlternarMostrarHistorialYTextoBoton()
    {
        // Arrange
        var vm = new FichaProyectoViewModel(
            proyecto: _proyectoDto,
            volverAction: () => { },
            proyectoRepository: null
        );

        vm.MostrarHistorial.Should().BeFalse();
        vm.TextoBotonHistorial.Should().Be("▼ Ver historial");

        // Act 1: Mostrar
        vm.ToggleHistorialCommand.Execute(null);

        // Assert 1
        vm.MostrarHistorial.Should().BeTrue();
        vm.TextoBotonHistorial.Should().Be("▲ Ocultar historial");

        // Act 2: Ocultar
        vm.ToggleHistorialCommand.Execute(null);

        // Assert 2
        vm.MostrarHistorial.Should().BeFalse();
        vm.TextoBotonHistorial.Should().Be("▼ Ver historial");
    }

    [Fact]
    public async Task CargarHistorialAuditoriaAsync_SinRegistros_HasNoHistorialDebeSerTrue()
    {
        // Arrange
        _repoMock
            .Setup(r => r.ObtenerHistorialProyectoAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditoriaProyectoDto>());

        var vm = new FichaProyectoViewModel(
            proyecto: _proyectoDto,
            volverAction: () => { },
            proyectoRepository: _repoMock.Object
        );

        // Act
        await vm.CargarHistorialAuditoriaAsync();

        // Assert
        vm.HistorialAuditoria.Should().BeEmpty();
        vm.HasHistorial.Should().BeFalse();
        vm.HasNoHistorial.Should().BeTrue();
        vm.HasCreadorInfo.Should().BeFalse();
        vm.HasUltimaModificacionInfo.Should().BeFalse();
    }
}

