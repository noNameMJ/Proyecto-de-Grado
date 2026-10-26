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

namespace Geomatica.UnitTests.ViewModels;

public class ArchivosViewModelFiltroExtensionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly FiltrosViewModel _filtros;
    private readonly ProyectoArchivosService _service;

    public ArchivosViewModelFiltroExtensionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_ArchivosVM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _repoMock = new Mock<IProyectoRepository>();
        _filtros = new FiltrosViewModel(null);
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
    public void ActualizarExtensionesDisponibles_DebeIncluirTodosYFormatosDetectados()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, null, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });

        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".tif", "Raster", "🛰️", "#E3F2FD", "#BBDEFB", "#1565C0", 5, 1024));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".shp", "Vectorial", "🗺️", "#E8F4EC", "#B2DFBF", "#1A5C34", 2, 512));

        // Act
        vm.ActualizarExtensionesDisponibles(ficha);

        // Assert
        vm.ExtensionesDisponibles.Should().HaveCount(3);
        vm.ExtensionesDisponibles[0].Extension.Should().BeEmpty();
        vm.ExtensionesDisponibles[0].Etiqueta.Should().Be("Todos los archivos");
        vm.ExtensionesDisponibles.Should().Contain(e => e.Extension == ".tif" && e.Cantidad == 5);
        vm.ExtensionesDisponibles.Should().Contain(e => e.Extension == ".shp" && e.Cantidad == 2);
    }

    [Fact]
    public void FiltrarPorExtension_Y_LimpiarFiltro_ModificanSeleccionCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, null, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });

        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".tif", "Raster", "🛰️", "#E3F2FD", "#BBDEFB", "#1565C0", 10, 2048));
        vm.ActualizarExtensionesDisponibles(ficha);

        // Act 1: Filtrar por .tif
        vm.FiltrarPorExtension(".tif");

        // Assert 1
        vm.FiltroExtensionSeleccionado.Should().NotBeNull();
        vm.FiltroExtensionSeleccionado!.Extension.Should().Be(".tif");

        // Act 2: Limpiar filtro
        vm.LimpiarFiltroExtensionCommand.Execute(null);

        // Assert 2
        vm.FiltroExtensionSeleccionado.Should().NotBeNull();
        vm.FiltroExtensionSeleccionado!.Extension.Should().BeEmpty();
    }

    [Fact]
    public void RefrescarSegunFiltros_ConFiltroExtension_ListaArchivosRecursivamenteConUbicacion()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Datos_Espaciales", "Ortofotos");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(subDir, "mosaico1.tif"), "tif1");
        File.WriteAllText(Path.Combine(_tempDir, "base.tif"), "tif2");
        File.WriteAllText(Path.Combine(_tempDir, "documento.pdf"), "pdf1");

        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);

        vm.ProyectoDetalle = ficha;

        // Act: Seleccionar filtro .tif
        vm.FiltrarPorExtension(".tif");

        // Assert
        vm.IsFiltradoPorExtension.Should().BeTrue();
        vm.Items.Should().HaveCount(2);
        vm.BannerFiltroTexto.Should().Contain("2");
        vm.BannerFiltroTexto.Should().Contain(".TIF");

        var itemsArchivos = vm.Items.OfType<Geomatica.Desktop.Models.ArchivoVirtual>().ToList();
        itemsArchivos.Should().HaveCount(2);
        itemsArchivos.Should().OnlyContain(a => a.Extension.Equals(".tif", StringComparison.OrdinalIgnoreCase));
        itemsArchivos.Should().Contain(a => a.Nombre == "mosaico1.tif" && a.UbicacionRelativa == "Datos_Espaciales/Ortofotos");
        itemsArchivos.Should().Contain(a => a.Nombre == "base.tif" && a.UbicacionRelativa == "(raíz)");
    }

    [Fact]
    public void LimpiarBusqueda_DebeRestablecerBusquedaTextoYHasBusquedaTexto()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        vm.BusquedaTexto = "ortofoto";

        // Assert intermedio
        vm.HasBusquedaTexto.Should().BeTrue();

        // Act
        vm.LimpiarBusquedaCommand.Execute(null);

        // Assert final
        vm.BusquedaTexto.Should().BeEmpty();
        vm.HasBusquedaTexto.Should().BeFalse();
    }

    [Fact]
    public void RefrescarSegunFiltros_ConMultiplesExtensiones_ListaArchivosDeAmbosTipos()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Capas_Vectoriales");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(subDir, "predios.shp"), "shp");
        File.WriteAllText(Path.Combine(subDir, "vias.gpkg"), "gpkg");
        File.WriteAllText(Path.Combine(_tempDir, "ortofoto.tif"), "tif");
        File.WriteAllText(Path.Combine(_tempDir, "informe.pdf"), "pdf");

        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".shp", "Vectorial", "🗺️", "#E8F4EC", "#B2DFBF", "#1A5C34", 1, 100));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".gpkg", "Vectorial", "🗺️", "#E8F4EC", "#B2DFBF", "#1A5C34", 1, 100));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".tif", "Raster", "🛰️", "#E3F2FD", "#BBDEFB", "#1565C0", 1, 100));

        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        vm.ProyectoDetalle = ficha;

        // Act: Seleccionar simultáneamente .shp y .gpkg
        var optShp = vm.ExtensionesDisponibles.First(e => e.Extension == ".shp");
        var optGpkg = vm.ExtensionesDisponibles.First(e => e.Extension == ".gpkg");
        optShp.IsSelected = true;
        optGpkg.IsSelected = true;

        // Assert
        vm.IsFiltradoPorExtension.Should().BeTrue();
        vm.HayExtensionesSeleccionadas.Should().BeTrue();
        vm.BannerFiltroTexto.Should().Contain("formatos");
        vm.BannerFiltroTexto.Should().Contain(".SHP");
        vm.BannerFiltroTexto.Should().Contain(".GPKG");

        var itemsArchivos = vm.Items.OfType<Geomatica.Desktop.Models.ArchivoVirtual>().ToList();
        itemsArchivos.Should().HaveCount(2);
        itemsArchivos.Should().Contain(a => a.Nombre == "predios.shp");
        itemsArchivos.Should().Contain(a => a.Nombre == "vias.gpkg");
        itemsArchivos.Should().NotContain(a => a.Nombre == "ortofoto.tif");
    }

    [Fact]
    public void FiltrarPorCategoria_Vectorial_SeleccionaFormatosVectoriales()
    {
        // Arrange
        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".shp", "Vectorial", "🗺️", "#E8F4EC", "#B2DFBF", "#1A5C34", 1, 100));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".gpkg", "Vectorial", "🗺️", "#E8F4EC", "#B2DFBF", "#1A5C34", 1, 100));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".tif", "Raster", "🛰️", "#E3F2FD", "#BBDEFB", "#1565C0", 1, 100));

        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        vm.ProyectoDetalle = ficha;

        // Act
        vm.FiltrarPorCategoriaCommand.Execute("Vectorial");

        // Assert
        var optShp = vm.ExtensionesDisponibles.First(e => e.Extension == ".shp");
        var optGpkg = vm.ExtensionesDisponibles.First(e => e.Extension == ".gpkg");
        var optTif = vm.ExtensionesDisponibles.First(e => e.Extension == ".tif");

        optShp.IsSelected.Should().BeTrue();
        optGpkg.IsSelected.Should().BeTrue();
        optTif.IsSelected.Should().BeFalse();
        vm.IsFiltradoPorExtension.Should().BeTrue();
    }

    [Fact]
    public void IrAUbicacionEnAppCommand_DesdeArchivoFiltrado_NavegaACarpetaYSeleccionaElemento()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Datos_Espaciales", "Ortofotos");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(subDir, "mosaico1.tif"), "tif1");
        File.WriteAllText(Path.Combine(_tempDir, "base.tif"), "tif2");

        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        vm.ProyectoDetalle = ficha;

        // Filtrar por .tif
        vm.FiltrarPorExtension(".tif");
        vm.IsFiltradoPorExtension.Should().BeTrue();

        var archivoTarget = vm.Items.OfType<Geomatica.Desktop.Models.ArchivoVirtual>().First(a => a.Nombre == "mosaico1.tif");
        vm.SelectedEntry = archivoTarget;

        // Assert inicial
        vm.CanIrAUbicacion.Should().BeTrue();

        // Act: Navegar a la ubicación dentro de la app
        vm.IrAUbicacionEnAppCommand.Execute(null);

        // Assert final
        vm.IsFiltradoPorExtension.Should().BeFalse();
        vm.RutaActual.Should().Be("Datos_Espaciales/Ortofotos");
        vm.SelectedEntry.Should().NotBeNull();
        ((Geomatica.Desktop.Models.ArchivoVirtual)vm.SelectedEntry!).Nombre.Should().Be("mosaico1.tif");
    }

    [Fact]
    public void Ficha_ExtensionesActualizadas_SincronizaExtensionesDisponiblesEnArchivosViewModel()
    {
        // Arrange
        var dto = new ProyectoDetalleDto(1, "Proyecto 1", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        vm.ProyectoDetalle = ficha;

        // Estado inicial
        vm.ExtensionesDisponibles.Should().HaveCount(1); // Solo "Todos los archivos"

        // Act: La ficha agrega extensiones detectadas durante o al finalizar el escaneo
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".las", "LiDAR", "☁️", "#F3E5F5", "#E1BEE7", "#6A1B9A", 5, 5000));
        ficha.ExtensionesDetectadas.Add(new FormatoExtensionItem(".dwg", "CAD", "📐", "#FFF8E1", "#FFE082", "#B78103", 2, 2000));
        ficha.NotificarExtensionesActualizadas();

        // Assert
        vm.ExtensionesDisponibles.Should().Contain(e => e.Extension == ".las" && e.Categoria == "LiDAR");
        vm.ExtensionesDisponibles.Should().Contain(e => e.Extension == ".dwg" && e.Categoria == "CAD");
        vm.HasExtensionesFiltrables.Should().BeTrue();
    }

    [Fact]
    public async Task BusquedaTexto_SinFiltroExtension_ConIndiceConstruido_BuscaGlobalmenteEnTodoElProyecto()
    {
        // Arrange
        var subDir1 = Path.Combine(_tempDir, "Datos_Espaciales", "LiDAR");
        var subDir2 = Path.Combine(_tempDir, "Documentos", "Informes");
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        File.WriteAllText(Path.Combine(subDir1, "curvas_magdalena.las"), "las");
        File.WriteAllText(Path.Combine(subDir2, "memoria_curvas.pdf"), "pdf");
        File.WriteAllText(Path.Combine(_tempDir, "ortofoto_general.tif"), "tif");

        // Pre-construir el índice del proyecto en caché
        await _service.ObtenerIndiceAsync(_tempDir);

        var dto = new ProyectoDetalleDto(1, "Proyecto Búsqueda Global", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object)
        {
            IndexarEnSegundoPlano = false,
            ProyectoDetalle = ficha
        };

        // Act: Escribir en BusquedaTexto sin ningún filtro de extensión activo
        vm.BusquedaTexto = "curvas";

        // Assert: Debe encontrar ambos archivos ubicados en distintas subcarpetas profundas
        vm.IsFiltradoPorExtension.Should().BeFalse();
        vm.BannerFiltroTexto.Should().Contain("curvas");
        vm.Items.Should().HaveCount(2);
        var nombres = vm.Items.OfType<Geomatica.Desktop.Models.ArchivoVirtual>().Select(a => a.Nombre).ToList();
        nombres.Should().Contain("curvas_magdalena.las");
        nombres.Should().Contain("memoria_curvas.pdf");
    }

    [Fact]
    public async Task MoverElementoAFolder_MueveArchivo_InvalidaIndiceYRegistraAuditoriaConRutaCompleta()
    {
        // Arrange
        var dirOrigen = Path.Combine(_tempDir, "Otros");
        var dirDestino = Path.Combine(_tempDir, "Datos_Espaciales", "Subcarpeta");
        Directory.CreateDirectory(dirOrigen);
        Directory.CreateDirectory(dirDestino);

        string archivoFisico = Path.Combine(dirOrigen, "levantamiento.shp");
        File.WriteAllText(archivoFisico, "shp content");

        // Pre-cargar índice para comprobar que MoverElementoAFolder lo invalida
        await _service.ObtenerIndiceAsync(_tempDir);
        _service.TryObtenerIndice(_tempDir, out _).Should().BeTrue();

        var dto = new ProyectoDetalleDto(77, "Proyecto Trazabilidad", null, null, null, _tempDir, -73.1, 7.1, "68001", "Bucaramanga");
        var ficha = new FichaProyectoViewModel(dto, () => { });
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object)
        {
            IndexarEnSegundoPlano = false,
            ProyectoDetalle = ficha
        };

        var nodoArchivo = new Geomatica.Desktop.Models.ArchivoVirtual
        {
            Nombre = "levantamiento.shp",
            RutaRelativaVirtual = "Otros/levantamiento.shp",
            UbicacionRelativa = "Otros",
            Extension = ".shp"
        };

        // Act
        bool movido = vm.MoverElementoAFolder(nodoArchivo, "Datos_Espaciales/Subcarpeta");

        // Assert
        movido.Should().BeTrue();
        File.Exists(archivoFisico).Should().BeFalse();
        File.Exists(Path.Combine(dirDestino, "levantamiento.shp")).Should().BeTrue();
        vm.Estado.Should().Contain("Datos_Espaciales/Subcarpeta/levantamiento.shp");

        // El índice en caché debe haberse invalidado tras el movimiento
        _service.TryObtenerIndice(_tempDir, out _).Should().BeFalse();

        // Debe haberse invocado el registro de auditoría con la acción MOVER y las rutas relativas
        _repoMock.Verify(r => r.RegistrarAuditoriaAsync(
            77,
            "Proyecto Trazabilidad",
            "MOVER",
            It.Is<string>(d => d.Contains("Otros/levantamiento.shp") && d.Contains("Datos_Espaciales/Subcarpeta/levantamiento.shp")),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }
}

