using System;
using System.Collections.Generic;
using System.Threading;
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

public class MapaViewModelCapasTests
{
    [Fact]
    public async Task ActualizarCapasConFiltroAsync_ConProyectos_CargaCapaProyectosYConsultaMunicipiosBajoDemanda()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        mockProyRepo.Setup(r => r.ObtenerCodigosMunicipioAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<string> { "68001" });

        mockMuniRepo.Setup(r => r.PorCodigosGeoJsonAsync(It.Is<IReadOnlyList<string>>(l => l.Contains("68001"))))
            .ReturnsAsync(new List<MunicipioGeoJsonDto>
            {
                new("68001", "Bucaramanga", null)
            });

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var proyectos = new List<ProyectoGeomatico>
        {
            new()
            {
                Id = 1,
                Titulo = "Proyecto Bucaramanga",
                Longitud = -73.12,
                Latitud = 7.12
            }
        };

        // Act
        await mapaVm.ActualizarCapasConFiltroAsync(proyectos);

        // Assert
        mapaVm.LayerProyectos.Should().NotBeNull();
        mapaVm.LayerProyectos!.Name.Should().Be("Proyectos");

        // Verifica que se consultaron los municipios correspondientes bajo demanda
        mockProyRepo.Verify(r => r.ObtenerCodigosMunicipioAsync(It.IsAny<IReadOnlyList<int>>()), Times.Once);
        mockMuniRepo.Verify(r => r.PorCodigosGeoJsonAsync(It.Is<IReadOnlyList<string>>(l => l.Contains("68001"))), Times.Once);

