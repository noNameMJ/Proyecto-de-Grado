using FluentAssertions;
using Geomatica.Desktop.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class ProyectoArchivosServiceFormatosTests : IDisposable
{
    private readonly ProyectoArchivosService _service;
    private readonly string _tempDir;

    public ProyectoArchivosServiceFormatosTests()
    {
        _service = new ProyectoArchivosService();
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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
        catch
        {
            // Ignorar errores de limpieza de carpeta temporal
        }
    }

    [Fact]
    public async Task EscanearFormatosArchivosAsync_RutaNulaOVacia_RetornaResumenVacio()
    {
        // Act
        var resNull = await _service.EscanearFormatosArchivosAsync(null);
        var resEmpty = await _service.EscanearFormatosArchivosAsync("");
        var resWhitespace = await _service.EscanearFormatosArchivosAsync("   ");

        // Assert
        resNull.TotalArchivos.Should().Be(0);
        resNull.Categorias.Should().BeEmpty();

        resEmpty.TotalArchivos.Should().Be(0);
        resEmpty.Categorias.Should().BeEmpty();

        resWhitespace.TotalArchivos.Should().Be(0);
        resWhitespace.Categorias.Should().BeEmpty();
    }

    [Fact]
    public async Task EscanearFormatosArchivosAsync_RutaInexistente_RetornaResumenVacio()
    {
        // Arrange
        var rutaInexistente = Path.Combine(_tempDir, "SubCarpetaInexistente_12345");

        // Act
        var res = await _service.EscanearFormatosArchivosAsync(rutaInexistente);

        // Assert
        res.TotalArchivos.Should().Be(0);
        res.Categorias.Should().BeEmpty();
    }

    [Fact]
    public async Task EscanearFormatosArchivosAsync_ConArchivosEspacialesYDiversos_ClasificaCategoriasCorrectamente()
    {
        // Arrange: Crear estructura de prueba con varios tipos de archivos
        var subDir1 = Path.Combine(_tempDir, "Datos_Espaciales");
        var subDir2 = Path.Combine(_tempDir, "Documentos");
        Directory.CreateDirectory(subDir1);
        Directory.CreateDirectory(subDir2);

        // Vectoriales
        File.WriteAllText(Path.Combine(subDir1, "predios.shp"), "dummy shp");
        File.WriteAllText(Path.Combine(subDir1, "vias.geojson"), "dummy geojson");

        // Raster
        File.WriteAllText(Path.Combine(subDir1, "ortofoto.tif"), "dummy tif");
        File.WriteAllText(Path.Combine(subDir1, "modelo_elevacion.dem"), "dummy dem");

        // LiDAR
        File.WriteAllText(Path.Combine(subDir1, "nube_puntos.laz"), "dummy laz");

        // CAD
        File.WriteAllText(Path.Combine(subDir1, "plano_topografico.dwg"), "dummy dwg");

        // Documentos
        File.WriteAllText(Path.Combine(subDir2, "informe_final.pdf"), "dummy pdf");
        File.WriteAllText(Path.Combine(subDir2, "metadatos.docx"), "dummy docx");

        // Act
        var res = await _service.EscanearFormatosArchivosAsync(_tempDir);

        // Assert
        res.TotalArchivos.Should().Be(8);
        res.Categorias.Should().HaveCount(5);

        var catVector = res.Categorias.FirstOrDefault(c => c.Categoria == "Vectorial");
        catVector.Should().NotBeNull();
        catVector!.CantidadArchivos.Should().Be(2);
        catVector.Icono.Should().Be("🗺️");
        catVector.TextoBadge.Should().Be("🗺️ Vectorial (2)");

        var catRaster = res.Categorias.FirstOrDefault(c => c.Categoria == "Raster / Ortofoto");
        catRaster.Should().NotBeNull();
        catRaster!.CantidadArchivos.Should().Be(2);
        catRaster.Icono.Should().Be("🛰️");

        var catLidar = res.Categorias.FirstOrDefault(c => c.Categoria == "LiDAR / Nubes");
        catLidar.Should().NotBeNull();
        catLidar!.CantidadArchivos.Should().Be(1);
        catLidar.Icono.Should().Be("☁️");

        var catCad = res.Categorias.FirstOrDefault(c => c.Categoria == "CAD / Planos");
        catCad.Should().NotBeNull();
        catCad!.CantidadArchivos.Should().Be(1);
        catCad.Icono.Should().Be("📐");

        var catDoc = res.Categorias.FirstOrDefault(c => c.Categoria == "Documentación");
        catDoc.Should().NotBeNull();
        catDoc!.CantidadArchivos.Should().Be(2);
        catDoc.Icono.Should().Be("📄");

        // Validar desglose detallado de ExtensionesLista
        res.ExtensionesLista.Should().NotBeEmpty();
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".tif" && e.CantidadArchivos == 1 && e.ExtensionMayus == "TIF");
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".shp" && e.CantidadArchivos == 1 && e.ExtensionMayus == "SHP");
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".laz" && e.CantidadArchivos == 1 && e.ExtensionMayus == "LAZ");
    }

    [Fact]
    public async Task EscanearFormatosArchivosAsync_ConArchivosSidecar_ClasificaAuxiliares()
    {
        // Arrange
        var dirEspaciales = Path.Combine(_tempDir, "Datos_Espaciales");
        Directory.CreateDirectory(dirEspaciales);

        File.WriteAllText(Path.Combine(dirEspaciales, "ortofoto.tif"), "tif");
        File.WriteAllText(Path.Combine(dirEspaciales, "ortofoto.tfw"), "tfw");
        File.WriteAllText(Path.Combine(dirEspaciales, "predios.shp"), "shp");
        File.WriteAllText(Path.Combine(dirEspaciales, "predios.prj"), "prj");
        File.WriteAllText(Path.Combine(dirEspaciales, "predios.dbf"), "dbf");

        // Act
        var res = await _service.EscanearFormatosArchivosAsync(_tempDir);

        // Assert
        res.TotalArchivos.Should().Be(5);
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".tif" && !e.EsAuxiliar);
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".shp" && !e.EsAuxiliar);
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".tfw" && e.EsAuxiliar && e.Categoria == "Auxiliar / Sidecar");
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".prj" && e.EsAuxiliar);
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".dbf" && e.EsAuxiliar);
    }

    [Fact]
    public void ListarArchivosPorExtension_ConSubcarpetas_RetornaUbicacionRelativaCorrecta()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Datos_Espaciales", "Ortofotos_2024");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(Path.Combine(subDir, "mosaico_norte.tif"), "dummy tif 1");
        File.WriteAllText(Path.Combine(subDir, "mosaico_sur.tif"), "dummy tif 2");
        File.WriteAllText(Path.Combine(_tempDir, "raiz.tif"), "dummy tif raiz");
        File.WriteAllText(Path.Combine(subDir, "otro.shp"), "dummy shp");

        // Act
        var lista = _service.ListarArchivosPorExtension(_tempDir, ".tif");

        // Assert
        lista.Should().HaveCount(3);
        lista.Should().OnlyContain(a => a.Extension.Equals(".tif", StringComparison.OrdinalIgnoreCase));
        
        var raiz = lista.FirstOrDefault(a => a.Nombre == "raiz.tif");
        raiz.Should().NotBeNull();
        raiz!.UbicacionRelativa.Should().Be("(raíz)");
        raiz.RutaRelativaVirtual.Should().Be("raiz.tif");

        var sub = lista.FirstOrDefault(a => a.Nombre == "mosaico_norte.tif");
        sub.Should().NotBeNull();
        sub!.UbicacionRelativa.Should().Be("Datos_Espaciales/Ortofotos_2024");
        sub.RutaRelativaVirtual.Should().Be("Datos_Espaciales/Ortofotos_2024/mosaico_norte.tif");
    }

    [Fact]
    public void ListarContenidoVirtual_ConCarpetaGdb_RetornaArchivoVirtualAtomicoYNoCarpeta()
    {
        // Arrange
        var gdbPath = Path.Combine(_tempDir, "Inventario_Forestal.gdb");
        Directory.CreateDirectory(gdbPath);
        File.WriteAllText(Path.Combine(gdbPath, "a00000001.gdbtable"), "binary data table");
        File.WriteAllText(Path.Combine(gdbPath, "a00000001.gdbindexes"), "index data");
        File.WriteAllText(Path.Combine(gdbPath, "timestamps"), "timestamp data");

        // Act: Listar directorio raíz
        var nodos = _service.ListarContenidoVirtual(_tempDir);

        // Assert: Se debe listar como ArchivoVirtual, NO como CarpetaVirtual
        nodos.Should().HaveCount(1);
        var nodo = nodos.First();
        nodo.Should().BeOfType<Geomatica.Desktop.Models.ArchivoVirtual>();
        var archivoGdb = (Geomatica.Desktop.Models.ArchivoVirtual)nodo;
        archivoGdb.Nombre.Should().Be("Inventario_Forestal.gdb");
        archivoGdb.Extension.Should().Be(".gdb");
        archivoGdb.Icono.Should().Be("🗃️");
        archivoGdb.TamanoBytes.Should().BeGreaterThan(0);

        // Act: Intentar navegar dentro del .gdb
        var nodosInternos = _service.ListarContenidoVirtual(_tempDir, "Inventario_Forestal.gdb");

        // Assert: No debe exponer archivos binarios internos de la GDB
        nodosInternos.Should().BeEmpty();
    }

    [Fact]
    public async Task EscanearFormatosArchivosAsync_ConCarpetaGdb_ClasificaComoUnSoloDatasetVectorial()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "Capas");
        Directory.CreateDirectory(subDir);
        var gdbPath = Path.Combine(subDir, "Redes.gdb");
        Directory.CreateDirectory(gdbPath);

        // Archivos binarios internos de la GDB
        File.WriteAllText(Path.Combine(gdbPath, "a00000001.gdbtable"), "data");
        File.WriteAllText(Path.Combine(gdbPath, "a00000002.gdbtable"), "data");
        File.WriteAllText(Path.Combine(gdbPath, "timestamps"), "data");

        // Archivo vectorial regular fuera de la GDB
        File.WriteAllText(Path.Combine(subDir, "puntos.shp"), "shp data");

        // Act
        var res = await _service.EscanearFormatosArchivosAsync(_tempDir);

        // Assert: Debe contar 2 archivos en total (puntos.shp y Redes.gdb como unidad), no 4 archivos
        res.TotalArchivos.Should().Be(2);

        var catVectorial = res.Categorias.FirstOrDefault(c => c.Categoria == "Vectorial");
        catVectorial.Should().NotBeNull();
        catVectorial!.CantidadArchivos.Should().Be(2);

        res.ExtensionesLista.Should().Contain(e => e.Extension == ".gdb" && !e.EsAuxiliar);
        res.ExtensionesLista.Should().Contain(e => e.Extension == ".shp" && !e.EsAuxiliar);
        res.ExtensionesLista.Should().NotContain(e => e.Extension == ".gdbtable");
    }
}

