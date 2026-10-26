using Esri.ArcGISRuntime.Geometry;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Infrastructure.Gis.Services;

namespace Geomatica.UnitTests.ViewModels;

public class SubproyectosViewModelTests
{
    private static ProyectoDetalleDto CrearProyectoEjemplo(int id = 10)
    {
        return new ProyectoDetalleDto(
            Id: id,
            Titulo: "Proyecto Gran Magdalena Embarcaderos",
            Descripcion: "Construcción y adecuación de muelles y embarcaderos fluviales",
            FechaInicio: new DateTime(2023, 1, 15),
            PalabraClave: "embarcaderos, fluvial, magdalena",
            RutaArchivos: @"C:\Proyectos\Embarcaderos",
            Lon: -73.85,
            Lat: 7.06,
            MunicipioCodigo: "68081",
            MunicipioNombre: "Barrancabermeja");
    }

    [Fact]
    public async Task FichaProyectoViewModel_CargarSubproyectos_PopulaColeccionYPropiedades()
    {
        var mockRepo = new Mock<IProyectoRepository>();
        var items = new List<SubproyectoDto>
        {
            new(1, 10, "Embarcadero Muelle Principal", "68081", "Barrancabermeja", "Muelle_Principal", -73.854, 7.062),
            new(2, 10, "Embarcadero Yondó Río", "05893", "Yondó", "Yondo_Rio", -73.910, 7.085)
        };
        mockRepo.Setup(r => r.ObtenerSubproyectosAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        var useCase = new ObtenerSubproyectosUseCase(mockRepo.Object);
        var proyecto = CrearProyectoEjemplo(10);

        var vm = new FichaProyectoViewModel(
            proyecto,
            volverAction: () => { },
            obtenerSubproyectosUseCase: useCase);

        await vm.CargarSubproyectosAsync();

        vm.HasSubproyectos.Should().BeTrue();
        vm.TotalSubproyectos.Should().Be(2);
        vm.Subproyectos.Should().HaveCount(2);
        vm.Subproyectos[0].Nombre.Should().Be("Embarcadero Muelle Principal");
        vm.Subproyectos[0].Subcarpeta.Should().Be("Muelle_Principal");
        vm.Subproyectos[1].Nombre.Should().Be("Embarcadero Yondó Río");
    }

    [Fact]
    public void FichaProyectoViewModel_ExplorarArchivosSubproyecto_DisparaEventoConRutaCorrecta()
    {
        var proyecto = CrearProyectoEjemplo(10);
        var vm = new FichaProyectoViewModel(proyecto, volverAction: () => { });

        var sub = new SubproyectoDto(1, 10, "Embarcadero Norte", "68081", "Barrancabermeja", "Norte/Muelle1", -73.85, 7.06);

        string? rutaDisparada = null;
        vm.NavegacionSubcarpetaSolicitada += (s, ruta) => rutaDisparada = ruta;

        vm.ExplorarArchivosSubproyectoCommand.Execute(sub);

        rutaDisparada.Should().Be("Norte/Muelle1");
    }

    [Fact]
    public void FichaProyectoViewModel_UbicarEnMapaSubproyecto_DisparaEventoConCoordenadas()
    {
        var proyecto = CrearProyectoEjemplo(10);
        var vm = new FichaProyectoViewModel(proyecto, volverAction: () => { });

        var sub = new SubproyectoDto(1, 10, "Embarcadero Norte", "68081", "Barrancabermeja", "Norte/Muelle1", -73.85432, 7.06123);

        (double lon, double lat, string nombre)? coordsRecibidas = null;
        vm.UbicarEnMapaSubproyectoSolicitado += (s, coords) => coordsRecibidas = coords;

        vm.UbicarEnMapaSubproyectoCommand.Execute(sub);

        coordsRecibidas.Should().NotBeNull();
        coordsRecibidas!.Value.lon.Should().Be(-73.85432);
        coordsRecibidas!.Value.lat.Should().Be(7.06123);
        coordsRecibidas!.Value.nombre.Should().Be("Embarcadero Norte");
    }

    [Fact]
    public void ArchivosViewModel_NavegarASubcarpeta_ActualizaRutaActual()
    {
        var filtrosVm = new FiltrosViewModel(null);
        var archivosService = new ProyectoArchivosService();
        var vm = new ArchivosViewModel(filtrosVm, archivosService, Mock.Of<IProyectoRepository>())
        {
            IndexarEnSegundoPlano = false
        };

        vm.NavegarASubcarpeta(@"/Barrancabermeja\Muelle_Sur/");

        vm.RutaActual.Should().Be("Barrancabermeja/Muelle_Sur");
    }

    [Fact]
    public void ProyectoStylingHelper_CrearSubproyectoGraphics_GeneraMarcadorYEtiquetaConAtributos()
    {
        var pt = new MapPoint(-73.85, 7.06, SpatialReferences.Wgs84);

        var graphics = ProyectoStylingHelper.CrearSubproyectoGraphics(
            pt,
            idSubproyecto: 5,
            nombre: "Embarcadero El Llanito",
            subcarpeta: "El_Llanito/Muelle",
            municipio: "Barrancabermeja",
            descripcion: "Sitio pesquero fluvial",
            idProyecto: 10);

        graphics.Should().HaveCount(2);

        var marker = graphics[0];
        marker.Geometry.Should().Be(pt);
        marker.Attributes["id_subproyecto"].Should().Be(5);
        marker.Attributes["id_proyecto"].Should().Be(10);
        marker.Attributes["nombre"].Should().Be("Embarcadero El Llanito");
        marker.Attributes["subcarpeta"].Should().Be("El_Llanito/Muelle");
        marker.Attributes["municipio"].Should().Be("Barrancabermeja");
        marker.Attributes["tipo"].Should().Be("subproyecto");

        var label = graphics[1];
        label.Symbol.Should().BeOfType<Esri.ArcGISRuntime.Symbology.TextSymbol>();
        var textSym = (Esri.ArcGISRuntime.Symbology.TextSymbol)label.Symbol;
        textSym.Text.Should().Contain("Embarcadero El Llanito");
    }

    [Fact]
    public void SubproyectoDto_PropiedadesCalculadas_FormateanCorrectamente()
    {
        var conMpio = new SubproyectoDto(1, 10, "Embarcadero Central", "68081", "Barrancabermeja", "Central", -73.85432, 7.06123);
        conMpio.CoordenadasTexto.Should().Be("7.06123° N, -73.85432° W");
        conMpio.UbicacionMunicipioTexto.Should().Be("Barrancabermeja (68081)");

        var sinCodigo = new SubproyectoDto(2, 10, "Embarcadero Fluvial", null, "Yondó", "Yondo", -73.9, 7.1);
        sinCodigo.UbicacionMunicipioTexto.Should().Be("Yondó");

        var sinMpio = new SubproyectoDto(3, 10, "Embarcadero Ribera", null, null, "Ribera", -73.9, 7.1);
        sinMpio.UbicacionMunicipioTexto.Should().Be("Sin municipio asignado");
    }
}
