using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class FileGdbImporterServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheDir;
    private readonly FileGdbImporterService _service;

    public FileGdbImporterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_GDB_" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(_tempDir, "Cache");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_cacheDir);

        _service = new FileGdbImporterService(customCacheDirectory: _cacheDir);
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
    public void ObtenerRutaCache_RutaValida_GeneraNombreUnicoYDeterminista()
    {
        // Arrange
        var gdbFolder = Path.Combine(_tempDir, "Catastro.gdb");
        Directory.CreateDirectory(gdbFolder);
        File.WriteAllText(Path.Combine(gdbFolder, "test.gdbtable"), "contenido de prueba");

        // Act
        string cache1 = _service.ObtenerRutaCache(gdbFolder);
        string cache2 = _service.ObtenerRutaCache(gdbFolder);

        // Assert
        cache1.Should().NotBeNullOrWhiteSpace();
        cache1.Should().Be(cache2);
        cache1.Should().EndWith(".gpkg");
        cache1.Should().StartWith(_cacheDir);
        Path.GetFileName(cache1).Should().StartWith("Catastro_");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ObtenerRutaCache_RutaVacia_LanzaArgumentException(string? rutaInvalida)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.ObtenerRutaCache(rutaInvalida!));
    }

    [Fact]
    public async Task ImportarGdbAsync_RutaInexistente_RetornaResultadoFallido()
    {
        // Arrange
        string rutaInexistente = Path.Combine(_tempDir, "NoExiste.gdb");

        // Act
        var result = await _service.ImportarGdbAsync(rutaInexistente);

        // Assert
        result.Success.Should().BeFalse();
        result.GeoPackagePath.Should().BeNull();
        result.MensajeError.Should().Contain("no existe");
    }

    [Fact]
    public async Task ImportarGdbAsync_RutaNula_RetornaResultadoFallido()
    {
        // Act
        var result = await _service.ImportarGdbAsync(null!);

        // Assert
        result.Success.Should().BeFalse();
        result.MensajeError.Should().NotBeNull();
    }

    [Fact]
    public void LimpiarCache_DebeEliminarArchivosGpkgDeDirectorioCache()
    {
        // Arrange
        string archivoDummy = Path.Combine(_cacheDir, "prueba_cache_123.gpkg");
        File.WriteAllText(archivoDummy, "dummy content");
        File.Exists(archivoDummy).Should().BeTrue();

        // Act
        _service.LimpiarCache();

        // Assert
        File.Exists(archivoDummy).Should().BeFalse();
    }

    [Fact]
    public async Task ImportarGdbAsync_ConGdbRealSiExiste_ExportaYLeeDeCache()
    {
        // Arrange: Verificar si la ruta de prueba del usuario existe en este equipo
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal) || !_service.IsArcPyAvailable)
        {
            // Omitir ejecución si el archivo de prueba o ArcGIS Pro no está disponible en el entorno
            return;
        }

        // Act 1: Importación inicial (convierte y almacena en caché)
        var res1 = await _service.ImportarGdbAsync(rutaGdbReal);

        // Assert 1
        res1.Success.Should().BeTrue();
        res1.GeoPackagePath.Should().NotBeNull();
        File.Exists(res1.GeoPackagePath!).Should().BeTrue();
        new FileInfo(res1.GeoPackagePath!).Length.Should().BeGreaterThan(0);

        // Act 2: Carga posterior (debe resolver inmediatamente desde caché)
        var res2 = await _service.ImportarGdbAsync(rutaGdbReal);

        // Assert 2
        res2.Success.Should().BeTrue();
        res2.FromCache.Should().BeTrue();
        res2.GeoPackagePath.Should().Be(res1.GeoPackagePath);
    }

    [Fact]
    public async Task ObtenerCapasGdbAsync_RutaInvalida_RetornaListaVacia()
    {
        var capas = await _service.ObtenerCapasGdbAsync(Path.Combine(_tempDir, "Inexistente.gdb"));
        capas.Should().BeEmpty();
    }

    [Fact]
    public async Task ObtenerCapasGdbAsync_ConCacheJsonExistente_ParseaCapasYMetadatos()
    {
        // Arrange
        var gdbFolder = Path.Combine(_tempDir, "PruebaMeta.gdb");
        Directory.CreateDirectory(gdbFolder);
        File.WriteAllText(Path.Combine(gdbFolder, "a00000001.gdbtable"), "datos");

        string cacheGpkg = _service.ObtenerRutaCache(gdbFolder);
        string metaPath = cacheGpkg + ".meta.json";

        string jsonMock = @"[
            {
                ""name"": ""Arbolado"",
                ""geometry_type"": ""Point"",
                ""count"": 2947,
                ""crs"": ""GCS_WGS_1984"",
                ""is_table"": false
            },
            {
                ""name"": ""Fotos_Adjuntas"",
                ""geometry_type"": ""None"",
                ""count"": 6183,
                ""crs"": """",
                ""is_table"": true
            }
        ]";
        File.WriteAllText(metaPath, jsonMock);

        // Act
        var capas = await _service.ObtenerCapasGdbAsync(gdbFolder);

        // Assert
        capas.Should().HaveCount(2);

        var capaPuntos = capas[0];
        capaPuntos.Nombre.Should().Be("Arbolado");
        capaPuntos.TipoGeometria.Should().Be("Point");
        capaPuntos.CantidadElementos.Should().Be(2947);
        capaPuntos.Icono.Should().Be("📍");
        capaPuntos.EsTabla.Should().BeFalse();
        capaPuntos.DetalleTexto.Should().Contain(2947.ToString("N0"));

        var tablaFotos = capas[1];
        tablaFotos.Nombre.Should().Be("Fotos_Adjuntas");
        tablaFotos.EsTabla.Should().BeTrue();
        tablaFotos.CantidadElementos.Should().Be(6183);
        tablaFotos.Icono.Should().Be("📋");
        tablaFotos.DetalleTexto.Should().Contain(6183.ToString("N0"));
        tablaFotos.DetalleTexto.Should().Contain("registros");
    }

    [Theory]
    [InlineData("Point", false, "📍", "Puntos")]
    [InlineData("Polyline", false, "📏", "Líneas")]
    [InlineData("Polygon", false, "⬡", "Polígonos")]
    [InlineData("None", true, "📋", "Tabla No Espacial")]
    public void GdbCapaInfo_PropiedadesCalculadas_FormateanCorrectamente(string geomType, bool esTabla, string iconoEsperado, string tipoTextoEsperado)
    {
        var info = new Geomatica.Desktop.Models.GdbCapaInfo
        {
            Nombre = "CapaTest",
            TipoGeometria = geomType,
            EsTabla = esTabla,
            CantidadElementos = 100,
            CrsNombre = "MAGNA-SIRGAS"
        };

        info.Icono.Should().Be(iconoEsperado);
        info.TipoGeometriaTexto.Should().Be(tipoTextoEsperado);
        info.DetalleTexto.Should().NotBeNullOrWhiteSpace();
        info.DetalleCompleto.Should().Contain("CapaTest" != null ? "100" : "");
    }

    [Fact]
    public void GdbCapaInfo_ParaAdjuntosYRelaciones_FormateaIconosYTextosEspeciales()
    {
        // Arrange & Act - Tabla de Adjuntos
        var infoAdj = new Geomatica.Desktop.Models.GdbCapaInfo
        {
            Nombre = "Sheet1_Tabla1__ATTACH",
            EsTabla = true,
            EsAdjunto = true,
            CantidadElementos = 6183
        };

        // Assert - Adjuntos
        infoAdj.Icono.Should().Be("📎");
        infoAdj.TipoGeometriaTexto.Should().Be("Tabla de Adjuntos");
        infoAdj.DetalleTexto.Should().Contain(6183.ToString("N0"));
        infoAdj.DetalleTexto.Should().Contain("fotos / archivos adjuntos");

        // Arrange & Act - Clase de Relación
        var infoRel = new Geomatica.Desktop.Models.GdbCapaInfo
        {
            Nombre = "Sheet1_Tabla1__ATTACHREL",
            EsRelacion = true,
            Cardinalidad = "OneToMany",
            TablaOrigen = "Sheet1_Tabla1",
            TablaDestino = "Sheet1_Tabla1__ATTACH"
        };

        // Assert - Relaciones
        infoRel.Icono.Should().Be("🔗");
        infoRel.TipoGeometriaTexto.Should().Be("Clase de Relación");
        infoRel.DetalleTexto.Should().Contain("Sheet1_Tabla1 ➔ Sheet1_Tabla1__ATTACH");
    }

    [Fact]
    public void AdjuntoFotoInfo_PropiedadesCalculadas_FormateanTamanoYEsImagenCorrectamente()
    {
        var foto1 = new Geomatica.Desktop.Models.AdjuntoFotoInfo
        {
            AttachmentId = 1,
            Nombre = "arbol_ficus.jpg",
            ContentType = "image/jpeg",
            TamanoBytes = 450 * 1024 // 450 KB
        };

        foto1.EsImagen.Should().BeTrue();
        foto1.TamanoLegible.Should().Be(450.0.ToString("F1") + " KB");

        var doc = new Geomatica.Desktop.Models.AdjuntoFotoInfo
        {
            AttachmentId = 2,
            Nombre = "ficha.pdf",
            ContentType = "application/pdf",
            TamanoBytes = 2 * 1024 * 1024 // 2 MB
        };

        doc.EsImagen.Should().BeFalse();
        doc.TamanoLegible.Should().Be(2.0.ToString("F2") + " MB");
    }

    [Fact]
    public async Task ObtenerAdjuntosElementoAsync_ConCacheValida_RetornaAdjuntosInmediatamente()
    {
        // Arrange
        string gdbPath = Path.Combine(_tempDir, "Inventario.gdb");
        Directory.CreateDirectory(gdbPath);

        string gid = "EE8517DF-AE85-4963-B63E-51F1D6AF586D";
        string cleanGid = gid.ToLowerInvariant();

        string nombreGdb = Path.GetFileNameWithoutExtension(gdbPath);
        using var sha = System.Security.Cryptography.SHA256.Create();
        string gdbHash = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(gdbPath.ToLowerInvariant())))[..12].ToLowerInvariant();

        string attDir = Path.Combine(_cacheDir, "Attachments", $"{nombreGdb}_{gdbHash}", cleanGid);
        Directory.CreateDirectory(attDir);

        string testImgPath = Path.Combine(attDir, "1_1.jpg");
        File.WriteAllText(testImgPath, "fake image bytes");

        string jsonMeta = @"[
            {
                ""attachment_id"": 1,
                ""name"": ""1.jpg"",
                ""content_type"": ""image/jpeg"",
                ""size"": 1024,
                ""local_path"": """ + testImgPath.Replace("\\", "\\\\") + @"""
            }
        ]";
        File.WriteAllText(Path.Combine(attDir, "attachments.json"), jsonMeta);

        // Act
        var adjuntos = await _service.ObtenerAdjuntosElementoAsync(gdbPath, gid);

        // Assert
        adjuntos.Should().NotBeNull();
        adjuntos.Should().HaveCount(1);
        adjuntos[0].AttachmentId.Should().Be(1);
        adjuntos[0].Nombre.Should().Be("1.jpg");
        adjuntos[0].RutaArchivoLocal.Should().Be(testImgPath);
        adjuntos[0].EsImagen.Should().BeTrue();
    }
}

