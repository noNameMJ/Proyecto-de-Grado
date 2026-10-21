using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Geomatica.Desktop.Models;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class ArchivosViewModelOrdenamientoTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IProyectoRepository> _repoMock;
    private readonly FiltrosViewModel _filtros;
    private readonly ProyectoArchivosService _service;

    public ArchivosViewModelOrdenamientoTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_ArchivosVM_Sort_" + Guid.NewGuid().ToString("N"));
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
    public void OrdenarPor_Nombre_AscendenteYDescendente_OrdenaCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var f1 = new ArchivoVirtual { Nombre = "Zebra.shp", Extension = ".shp" };
        var f2 = new ArchivoVirtual { Nombre = "Alpha.tif", Extension = ".tif" };
        var c1 = new CarpetaVirtual { Nombre = "BetaDir" };
        var c2 = new CarpetaVirtual { Nombre = "GammaDir" };

        vm.Items.Add(f1);
        vm.Items.Add(f2);
        vm.Items.Add(c1);
        vm.Items.Add(c2);

        // Act 1: Ordenar por Nombre (por defecto ya está en Nombre Ascendente, hacer clic cambia a Descendente)
        vm.OrdenarPorCommand.Execute("Nombre");

        // Assert 1: Descendente -> Carpetas Z-A, luego Archivos Z-A
        vm.OrdenAscendente.Should().BeFalse();
        vm.ColumnaOrden.Should().Be("Nombre");
        vm.HeaderNombre.Should().Be("Nombre ▼");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("GammaDir", "BetaDir", "Zebra.shp", "Alpha.tif");

        // Act 2: Clic de nuevo -> Ascendente
        vm.OrdenarPorCommand.Execute("Nombre");

        // Assert 2: Ascendente -> Carpetas A-Z, luego Archivos A-Z
        vm.OrdenAscendente.Should().BeTrue();
        vm.HeaderNombre.Should().Be("Nombre ▲");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("BetaDir", "GammaDir", "Alpha.tif", "Zebra.shp");
    }

    [Fact]
    public void OrdenarPor_Tamano_AscendenteYDescendente_OrdenaCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var fChico = new ArchivoVirtual { Nombre = "chico.txt", TamanoBytes = 100 };
        var fMedio = new ArchivoVirtual { Nombre = "medio.shp", TamanoBytes = 5000 };
        var fGrande = new ArchivoVirtual { Nombre = "grande.las", TamanoBytes = 100000 };
        var carpeta = new CarpetaVirtual { Nombre = "Carpeta" };

        vm.Items.Add(fGrande);
        vm.Items.Add(fChico);
        vm.Items.Add(carpeta);
        vm.Items.Add(fMedio);

        // Act 1: Ordenar por Tamaño (primer clic -> Ascendente)
        vm.OrdenarPorCommand.Execute("Tamano");

        // Assert 1: Carpetas arriba, archivos de menor a mayor
        vm.ColumnaOrden.Should().Be("Tamano");
        vm.OrdenAscendente.Should().BeTrue();
        vm.HeaderTamano.Should().Be("Tamaño ▲");
        vm.HeaderNombre.Should().Be("Nombre");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("Carpeta", "chico.txt", "medio.shp", "grande.las");

        // Act 2: Clic de nuevo -> Descendente
        vm.OrdenarPorCommand.Execute("Tamano");

        // Assert 2: Carpetas arriba, archivos de mayor a menor
        vm.OrdenAscendente.Should().BeFalse();
        vm.HeaderTamano.Should().Be("Tamaño ▼");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("Carpeta", "grande.las", "medio.shp", "chico.txt");
    }

    [Fact]
    public void OrdenarPor_Tipo_AscendenteYDescendente_OrdenaCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var f1 = new ArchivoVirtual { Nombre = "a.tif", Extension = ".tif" };
        var f2 = new ArchivoVirtual { Nombre = "b.dwg", Extension = ".dwg" };
        var f3 = new ArchivoVirtual { Nombre = "c.shp", Extension = ".shp" };
        var c = new CarpetaVirtual { Nombre = "dir" };

        vm.Items.Add(f1);
        vm.Items.Add(f2);
        vm.Items.Add(c);
        vm.Items.Add(f3);

        // Act 1: Ordenar por Tipo (Ascendente)
        vm.OrdenarPorCommand.Execute("Tipo");

        // Assert 1: Carpetas arriba, luego .dwg, .shp, .tif
        vm.ColumnaOrden.Should().Be("Tipo");
        vm.OrdenAscendente.Should().BeTrue();
        vm.HeaderTipo.Should().Be("Tipo ▲");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("dir", "b.dwg", "c.shp", "a.tif");

        // Act 2: Ordenar por Tipo (Descendente)
        vm.OrdenarPorCommand.Execute("Tipo");

        // Assert 2: Carpetas arriba, luego .tif, .shp, .dwg
        vm.OrdenAscendente.Should().BeFalse();
        vm.HeaderTipo.Should().Be("Tipo ▼");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("dir", "a.tif", "c.shp", "b.dwg");
    }

    [Fact]
    public void OrdenarPor_Modificado_AscendenteYDescendente_OrdenaCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var fAntiguo = new ArchivoVirtual { Nombre = "antiguo.txt", FechaModificacion = new DateTime(2025, 1, 1) };
        var fMedio = new ArchivoVirtual { Nombre = "medio.txt", FechaModificacion = new DateTime(2025, 6, 1) };
        var fReciente = new ArchivoVirtual { Nombre = "reciente.txt", FechaModificacion = new DateTime(2026, 1, 1) };

        vm.Items.Add(fReciente);
        vm.Items.Add(fAntiguo);
        vm.Items.Add(fMedio);

        // Act 1: Ordenar por Modificado (Ascendente: más antiguo a más reciente)
        vm.OrdenarPorCommand.Execute("Modificado");

        // Assert 1
        vm.ColumnaOrden.Should().Be("Modificado");
        vm.OrdenAscendente.Should().BeTrue();
        vm.HeaderModificado.Should().Be("Modificado ▲");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("antiguo.txt", "medio.txt", "reciente.txt");

        // Act 2: Ordenar por Modificado (Descendente: más reciente a más antiguo)
        vm.OrdenarPorCommand.Execute("Modificado");

        // Assert 2
        vm.OrdenAscendente.Should().BeFalse();
        vm.HeaderModificado.Should().Be("Modificado ▼");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("reciente.txt", "medio.txt", "antiguo.txt");
    }

    [Fact]
    public void OrdenarPor_Ubicacion_AscendenteYDescendente_OrdenaCorrectamente()
    {
        // Arrange
        var vm = new ArchivosViewModel(_filtros, _service, _repoMock.Object);
        var f1 = new ArchivoVirtual { Nombre = "f1.txt", UbicacionRelativa = "SubcarpetaZ" };
        var f2 = new ArchivoVirtual { Nombre = "f2.txt", UbicacionRelativa = "SubcarpetaA" };
        var f3 = new ArchivoVirtual { Nombre = "f3.txt", UbicacionRelativa = "SubcarpetaM" };

        vm.Items.Add(f1);
        vm.Items.Add(f2);
        vm.Items.Add(f3);

        // Act 1: Ubicación Ascendente
        vm.OrdenarPorCommand.Execute("Ubicacion");

        // Assert 1
        vm.ColumnaOrden.Should().Be("Ubicacion");
        vm.OrdenAscendente.Should().BeTrue();
        vm.HeaderUbicacion.Should().Be("Ubicación ▲");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("f2.txt", "f3.txt", "f1.txt");

        // Act 2: Ubicación Descendente
        vm.OrdenarPorCommand.Execute("Ubicacion");

        // Assert 2
        vm.OrdenAscendente.Should().BeFalse();
        vm.HeaderUbicacion.Should().Be("Ubicación ▼");
        vm.Items.OfType<NodoArchivoVirtual>().Select(n => n.Nombre)
            .Should().ContainInOrder("f1.txt", "f3.txt", "f2.txt");
    }
}