        // No se debe llamar a ObtenerTodosCodigosMunicipioAsync en el ciclo crítico de visualización
        mockProyRepo.Verify(r => r.ObtenerTodosCodigosMunicipioAsync(), Times.Never);
    }

    [Fact]
    public void ActualizarResultadosFiltros_PueblaColeccionResultadosListaYResumenDeInmediato()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var proyectos = new List<ProyectoGeomatico>
        {
            new()
            {
                Id = 10,
                Titulo = "Proyecto NorOriente",
                Longitud = -73.0,
                Latitud = 7.0,
                RutaArchivos = "D:\\Proyectos\\NorOriente"
            }
        };

        // Act
        mapaVm.ActualizarResultadosFiltros(proyectos);

        // Assert
        filtrosVm.ResultadosLista.Should().HaveCount(1);
        filtrosVm.ResultadosResumen.Should().Contain("1 proyecto encontrado");
        filtrosVm.HasResultados.Should().BeTrue();
        filtrosVm.NoHayResultados.Should().BeFalse();
        filtrosVm.IsBuscando.Should().BeFalse();

        var item = filtrosVm.ResultadosLista[0] as FiltrosViewModel.ProyectoItem;
        item.Should().NotBeNull();
        item!.Id.Should().Be(10);
        item.Titulo.Should().Be("Proyecto NorOriente");
    }

    [Fact]
    public void ActualizarResultadosFiltros_PreservaSubproyectosYEstadoExpandido()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var proyectos = new List<ProyectoGeomatico>
        {
            new()
            {
                Id = 10,
                Titulo = "Proyecto NorOriente",
                Longitud = -73.0,
                Latitud = 7.0,
                RutaArchivos = "D:\\Proyectos\\NorOriente"
            }
        };

        var subs = new Dictionary<int, IReadOnlyList<SubproyectoDto>>
        {
            [10] = new List<SubproyectoDto>
            {
                new(1, 10, "Frente Norte", "68001", "Bucaramanga", "norte", -73.1, 7.1)
            }
        };

        // 1. Cargar con subproyectos
        mapaVm.ActualizarResultadosFiltros(proyectos, subs);
        var item = filtrosVm.ResultadosLista[0] as FiltrosViewModel.ProyectoItem;
        item.Should().NotBeNull();
        item!.HasSubproyectos.Should().BeTrue();
        item.EstaExpandido = true; // El usuario abre el menú desplegable

        // 2. Simular un refresco posterior sin pasar subproyectos (sobrecarga de 1 parámetro)
        mapaVm.ActualizarResultadosFiltros(proyectos);

        // Assert: No debe desaparecer el menú desplegable ni perderse la expansión
        var itemRefrescado = filtrosVm.ResultadosLista[0] as FiltrosViewModel.ProyectoItem;
        itemRefrescado.Should().NotBeNull();
        itemRefrescado!.HasSubproyectos.Should().BeTrue();
        itemRefrescado.Subproyectos.Should().HaveCount(1);
        itemRefrescado.EstaExpandido.Should().BeTrue();
    }

    [Fact]
    public void ActualizarResultadosFiltros_ConDiccionarioVacio_LimpiaSubproyectosAntiguosYOverlay()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var proyectos = new List<ProyectoGeomatico>
        {
            new() { Id = 10, Titulo = "Proyecto 10", Longitud = -73.0, Latitud = 7.0 }
        };

        var subsIniciales = new Dictionary<int, IReadOnlyList<SubproyectoDto>>
        {
            [10] = new List<SubproyectoDto>
            {
                new(1, 10, "Frente 1", "68001", "Bucaramanga", "frente1", -73.1, 7.1)
            }
        };

        mapaVm.ActualizarResultadosFiltros(proyectos, subsIniciales);
        mapaVm.OverlaySubproyectos.Graphics.Should().HaveCount(2);

        // Cuando todos los subproyectos fueron eliminados (diccionario vacío explícito)
        var subsVacios = new Dictionary<int, IReadOnlyList<SubproyectoDto>>();
        mapaVm.ActualizarResultadosFiltros(proyectos, subsVacios);

        var item = filtrosVm.ResultadosLista[0] as FiltrosViewModel.ProyectoItem;
        item!.HasSubproyectos.Should().BeFalse();
        mapaVm.OverlaySubproyectos.Graphics.Should().BeEmpty();
    }

    [Fact]
    public async Task AbrirArchivosSubproyectoCommand_DesdeBarraFiltros_AbreFichaProyectoPadreYNavegaASubcarpeta()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var detalleProy10 = new ProyectoDetalleDto(
            Id: 10,
            Titulo: "Proyecto Magdalena",
            Descripcion: "Desc",
            FechaInicio: DateTime.Today,
            PalabraClave: "magdalena",
            RutaArchivos: @"C:\Proyectos\Magdalena",
            Lon: -73.85,
            Lat: 7.06,
            MunicipioCodigo: "68081",
            MunicipioNombre: "Barrancabermeja");

        mockProyRepo.Setup(r => r.ObtenerPorIdAsync(10))
            .ReturnsAsync(detalleProy10);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>())
        {
            IndexarEnSegundoPlano = false
        };

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var mainVm = new MainViewModel(
            filtrosVm,
            Mock.Of<INotificationService>(),
            new EliminarProyectoUseCase(mockProyRepo.Object),
            new ProyectoArchivosService(),
            () => mapaVm,
            () => archivosVm,
            (back, created) => null!,
            (proy, back, edited) => null!,
            mockProyRepo.Object);

        mainVm.ShowMapaCommand.Execute(null);

        var sub = new SubproyectoDto(5, 10, "Embarcadero Norte", "68081", "Barrancabermeja", @"Frentes\Norte", -73.85, 7.06);

        await mapaVm.NavegarASubproyectoArchivosAsync(sub);

        archivosVm.ProyectoDetalle.Should().NotBeNull();
        archivosVm.ProyectoDetalle!.Id.Should().Be(10);
        archivosVm.RutaRaizProyecto.Should().Be(@"C:\Proyectos\Magdalena");
        archivosVm.RutaActual.Should().Be("Frentes/Norte");
        mainVm.CurrentView.Should().BeSameAs(mapaVm);
    }

    [Fact]
    public void LimpiarSubproyectos_DespuesDeFiltrarEnFicha_RestauraTodosLosSubproyectosEnMapa()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var globales = new List<SubproyectoDto>
        {
            new(1, 10, "Sub Proy 10", "68081", "Barranca", "s1", -73.8, 7.0),
            new(2, 20, "Sub Proy 20", "05893", "Yondó", "s2", -73.9, 7.1)
        };

        // 1. Mostrar lista global (2 subproyectos = 4 graphics: marcador + etiqueta cada uno)
        mapaVm.MostrarSubproyectosEnMapa(globales, actualizarListaGlobal: true);
        mapaVm.OverlaySubproyectos.Graphics.Should().HaveCount(4);

        // 2. Abrir ficha del proyecto 10 (filtra visualmente a 1 subproyecto sin pisar lista global)
        mapaVm.MostrarSubproyectosEnMapa(new[] { globales[0] }, actualizarListaGlobal: false);
        mapaVm.OverlaySubproyectos.Graphics.Should().HaveCount(2);

        // 3. Cerrar ficha -> LimpiarSubproyectos debe restaurar los 2 subproyectos globales
        mapaVm.LimpiarSubproyectos();
        mapaVm.OverlaySubproyectos.Graphics.Should().HaveCount(4);
    }

    [Fact]
    public async Task ActualizarCapaSubproyectosAsync_ConSubproyectos_CreaCapaOperacionalVisibleEnLeyenda()
    {
        var mockProyRepo = new Mock<IProyectoRepository>();
        var mockMuniRepo = new Mock<IMunicipioRepository>();
        var buscarUseCase = new BuscarProyectosUseCase(mockProyRepo.Object);

        var filtrosVm = new FiltrosViewModel(null);
        var archivosVm = new ArchivosViewModel(
            filtrosVm,
            new ProyectoArchivosService(),
            mockProyRepo.Object,
            Mock.Of<INotificationService>());

        var mapaVm = new MapaViewModel(
            buscarUseCase,
            mockProyRepo.Object,
            mockMuniRepo.Object,
            filtrosVm,
            archivosVm,
            Mock.Of<INotificationService>());

        var subproyectos = new List<SubproyectoDto>
        {
            new(1, 10, "Embarcadero Norte", "68081", "Barrancabermeja", "norte", -73.85, 7.06),
            new(2, 10, "Embarcadero Sur", "68081", "Barrancabermeja", "sur", -73.86, 7.05)
        };

        // Act
        await mapaVm.ActualizarCapaSubproyectosAsync(subproyectos);

        // Assert
        mapaVm.LayerSubproyectos.Should().NotBeNull();
        mapaVm.LayerSubproyectos!.Name.Should().Be("Subproyectos");
        mapaVm.Map!.OperationalLayers.Should().Contain(mapaVm.LayerSubproyectos!);

        // InvalidarCache debe limpiar la capa operacional y resetear la propiedad
        mapaVm.InvalidarCache();
        mapaVm.LayerSubproyectos.Should().BeNull();
        mapaVm.Map!.OperationalLayers.Should().NotContain(x => x.Name == "Subproyectos");
    }
}
