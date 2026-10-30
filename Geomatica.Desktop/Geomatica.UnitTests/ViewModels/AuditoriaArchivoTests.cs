using System.IO;
using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class AuditoriaArchivoTests
{
    private readonly Mock<IProyectoRepository> _repoMock;

    public AuditoriaArchivoTests()
    {
        _repoMock = new Mock<IProyectoRepository>();
    }

    [Fact]
    public void AuditoriaArchivoDto_FormatoYBadges_CalculaCorrectamente()
    {
        // Arrange
        var itemEliminado = new AuditoriaArchivoDto(
            IdAuditoriaArchivo: 1,
            IdProyecto: 10,
            RutaRelativa: "Cartografia/Vias/vias.shp",
            NombreArchivo: "vias.shp",
            Accion: "ELIMINAR",
            Usuario: @"UIS\gisadmin",
            Equipo: "PC-GIS-01",
            FechaHora: new DateTime(2026, 10, 27, 14, 30, 0),
            TamanoBytes: 1048576 * 5, // 5 MB
            Detalles: "Archivo eliminado por limpieza"
        );

        var itemCreado = new AuditoriaArchivoDto(
            IdAuditoriaArchivo: 2,
            IdProyecto: 10,
            RutaRelativa: "Ortofotos/mosaico.tif",
            NombreArchivo: "mosaico.tif",
            Accion: "CREACION",
            Usuario: @"UIS\topografo",
            Equipo: "PC-TOP-02",
            FechaHora: new DateTime(2026, 10, 27, 8, 0, 0),
            TamanoBytes: 512, // 512 B
            Detalles: "Ortofoto inicial"
        );

        // Assert
        itemEliminado.EsEliminado.Should().BeTrue();
        itemEliminado.AccionIcono.Should().Be("🗑️");
        itemEliminado.TamanoTexto.Should().Be("5,00 MB");
        itemEliminado.FechaHoraTexto.Should().Be("27/10/2026 14:30:00");

        itemCreado.EsEliminado.Should().BeFalse();
        itemCreado.AccionIcono.Should().Be("✨");
        itemCreado.TamanoTexto.Should().Be("512 B");
    }

    [Fact]
    public async Task HistorialArchivoViewModel_ModoArchivo_CargaMaximo5RegistrosYActualizaContador()
    {
        // Arrange
        var mockHistorial = new List<AuditoriaArchivoDto>
        {
            new(5, 10, "Docs/informe.pdf", "informe.pdf", "MODIFICACION", "user1", "PC1", DateTime.Now, 2048, "Edición 5"),
            new(4, 10, "Docs/informe.pdf", "informe.pdf", "MODIFICACION", "user2", "PC2", DateTime.Now.AddHours(-1), 2000, "Edición 4"),
            new(3, 10, "Docs/informe.pdf", "informe.pdf", "MOVER", "user1", "PC1", DateTime.Now.AddHours(-2), 1950, "Movido"),
            new(2, 10, "Docs/informe.pdf", "informe.pdf", "RENOMBRAR", "user1", "PC1", DateTime.Now.AddHours(-3), 1900, "Renombrado"),
            new(1, 10, "Docs/informe.pdf", "informe.pdf", "CREACION", "user3", "PC3", DateTime.Now.AddHours(-4), 1800, "Creado")
        };

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivoAsync(10, "Docs/informe.pdf", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHistorial);

        var vm = new HistorialArchivoViewModel(
            idProyecto: 10,
            rutaRelativa: "Docs/informe.pdf",
            nombreArchivo: "informe.pdf",
            tituloProyecto: "Proyecto Embarcaderos",
            proyectoRepository: _repoMock.Object,
            soloEliminados: false);

        // Act
        await vm.CargarHistorialAsync();

        // Assert
        vm.Historial.Should().HaveCount(5);
        vm.HasHistorial.Should().BeTrue();
        vm.HasNoHistorial.Should().BeFalse();
        vm.ContadorTexto.Should().Be("5 movimiento(s) registrado(s)");
        vm.TituloVentana.Should().Contain("informe.pdf");
        vm.SubtituloVentana.Should().Contain("Proyecto Embarcaderos");
    }

    [Fact]
    public async Task HistorialArchivoViewModel_ModoEliminados_CargaArchivosEliminados()
    {
        // Arrange
        var mockEliminados = new List<AuditoriaArchivoDto>
        {
            new(1, 10, "Cartografia/limite.shp", "limite.shp", "ELIMINAR", "user1", "PC1", DateTime.Now, 10000, "Eliminado"),
            new(2, 10, "LiDAR/nube_puntos.las", "nube_puntos.las", "ELIMINAR", "user2", "PC2", DateTime.Now.AddDays(-1), 500000, "Eliminado")
        };

        _repoMock
            .Setup(r => r.ObtenerArchivosEliminadosProyectoAsync(10, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEliminados);

        var vm = new HistorialArchivoViewModel(
            idProyecto: 10,
            rutaRelativa: string.Empty,
            nombreArchivo: string.Empty,
            tituloProyecto: "Proyecto Embarcaderos",
            proyectoRepository: _repoMock.Object,
            soloEliminados: true);

        // Act
        await vm.CargarHistorialAsync();

        // Assert
        vm.Historial.Should().HaveCount(2);
        vm.SoloEliminados.Should().BeTrue();
        vm.ContadorTexto.Should().Be("2 archivo(s) eliminado(s)");
        vm.TituloVentana.Should().Be("Registro de Archivos Eliminados");
        vm.SubtituloVentana.Should().Contain("Proyecto Embarcaderos");
    }

    [Fact]
    public async Task FichaProyectoViewModel_SubpestanaAuditoria_AlternaEntreProyectoYArchivos()
    {
        // Arrange
        var proyectoDto = new ProyectoDetalleDto(
            Id: 10,
            Titulo: "Proyecto Test Embarcaderos",
            Descripcion: "Desc",
            FechaInicio: DateTime.Today,
            PalabraClave: "test",
            RutaArchivos: null,
            Lon: -73.0,
            Lat: 7.0,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga"
        );

        var mockArchivos = new List<AuditoriaArchivoDto>
        {
            new(1, 10, "Shapes/rio.shp", "rio.shp", "CREACION", "user_gis", "PC1", DateTime.Now, 15000, "Capa de río creada")
        };

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivosProyectoAsync(10, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockArchivos);

        var vm = new FichaProyectoViewModel(
            proyecto: proyectoDto,
            volverAction: () => { },
            proyectoRepository: _repoMock.Object);

        // Assert inicial: subpestaña de Proyecto seleccionada
        vm.EsAuditoriaProyectoSeleccionada.Should().BeTrue();
        vm.EsAuditoriaArchivosSeleccionada.Should().BeFalse();

        // Act: cambiar a actividad de archivos
        vm.SeleccionarAuditoriaArchivosCommand.Execute(null);

        // Assert: subpestaña cambia y carga archivos
        vm.EsAuditoriaProyectoSeleccionada.Should().BeFalse();
        vm.EsAuditoriaArchivosSeleccionada.Should().BeTrue();
        vm.HistorialArchivos.Should().HaveCount(1);
        vm.HasHistorialArchivos.Should().BeTrue();

        // Act: regresar a metadatos del proyecto
        vm.SeleccionarAuditoriaProyectoCommand.Execute(null);
        vm.EsAuditoriaProyectoSeleccionada.Should().BeTrue();
        vm.EsAuditoriaArchivosSeleccionada.Should().BeFalse();
    }

    [Fact]
    public async Task UseCases_AuditoriaArchivo_EjecutanConParametrosCorrectos()
    {
        // Arrange
        var getFileHistory = new ObtenerHistorialArchivoUseCase(_repoMock.Object);
        var getProjectFiles = new ObtenerHistorialArchivosProyectoUseCase(_repoMock.Object);
        var getDeleted = new ObtenerArchivosEliminadosUseCase(_repoMock.Object);
        var recordFile = new RegistrarAuditoriaArchivoUseCase(_repoMock.Object);

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivoAsync(10, "test.shp", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditoriaArchivoDto>());

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivosProyectoAsync(10, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditoriaArchivoDto>());

        _repoMock
            .Setup(r => r.ObtenerArchivosEliminadosProyectoAsync(10, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditoriaArchivoDto>());

        // Act
        var res1 = await getFileHistory.EjecutarAsync(10, "test.shp", 5);
        var res2 = await getProjectFiles.EjecutarAsync(10, 50);
        var res3 = await getDeleted.EjecutarAsync(10, 50);
        await recordFile.EjecutarAsync(10, "test.shp", "test.shp", "CREACION", "Detalles", 100);

        // Assert
        res1.Should().NotBeNull();
        res2.Should().NotBeNull();
        res3.Should().NotBeNull();
        _repoMock.Verify(r => r.RegistrarAuditoriaArchivoAsync(
            10, "test.shp", "test.shp", "CREACION", "Detalles", 100, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void HistorialArchivoViewModel_CerrarCommand_DisparaEventoRequestClose()
    {
        // Arrange
        bool fueCerrado = false;
        var vm = new HistorialArchivoViewModel(
            idProyecto: 10,
            rutaRelativa: "test.docx",
            nombreArchivo: "test.docx",
            tituloProyecto: "Proyecto Embarcaderos",
            proyectoRepository: _repoMock.Object,
            soloEliminados: false,
            cerrarAction: () => fueCerrado = true);

        // Act
        vm.CerrarCommand.Execute(null);

        // Assert
        fueCerrado.Should().BeTrue();
    }

    [Fact]
    public void ArchivosViewModel_VerHistorialArchivo_SinSeleccion_NoLanzaExcepcion()
    {
        // Arrange
        var filtros = new FiltrosViewModel(null);
        var service = new Geomatica.Desktop.Services.ProyectoArchivosService();
        var vm = new ArchivosViewModel(filtros, service, _repoMock.Object);

        // Act
        var act = () => vm.VerHistorialArchivo();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ArchivosViewModel_VerArchivosEliminados_SinProyecto_NoLanzaExcepcion()
    {
        // Arrange
        var filtros = new FiltrosViewModel(null);
        var service = new Geomatica.Desktop.Services.ProyectoArchivosService();
        var vm = new ArchivosViewModel(filtros, service, _repoMock.Object);

        // Act
        var act = () => vm.VerArchivosEliminados();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ArchivosViewModel_RegistrarAuditoriaArchivoAsync_InvocaRepositorio()
    {
        // Arrange
        var filtros = new FiltrosViewModel(null);
        var service = new Geomatica.Desktop.Services.ProyectoArchivosService();
        var vm = new ArchivosViewModel(filtros, service, _repoMock.Object);

        // Asignar proyecto detalle activo
        vm.ProyectoDetalle = new FichaProyectoViewModel(
            new ProyectoDetalleDto(15, "Proyecto Test", "Desc", DateTime.Today, "kw", null, -73, 7, "68001", "Bucaramanga"),
            () => { },
            proyectoRepository: _repoMock.Object);

        // Act
        await vm.RegistrarAuditoriaArchivoAsync(
            accion: "CREAR_CARPETA",
            detalles: "Nueva carpeta creada",
            rutaRelativa: "Datos_Espaciales/Subcarpeta",
            nombreArchivo: "Subcarpeta",
            tamanoBytes: 0,
            fechaArchivo: DateTime.UtcNow);

        // Assert
        _repoMock.Verify(r => r.RegistrarAuditoriaAsync(
            15, "Proyecto Test", "CREAR_CARPETA", "Nueva carpeta creada", null, null, It.IsAny<CancellationToken>()), Times.Once);

        _repoMock.Verify(r => r.RegistrarAuditoriaArchivoAsync(
            15,
            "Datos_Espaciales/Subcarpeta",
            "Subcarpeta",
            "CREAR_CARPETA",
            "Nueva carpeta creada",
            0,
            null,
            null,
            It.IsNotNull<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void HistorialArchivoViewModel_SubtituloVentana_FormateaCorrectamente()
    {
        // Arrange
        var vm = new HistorialArchivoViewModel(
            idProyecto: 20,
            rutaRelativa: "Sub/plano.dwg",
            nombreArchivo: "plano.dwg",
            tituloProyecto: "Construcción Sede UIS",
            proyectoRepository: _repoMock.Object,
            soloEliminados: false);

        // Assert
        vm.SubtituloVentana.Should().Contain("Construcción Sede UIS");
        vm.SubtituloVentana.Should().Contain("Movimientos recientes");
        vm.SoloEliminados.Should().BeFalse();
    }
    [Fact]
    public async Task HistorialArchivoViewModel_ModoProyecto_CargaActividadGlobal()
    {
        // Arrange
        var mockActividad = new List<AuditoriaArchivoDto>
        {
            new(1, 10, "Cartografia/limite.shp", "limite.shp", "CREACION", "user1", "PC1", DateTime.Now, 10000, "Creado"),
            new(2, 10, "Docs/memoria.pdf", "memoria.pdf", "MODIFICACION", "user2", "PC2", DateTime.Now, 2000, "Modificado")
        };

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivosProyectoAsync(10, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockActividad);

        var vm = new HistorialArchivoViewModel(
            idProyecto: 10,
            rutaRelativa: string.Empty,
            nombreArchivo: string.Empty,
            tituloProyecto: "Proyecto Embarcaderos",
            proyectoRepository: _repoMock.Object,
            soloEliminados: false);

        // Act
        await vm.CargarHistorialAsync();

        // Assert
        vm.EsModoProyecto.Should().BeTrue();
        vm.Historial.Should().HaveCount(2);
        vm.ContadorTexto.Should().Be("2 movimiento(s)");
        vm.TituloVentana.Should().Be("Actividad de Archivos del Proyecto");
        vm.SubtituloVentana.Should().Contain("Historial completo de movimientos");
    }

    [Fact]
    public async Task FichaProyectoViewModel_CargarHistorialArchivos_LimitaA5YActivaTieneMas()
    {
        // Arrange
        var proyectoDto = new ProyectoDetalleDto(10, "Proyecto Test", "Desc", DateTime.Today, "kw", null, -73.0, 7.0, "68001", "Bucaramanga");
        var mockLista7 = new List<AuditoriaArchivoDto>();
        for (int i = 1; i <= 7; i++)
        {
            mockLista7.Add(new(i, 10, $"archivo_{i}.shp", $"archivo_{i}.shp", "CREACION", "user", "PC", DateTime.Now, 100, "Detalle"));
        }

        _repoMock
            .Setup(r => r.ObtenerHistorialArchivosProyectoAsync(10, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLista7);

        var vm = new FichaProyectoViewModel(proyectoDto, () => { }, proyectoRepository: _repoMock.Object);

        // Act
        await vm.CargarHistorialArchivosAsync();

        // Assert
        vm.HistorialArchivos.Should().HaveCount(5); // Solo muestra 5 en la ficha
        vm.TotalMovimientosArchivos.Should().Be(7);
        vm.TieneMasMovimientosArchivos.Should().BeTrue();
        vm.TextoVerMasArchivos.Should().Be("Ver todos los movimientos (7)...");
    }

    [Fact]
    public void FichaProyectoViewModel_VerMasActividadArchivos_InvocaModalAction()
    {
        // Arrange
        var proyectoDto = new ProyectoDetalleDto(10, "Proyecto Test", "Desc", DateTime.Today, "kw", null, -73.0, 7.0, "68001", "Bucaramanga");
        var vm = new FichaProyectoViewModel(proyectoDto, () => { }, proyectoRepository: _repoMock.Object);

        HistorialArchivoViewModel? vmModalCapturado = null;
        vm.AbrirModalHistorialAction = modalVm => vmModalCapturado = modalVm;

        // Act
        vm.VerMasActividadArchivosCommand.Execute(null);

        // Assert
        vmModalCapturado.Should().NotBeNull();
        vmModalCapturado!.EsModoProyecto.Should().BeTrue();
        vmModalCapturado.TituloVentana.Should().Be("Actividad de Archivos del Proyecto");
    }
}
