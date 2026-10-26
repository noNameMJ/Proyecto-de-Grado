using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class IndiceArchivosProyectoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ProyectoArchivosService _service;

    public IndiceArchivosProyectoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_Indice_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new ProyectoArchivosService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void Construir_OmiteContenidoInternoDeGdb_PeroRegistraContenedorGdb()
    {
        // Arrange
        var gdbDir = Path.Combine(_tempDir, "Datos_Espaciales", "Catastro.gdb");
        Directory.CreateDirectory(gdbDir);
        File.WriteAllText(Path.Combine(gdbDir, "a00000001.gdbtable"), "tabla binaria 1");
        File.WriteAllText(Path.Combine(gdbDir, "a00000001.gdbx"), "indice binario 1");
        File.WriteAllText(Path.Combine(gdbDir, "timestamps"), "timestamps");

        var lidarDir = Path.Combine(_tempDir, "Datos_Espaciales", "Nubes");
        Directory.CreateDirectory(lidarDir);
        File.WriteAllText(Path.Combine(lidarDir, "vuelo_01.las"), "las data 1");
        File.WriteAllText(Path.Combine(lidarDir, "vuelo_02.laz"), "laz data 2");
        File.WriteAllText(Path.Combine(_tempDir, ".probe_test.tmp"), "sonda temporal");

        // Act
        var indice = IndiceArchivosProyecto.Construir(_tempDir, gradoParalelismo: 4);

        // Assert
        // Solo debe indexar vuelo_01.las, vuelo_02.laz y Catastro.gdb (3 entradas), omitiendo .gdbtable/.gdbx y .probe_
        indice.TotalArchivos.Should().Be(3);
        indice.ContarCoincidencias(new[] { ".gdbtable", ".gdbx" }).Should().Be(0);
        indice.ContarCoincidencias(new[] { ".gdb" }).Should().Be(1);

        var gdbs = indice.Filtrar(new[] { ".gdb" });
        gdbs.Should().ContainSingle();
        gdbs[0].Nombre.Should().Be("Catastro.gdb");
        gdbs[0].UbicacionRelativa.Should().Be("Datos_Espaciales");
        gdbs[0].RutaRelativaVirtual.Should().Be("Datos_Espaciales/Catastro.gdb");
    }

    [Fact]
    public void Filtrar_Y_ContarCoincidencias_PorExtensionYTexto_RespetaMaxResultados()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "LiDAR_Embarcaderos");
        Directory.CreateDirectory(subDir);

        for (int i = 1; i <= 15; i++)
        {
            string nombre = i <= 10 ? $"tramo_norte_{i:D2}.las" : $"tramo_sur_{i:D2}.las";
            File.WriteAllText(Path.Combine(subDir, nombre), $"contenido {i}");
        }
        File.WriteAllText(Path.Combine(subDir, "ortofoto_norte.tif"), "tif");

        var indice = IndiceArchivosProyecto.Construir(_tempDir, gradoParalelismo: 4);

        // Act & Assert: Total de .las sin filtro de texto = 15, pero truncado a 5 visibles
        indice.ContarCoincidencias(new[] { ".las" }).Should().Be(15);
        var primerosCinco = indice.Filtrar(new[] { ".las" }, texto: null, maxResultados: 5);
        primerosCinco.Should().HaveCount(5);

        // Act & Assert: Filtro por texto "norte" dentro de .las = 10
        indice.ContarCoincidencias(new[] { ".las" }, "norte").Should().Be(10);
        var norteTruncado = indice.Filtrar(new[] { ".las" }, "norte", maxResultados: 4);
        norteTruncado.Should().HaveCount(4);
        norteTruncado.Should().OnlyContain(a => a.Nombre.Contains("norte", StringComparison.OrdinalIgnoreCase));

        // Act & Assert: Filtro por carpeta relativa "LiDAR_Embarcaderos" y múltiples extensiones
        indice.ContarCoincidencias(new[] { ".las", ".tif" }, "LiDAR_Embarcaderos").Should().Be(16);
    }

    [Fact]
    public async Task ProyectoArchivosService_CacheaIndice_E_InvalidaCorrectamente()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "inicial.shp"), "shp1");

        // Act 1: Construir y obtener índice
        _service.TryObtenerIndice(_tempDir, out _).Should().BeFalse();
        var indice1 = await _service.ObtenerIndiceAsync(_tempDir);
        _service.TryObtenerIndice(_tempDir, out var enCache).Should().BeTrue();
        enCache.Should().BeSameAs(indice1);
        indice1.ContarCoincidencias(new[] { ".shp" }).Should().Be(1);

        // Agregar un segundo archivo en disco sin invalidar aún
        File.WriteAllText(Path.Combine(_tempDir, "nuevo.shp"), "shp2");
        var indiceCacheado = await _service.ObtenerIndiceAsync(_tempDir);
        indiceCacheado.ContarCoincidencias(new[] { ".shp" }).Should().Be(1);

        // Act 2: Invalidar índice y volver a consultar
        _service.InvalidarIndice(_tempDir);
        _service.TryObtenerIndice(_tempDir, out _).Should().BeFalse();

        var indice2 = await _service.ObtenerIndiceAsync(_tempDir);
        indice2.ContarCoincidencias(new[] { ".shp" }).Should().Be(2);
    }

    [Fact]
    public void ArchivosViewModel_ConFiltroExtensionYBusquedaTexto_ReutilizaIndiceSinReescanearDisco()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Nubes_LAS");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "embarcadero_rio_magdalena.las"), "las1");
        File.WriteAllText(Path.Combine(subDir, "embarcadero_barrancabermeja.las"), "las2");
        File.WriteAllText(Path.Combine(subDir, "puente_sogamoso.las"), "las3");

        var repoMock = new Mock<IProyectoRepository>();
        var filtros = new FiltrosViewModel(null);
        var dto = new ProyectoDetalleDto(1, "EMBARCADEROS", null, null, null, _tempDir, -73.8, 7.0, "68081", "Barrancabermeja");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".las", "LiDAR / Nubes", "☁️", "#F3E5F5", "#E1BEE7", "#6A1B9A", 3, 300));

        var vm = new ArchivosViewModel(filtros, _service, repoMock.Object)
        {
            IndexarEnSegundoPlano = false,
            ProyectoDetalle = ficha
        };

        // Act 1: Activar filtro .las (construye el índice por primera vez)
        vm.FiltrarPorExtension(".las");
        vm.Items.Should().HaveCount(3);
        _service.TryObtenerIndice(_tempDir, out var indiceInicial).Should().BeTrue();

        // Act 2: Escribir en el buscador "embarcadero" (filtra sobre el mismo índice en memoria)
        vm.BusquedaTexto = "embarcadero";
        vm.Items.Should().HaveCount(2);
        _service.TryObtenerIndice(_tempDir, out var indiceDespues).Should().BeTrue();
        indiceDespues.Should().BeSameAs(indiceInicial);
    }

    [Fact]
    public void IndiceArchivosProyecto_BuscarPorTexto_BuscaGlobalmenteEnTodoElArbol()
    {
        // Arrange
        var subDir1 = Path.Combine(_tempDir, "Datos_Espaciales", "Vectores");
        var subDir2 = Path.Combine(_tempDir, "Documentos");
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        File.WriteAllText(Path.Combine(subDir1, "curvas_nivel.shp"), "shp");
        File.WriteAllText(Path.Combine(subDir1, "vias_principales.shp"), "shp");
        File.WriteAllText(Path.Combine(subDir2, "informe_curvas.pdf"), "pdf");
        File.WriteAllText(Path.Combine(_tempDir, "curvas_resumen.txt"), "txt");

        var indice = IndiceArchivosProyecto.Construir(_tempDir, gradoParalelismo: 2);

        // Act & Assert
        indice.ContarCoincidenciasPorTexto("curvas").Should().Be(3);
        var resultados = indice.BuscarPorTexto("curvas");
        resultados.Should().HaveCount(3);
        resultados.Should().Contain(r => r.Nombre == "curvas_nivel.shp");
        resultados.Should().Contain(r => r.Nombre == "informe_curvas.pdf");
        resultados.Should().Contain(r => r.Nombre == "curvas_resumen.txt");

        // Rutas físicas
        var rutasShp = indice.ObtenerRutasFisicasPorExtension(".shp").ToList();
        rutasShp.Should().HaveCount(2);
        rutasShp.Should().Contain(p => p.EndsWith("curvas_nivel.shp"));
        rutasShp.Should().Contain(p => p.EndsWith("vias_principales.shp"));
    }
}

