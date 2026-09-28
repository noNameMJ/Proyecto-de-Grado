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
        res1.Success.Should().BeTrue(because: res1.MensajeError);
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

    [Fact]
    public void Gdal_OpenFileGDB_DriverEstaDisponible()
    {
        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        OSGeo.OGR.Ogr.RegisterAll();
        var driver = OSGeo.OGR.Ogr.GetDriverByName("OpenFileGDB");
        driver.Should().NotBeNull();
    }

    [Fact]
    public void Gdal_PuedeAbrirGdbRealYListarCapas()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        OSGeo.OGR.Ogr.RegisterAll();
        using var ds = OSGeo.OGR.Ogr.Open(rutaGdbReal, 0);
        ds.Should().NotBeNull();

        int layerCount = ds.GetLayerCount();
        layerCount.Should().BeGreaterThan(0);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < layerCount; i++)
        {
            using var layer = ds.GetLayerByIndex(i);
            string name = layer.GetName();
            name.Should().NotBeNullOrWhiteSpace();
            var geom = layer.GetGeomType();
            long count = layer.GetFeatureCount(1);
            sb.AppendLine($"Layer {i}: {name} ({geom}, {count} features)");
            using var layerDefn = layer.GetLayerDefn();
            for (int f = 0; f < layerDefn.GetFieldCount(); f++)
            {
                using var fieldDefn = layerDefn.GetFieldDefn(f);
                sb.AppendLine($"   Field {f}: {fieldDefn.GetName()} ({fieldDefn.GetFieldType()})");
            }
        }
    }

    [Fact]
    public void Gdal_PuedeLeerAdjuntoBinario()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        OSGeo.OGR.Ogr.RegisterAll();
        using var ds = OSGeo.OGR.Ogr.Open(rutaGdbReal, 0);
        using var attachLayer = ds.GetLayerByName("Sheet1_Tabla1__ATTACH");
        attachLayer.Should().NotBeNull();

        using var feat = attachLayer.GetNextFeature();
        feat.Should().NotBeNull();

        int dataIdx = feat.GetFieldIndex("DATA");
        string val = feat.GetFieldAsString(dataIdx);
        val.Should().NotBeNullOrWhiteSpace();

        byte[] bytes = Convert.FromHexString(val);
        bytes.Length.Should().BeGreaterThan(1000);
        // Validar cabecera JPEG FF D8
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
    }

    [Fact]
    public async Task Gdal_VectorTranslate_PuedeExportarGdbAGeoPackage()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        MaxRev.Gdal.Core.GdalBase.ConfigureAll();
        OSGeo.GDAL.Gdal.AllRegister();
        OSGeo.OGR.Ogr.RegisterAll();

        string outGpkg = Path.Combine(_cacheDir, "test_out.gpkg");
        {
            using var srcDs = OSGeo.OGR.Ogr.Open(rutaGdbReal, 0);
            srcDs.Should().NotBeNull();

            var gpkgDriver = OSGeo.OGR.Ogr.GetDriverByName("GPKG");
            gpkgDriver.Should().NotBeNull();

            using var destDs = gpkgDriver.CreateDataSource(outGpkg, null);
            destDs.Should().NotBeNull();

            int layerCount = srcDs.GetLayerCount();
            for (int i = 0; i < layerCount; i++)
            {
                using var layer = srcDs.GetLayerByIndex(i);
                using var copied = destDs.CopyLayer(layer, layer.GetName(), null);
            }
            destDs.FlushCache();
        }

        File.Exists(outGpkg).Should().BeTrue();
        new FileInfo(outGpkg).Length.Should().BeGreaterThan(0);

        var gpkg = await Esri.ArcGISRuntime.Data.GeoPackage.OpenAsync(outGpkg);
        try
        {
            gpkg.GeoPackageFeatureTables.Should().NotBeEmpty();
        }
        finally
        {
            gpkg.Close();
        }
    }

    [Fact]
    public void DisponibilidadMotores_VerificaPrioridadSegunEntorno()
    {
        _service.IsGdalAvailable.Should().BeTrue();
        if (_service.IsArcPyAvailable)
        {
            _service.ProveedorActivo.Should().Be("ArcPy (ArcGIS Pro)");
        }
        else
        {
            _service.ProveedorActivo.Should().Be("GDAL OpenFileGDB (Autónomo)");
        }

        // Si se fuerza un entorno sin Python/ArcPy, debe degradar elegantemente a GDAL
        var serviceAutonomo = new FileGdbImporterService(customPythonPath: "no_existe_python.exe", customCacheDirectory: _cacheDir);
        serviceAutonomo.IsArcPyAvailable.Should().BeFalse();
        serviceAutonomo.IsGdalAvailable.Should().BeTrue();
        serviceAutonomo.ProveedorActivo.Should().Be("GDAL OpenFileGDB (Autónomo)");
    }

    [Fact]
    public async Task ImportarGdbAsync_ConGdbReal_ExportaSegunPrioridadYLeeDeCache()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        var result = await _service.ImportarGdbAsync(rutaGdbReal);

        result.Success.Should().BeTrue(because: result.MensajeError);
        result.GeoPackagePath.Should().NotBeNull();
        File.Exists(result.GeoPackagePath!).Should().BeTrue();
        new FileInfo(result.GeoPackagePath!).Length.Should().BeGreaterThan(0);

        // Verificamos que se crearon los metadatos JSON
        string metaPath = result.GeoPackagePath + ".meta.json";
        File.Exists(metaPath).Should().BeTrue();

        // Segunda llamada debe salir de caché inmediatamente
        var resCache = await _service.ImportarGdbAsync(rutaGdbReal);
        resCache.Success.Should().BeTrue();
        resCache.FromCache.Should().BeTrue();
    }

    [Fact]
    public async Task ImportarGdbAsync_SinArcPy_DegradaExitosamenteAGdalAutonomo()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        string subCache = Path.Combine(_cacheDir, "AutonomoTest");
        var serviceAutonomo = new FileGdbImporterService(customPythonPath: "no_existe_python.exe", customCacheDirectory: subCache);
        serviceAutonomo.IsArcPyAvailable.Should().BeFalse();

        var result = await serviceAutonomo.ImportarGdbAsync(rutaGdbReal);
        result.Success.Should().BeTrue(because: result.MensajeError);
        result.GeoPackagePath.Should().NotBeNull();
        File.Exists(result.GeoPackagePath!).Should().BeTrue();
    }

    [Fact]
    public async Task ObtenerCapasGdbAsync_ConGdbReal_RetornaCapasYTablas()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        var capas = await _service.ObtenerCapasGdbAsync(rutaGdbReal);

        capas.Should().NotBeEmpty();
        capas.Should().Contain(c => c.Nombre == "Sheet1_Tabla1" && c.TipoGeometria == "Point");
        capas.Should().Contain(c => c.Nombre == "Sheet1_Tabla1__ATTACH" && c.EsAdjunto);
    }

    [Fact]
    public async Task ObtenerAdjuntosElementoAsync_ConGdbReal_EjecutaCorrectamente()
    {
        string rutaGdbReal = @"D:\Pruebas\INVENTARIO_FORESTAL_GDB\cce98b87-a635-4ece-a6a8-e5c7d5303c61.gdb";
        if (!Directory.Exists(rutaGdbReal)) return;

        var adjuntos = await _service.ObtenerAdjuntosElementoAsync(rutaGdbReal, "{EE8517DF-AE85-4963-B63E-51F1D6AF586D}");
        adjuntos.Should().NotBeNull();
    }
}

