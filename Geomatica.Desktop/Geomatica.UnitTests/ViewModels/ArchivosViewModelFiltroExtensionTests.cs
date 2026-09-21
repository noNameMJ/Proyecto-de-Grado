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
}
