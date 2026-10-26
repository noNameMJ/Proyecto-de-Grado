using FluentAssertions;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class FormularioProyectoSubproyectosTests
{
    private static (Mock<IProyectoRepository> mockProy, Mock<IMunicipioRepository> mockMuni, Mock<INotificationService> mockNotif) CrearMocks()
    {
        var mockProy = new Mock<IProyectoRepository>();
        var mockMuni = new Mock<IMunicipioRepository>();
        var mockNotif = new Mock<INotificationService>();

        mockMuni.Setup(m => m.ListarDepartamentosAsync())
            .ReturnsAsync(new List<DepartamentoDto>
            {
                new("68", "Santander"),
                new("20", "Cesar")
            });

        mockMuni.Setup(m => m.ListarMunicipiosPorDepartamentoAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<MunicipioDto>
            {
                new("68081", "Barrancabermeja"),
                new("20295", "Gamarra")
            });

        mockMuni.Setup(m => m.ListarTodosMunicipiosAsync())
            .ReturnsAsync(new List<MunicipioDto>
            {
                new("68081", "Barrancabermeja"),
                new("20295", "Gamarra")
            });

        mockMuni.Setup(m => m.PuntoEstaEnMunicipioAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>()))
            .ReturnsAsync(true);

        return (mockProy, mockMuni, mockNotif);
    }

    [Fact]
    public void ModoCreacion_InicializaConColeccionVaciaYFormularioOculto()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            onProyectoCreado: null,
            notifications: mockNotif.Object);

        vm.HasSubproyectos.Should().BeFalse();
        vm.NoHaySubproyectos.Should().BeTrue();
        vm.TotalSubproyectos.Should().Be(0);
        vm.Subproyectos.Should().BeEmpty();
        vm.FormularioSubproyectoVisible.Should().BeFalse();
        vm.BotonAgregarSubproyectoVisible.Should().BeTrue();
    }

    [Fact]
    public void MostrarFormularioSubproyecto_AbreFormularioYHeradaCoordenadas()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.LatStr = "7.062000";
        vm.LonStr = "-73.854000";

        vm.MostrarFormularioSubproyectoCommand.Execute(null);

        vm.FormularioSubproyectoVisible.Should().BeTrue();
        vm.BotonAgregarSubproyectoVisible.Should().BeFalse();
        vm.NuevoSubproyectoLatStr.Should().Be("7.062000");
        vm.NuevoSubproyectoLonStr.Should().Be("-73.854000");
    }

    [Fact]
    public void GuardarNuevoSubproyecto_ValidaCamposRequeridos()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        // Sin nombre ni subcarpeta
        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().BeEmpty();
        mockNotif.Verify(n => n.ShowWarning(It.Is<string>(s => s.Contains("nombre")), It.IsAny<string?>(), It.IsAny<int>()), Times.Once);

        // Con nombre pero sin subcarpeta
        vm.NuevoSubproyectoNombre = "Frente 01 - Tramo Norte";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().BeEmpty();
        mockNotif.Verify(n => n.ShowWarning(It.Is<string>(s => s.Contains("subcarpeta")), It.IsAny<string?>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void GuardarNuevoSubproyecto_CamposValidos_AgregaALaListaYReseteaFormulario()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Frente 01 - Tramo Norte";
        vm.NuevoSubproyectoSubcarpeta = @"Datos_Espaciales\01_FrenteNorte";
        vm.NuevoSubproyectoLatStr = "8.318000";
        vm.NuevoSubproyectoLonStr = "-73.743000";
        vm.NuevoSubproyectoDescripcion = "Frente principal de levantamiento y adecuación";

        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().HaveCount(1);
        vm.HasSubproyectos.Should().BeTrue();
        vm.TotalSubproyectos.Should().Be(1);

        var item = vm.Subproyectos[0];
        item.Nombre.Should().Be("Frente 01 - Tramo Norte");
        item.Subcarpeta.Should().Be(@"Datos_Espaciales\01_FrenteNorte");
        item.Lat.Should().BeApproximately(8.318, 0.001);
        item.Lon.Should().BeApproximately(-73.743, 0.001);
        item.Descripcion.Should().Be("Frente principal de levantamiento y adecuación");
        item.HasDescripcion.Should().BeTrue();
        item.EsNuevo.Should().BeTrue();
        item.CoordenadasTexto.Should().Contain("8.3180°N");

        vm.FormularioSubproyectoVisible.Should().BeFalse();
        vm.BotonAgregarSubproyectoVisible.Should().BeTrue();
    }

    [Fact]
    public void QuitarSubproyecto_RemueveDeLista()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Subproyecto 1";
        vm.NuevoSubproyectoSubcarpeta = "Sub1";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().HaveCount(1);

        vm.QuitarSubproyectoCommand.Execute(vm.Subproyectos[0]);

        vm.Subproyectos.Should().BeEmpty();
        vm.HasSubproyectos.Should().BeFalse();
        vm.TotalSubproyectos.Should().Be(0);
    }

    [Fact]
    public async Task GuardarAsync_ModoCreacion_PersisteProyectoYSubproyectosNuevos()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        int proyectoGeneradoId = 77;

        mockProy.Setup(r => r.InsertarAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(proyectoGeneradoId);

        mockProy.Setup(r => r.InsertarSubproyectoAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(101);

        var crearUseCase = new CrearProyectoUseCase(mockProy.Object);
        var guardarSubUseCase = new GuardarSubproyectoUseCase(mockProy.Object);
        var obtenerSubUseCase = new ObtenerSubproyectosUseCase(mockProy.Object);
        var eliminarSubUseCase = new EliminarSubproyectoUseCase(mockProy.Object);

        bool navego = false;
        var vm = new FormularioProyectoViewModel(
            crearUseCase,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { navego = true; },
            onProyectoCreado: null,
            notifications: mockNotif.Object,
            guardarSubproyectoUseCase: guardarSubUseCase,
            obtenerSubproyectosUseCase: obtenerSubUseCase,
            eliminarSubproyectoUseCase: eliminarSubUseCase);

        vm.Titulo = "Proyecto Magdalena Medio";
        vm.SelectedMunicipio = new FormularioProyectoViewModel.MunicipioItem("68081", "Barrancabermeja");

        // Añadir subproyecto
        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Frente Barranca Centro";
        vm.NuevoSubproyectoSubcarpeta = "01_Centro";
        vm.NuevoSubproyectoLatStr = "7.062000";
        vm.NuevoSubproyectoLonStr = "-73.854000";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        await vm.GuardarCommand.ExecuteAsync(null);

        // Verificar que se insertó el proyecto y luego el subproyecto con id 77
        mockProy.Verify(r => r.InsertarAsync(
            It.Is<string>(t => t == "Proyecto Magdalena Medio"),
            It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);

        mockProy.Verify(r => r.InsertarSubproyectoAsync(
            77,
            "Frente Barranca Centro",
            It.IsAny<string?>(),
            "01_Centro",
            It.Is<double>(lon => Math.Abs(lon - (-73.854)) < 0.001),
            It.Is<double>(lat => Math.Abs(lat - 7.062) < 0.001),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        navego.Should().BeTrue();
    }

    [Fact]
    public void ActualizarSubcarpetasDisponibles_EscaneaSubdirectoriosProfundos_IgnoraGdbYOcultos()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        var tempDir = Path.Combine(Path.GetTempPath(), "Geomatica_Sub_Deep_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        
        // Estructura anidada multi-nivel
        Directory.CreateDirectory(Path.Combine(tempDir, "Datos_Espaciales", "Frentes", "Sector_01"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Datos_Espaciales", "Frentes", "Sector_02"));
        Directory.CreateDirectory(Path.Combine(tempDir, "Entregables", "Informes"));
        // Carpeta .gdb (debe ser omitida del escaneo)
        Directory.CreateDirectory(Path.Combine(tempDir, "Datos_Espaciales", "Cartografia.gdb"));

        try
        {
            vm.Ruta = tempDir;

            // Debe detectar los niveles profundos con rutas relativas
            vm.SubcarpetasDisponibles.Should().Contain(Path.Combine("Datos_Espaciales", "Frentes", "Sector_01"));
            vm.SubcarpetasDisponibles.Should().Contain(Path.Combine("Datos_Espaciales", "Frentes", "Sector_02"));
            vm.SubcarpetasDisponibles.Should().Contain(Path.Combine("Entregables", "Informes"));

            // No debe incluir carpetas internas de FileGDB
            vm.SubcarpetasDisponibles.Should().NotContain(Path.Combine("Datos_Espaciales", "Cartografia.gdb"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void EditarSubproyecto_CargaValoresEnFormulario_YActualizaEnGuardar()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        // 1. Agregar un subproyecto inicial
        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Frente Inicial";
        vm.NuevoSubproyectoSubcarpeta = "01_Frente";
        vm.NuevoSubproyectoLatStr = "7.050000";
        vm.NuevoSubproyectoLonStr = "-73.800000";
        vm.NuevoSubproyectoDescripcion = "Desc inicial";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().HaveCount(1);
        var sub = vm.Subproyectos[0];

        // 2. Activar modo edición sobre el subproyecto
        vm.EditarSubproyectoCommand.Execute(sub);

        vm.FormularioSubproyectoVisible.Should().BeTrue();
        vm.EsEdicionSubproyecto.Should().BeTrue();
        vm.TituloFormularioSubproyecto.Should().Contain("Editar");
        vm.TextoBotonGuardarSubproyecto.Should().Contain("Actualizar");
        vm.NuevoSubproyectoNombre.Should().Be("Frente Inicial");
        vm.NuevoSubproyectoSubcarpeta.Should().Be("01_Frente");
        vm.NuevoSubproyectoLatStr.Should().Be("7.050000");
        vm.NuevoSubproyectoLonStr.Should().Be("-73.800000");
        vm.NuevoSubproyectoDescripcion.Should().Be("Desc inicial");

        // 3. Modificar campos y guardar
        vm.NuevoSubproyectoNombre = "Frente Modificado y Renombrado";
        vm.NuevoSubproyectoSubcarpeta = "01_Frente_Actualizado";
        vm.NuevoSubproyectoLatStr = "7.090000";
        vm.NuevoSubproyectoLonStr = "-73.890000";
        vm.NuevoSubproyectoDescripcion = "Desc actualizada";

        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        // No debe duplicar, debe actualizar en sitio
        vm.Subproyectos.Should().HaveCount(1);
        sub.Nombre.Should().Be("Frente Modificado y Renombrado");
        sub.Subcarpeta.Should().Be("01_Frente_Actualizado");
        sub.Lat.Should().BeApproximately(7.09, 0.0001);
        sub.Lon.Should().BeApproximately(-73.89, 0.0001);
        sub.Descripcion.Should().Be("Desc actualizada");
        sub.EsModificado.Should().BeTrue();

        vm.FormularioSubproyectoVisible.Should().BeFalse();
        vm.EsEdicionSubproyecto.Should().BeFalse();
    }

    [Fact]
    public void UbicarSubproyectoEnMapa_ConCoordenadas_DisparaEventoCentrado()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Sitio Fluvial";
        vm.NuevoSubproyectoSubcarpeta = "Sitio1";
        vm.NuevoSubproyectoLatStr = "7.062000";
        vm.NuevoSubproyectoLonStr = "-73.854000";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        var sub = vm.Subproyectos[0];

        double lonCentrada = 0;
        double latCentrada = 0;
        bool eventoDisparado = false;

        vm.CentrarEnSubproyectoSolicitado += (lon, lat) =>
        {
            eventoDisparado = true;
            lonCentrada = lon;
            latCentrada = lat;
        };

        vm.UbicarSubproyectoEnMapaCommand.Execute(sub);

        eventoDisparado.Should().BeTrue();
        lonCentrada.Should().BeApproximately(-73.854, 0.0001);
        latCentrada.Should().BeApproximately(7.062, 0.0001);
    }

    [Fact]
    public void SubproyectosPinsChanged_SeDisparaAlAgregarEditarYQuitar()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        int disparos = 0;
        vm.SubproyectosPinsChanged += (lista) =>
        {
            disparos++;
        };

        // Disparo 1: Al agregar
        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Sub 1";
        vm.NuevoSubproyectoSubcarpeta = "Carpeta1";
        vm.NuevoSubproyectoLatStr = "7.1";
        vm.NuevoSubproyectoLonStr = "-73.8";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);
        disparos.Should().Be(1);

        // Disparo 2: Al editar
        var sub = vm.Subproyectos[0];
        vm.EditarSubproyectoCommand.Execute(sub);
        vm.NuevoSubproyectoNombre = "Sub 1 Mod";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);
        disparos.Should().Be(2);

        // Disparo 3: Al quitar
        vm.QuitarSubproyectoCommand.Execute(sub);
        disparos.Should().Be(3);
    }

    [Fact]
    public async Task GuardarAsync_ModoEdicion_ActualizaSubproyectosExistentesModificados()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();

        var detalleProyecto = new ProyectoDetalleDto(
            Id: 99,
            Titulo: "Proyecto En Edicion",
            Descripcion: "Desc",
            FechaInicio: DateTime.Today,
            PalabraClave: "PC",
            RutaArchivos: @"C:\Proyectos\Test",
            Lon: -73.85,
            Lat: 7.06,
            MunicipioCodigo: "68081",
            MunicipioNombre: "Barrancabermeja");

        var subsExistentes = new List<SubproyectoDto>
        {
            new(201, 99, "Frente Antiguo", "68081", "Barrancabermeja", "01_Antiguo", -73.85, 7.06, "Original")
        };

        mockProy.Setup(r => r.ObtenerSubproyectosAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subsExistentes);

        mockProy.Setup(r => r.ActualizarAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        mockProy.Setup(r => r.ActualizarSubproyectoAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var actProyUseCase = new ActualizarProyectoUseCase(mockProy.Object);
        var guardarSubUseCase = new GuardarSubproyectoUseCase(mockProy.Object);
        var obtenerSubUseCase = new ObtenerSubproyectosUseCase(mockProy.Object);
        var eliminarSubproyectoUseCase = new EliminarSubproyectoUseCase(mockProy.Object);

        var vm = new FormularioProyectoViewModel(
            actProyUseCase,
            mockMuni.Object,
            detalleProyecto,
            navigateBack: () => { },
            onProyectoEditado: null,
            eliminarProyectoUseCase: null,
            notifications: mockNotif.Object,
            guardarSubproyectoUseCase: guardarSubUseCase,
            obtenerSubproyectosUseCase: obtenerSubUseCase,
            eliminarSubproyectoUseCase: eliminarSubproyectoUseCase);

        await vm.CargarDatosInicialesAsync("68081");

        vm.Subproyectos.Should().HaveCount(1);
        var sub = vm.Subproyectos[0];
        sub.IdSubproyecto.Should().Be(201);
        sub.EsNuevo.Should().BeFalse();
        sub.EsModificado.Should().BeFalse();

        // Editar el subproyecto
        vm.EditarSubproyectoCommand.Execute(sub);
        vm.NuevoSubproyectoNombre = "Frente Renombrado y Modificado";
        vm.NuevoSubproyectoSubcarpeta = "01_Nuevo";
        vm.NuevoSubproyectoLatStr = "7.070000";
        vm.NuevoSubproyectoLonStr = "-73.860000";
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        sub.EsModificado.Should().BeTrue();
        sub.EsModificadoExistente.Should().BeTrue();

        // Guardar proyecto
        await vm.GuardarCommand.ExecuteAsync(null);

        mockProy.Verify(r => r.ActualizarSubproyectoAsync(
            201,
            "Frente Renombrado y Modificado",
            "68081",
            "01_Nuevo",
            It.Is<double>(lon => Math.Abs(lon - (-73.86)) < 0.001),
            It.Is<double>(lat => Math.Abs(lat - 7.07) < 0.001),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        mockProy.Verify(r => r.InsertarSubproyectoAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcesarClickMapaAsync_ConFormularioSubproyectoVisible_NoSobrescribeCoordenadasProyectoPadre()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        mockMuni.Setup(m => m.ObtenerPorPuntoAsync(It.IsAny<double>(), It.IsAny<double>()))
            .ReturnsAsync(new MunicipioUbicacionDto("68081", "Barrancabermeja", "68", "Santander", null));

        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.SelectedMunicipio = new FormularioProyectoViewModel.MunicipioItem("68081", "Barrancabermeja");
        vm.SetCoordenadas(7.062000, -73.854000);

        // Abrir formulario de subproyecto y hacer clic en otra ubicación para el subproyecto
        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        var ok = await vm.ProcesarClickMapaAsync(7.110000, -73.920000);

        ok.Should().BeTrue();
        // Las coordenadas del proyecto principal deben permanecer intactas
        vm.LatStr.Should().Be("7.062000");
        vm.LonStr.Should().Be("-73.854000");
        // Y las del subproyecto deben actualizarse al punto clicado
        vm.NuevoSubproyectoLatStr.Should().Be("7.110000");
        vm.NuevoSubproyectoLonStr.Should().Be("-73.920000");
        vm.NuevoSubproyectoMunicipio?.Codigo.Should().Be("68081");
    }

    [Fact]
    public async Task AgregarMunicipioCommand_ActualizaMunicipiosDisponiblesSubproyecto_YGuardaNombreLimpio()
    {
        var (mockProy, mockMuni, mockNotif) = CrearMocks();
        var vm = new CrearProyectoViewModel(
            mockProy.Object,
            mockMuni.Object,
            new ProyectoArchivosService(),
            navigateBack: () => { },
            notifications: mockNotif.Object);

        vm.SelectedDepartamento = new FormularioProyectoViewModel.DepartamentoItem("68", "Santander");
        vm.SelectedMunicipio = new FormularioProyectoViewModel.MunicipioItem("68081", "Barrancabermeja");

        await vm.AgregarMunicipioCommand.ExecuteAsync(null);

        vm.MunicipiosDisponiblesSubproyecto.Should().ContainSingle(m => m.Codigo == "68081");

        vm.MostrarFormularioSubproyectoCommand.Execute(null);
        vm.NuevoSubproyectoNombre = "Frente Barranca";
        vm.NuevoSubproyectoSubcarpeta = "01_Barranca";
        vm.NuevoSubproyectoMunicipio = vm.MunicipiosDisponiblesSubproyecto[0];
        vm.GuardarNuevoSubproyectoCommand.Execute(null);

        vm.Subproyectos.Should().HaveCount(1);
        // Debe guardar solo el nombre del municipio sin "(Santander)" concatenado
        vm.Subproyectos[0].MunicipioNombre.Should().Be("Barrancabermeja");
    }
}
