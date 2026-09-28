using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using FluentAssertions;
using Geomatica.Desktop.Models;
using Geomatica.Desktop.Services;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class CadImporterServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cacheDir;
    private readonly CadImporterService _service;

    public CadImporterServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTests_CAD_" + Guid.NewGuid().ToString("N"));
        _cacheDir = Path.Combine(_tempDir, "Cache");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_cacheDir);

        _service = new CadImporterService(customCacheDirectory: _cacheDir);
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
        string cadFile = Path.Combine(_tempDir, "Plano_Topografico.dwg");
        File.WriteAllText(cadFile, "dummy dwg content");

        // Act
        string cache1 = _service.ObtenerRutaCache(cadFile);
        string cache2 = _service.ObtenerRutaCache(cadFile);

        // Assert
        cache1.Should().NotBeNullOrWhiteSpace();
        cache1.Should().Be(cache2);
        cache1.Should().EndWith(".gpkg");
        cache1.Should().StartWith(_cacheDir);
        Path.GetFileName(cache1).Should().StartWith("Plano_Topografico_");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ObtenerRutaCache_RutaInvalida_LanzaArgumentException(string? rutaInvalida)
    {
        Assert.Throws<ArgumentException>(() => _service.ObtenerRutaCache(rutaInvalida!));
    }

    [Fact]
    public void LimpiarCache_DebeEliminarArchivosDeDirectorioCache()
    {
        // Arrange
        string dummyFile = Path.Combine(_cacheDir, "plano_test.gpkg");
        File.WriteAllText(dummyFile, "dummy data");
        File.Exists(dummyFile).Should().BeTrue();

        // Act
        _service.LimpiarCache();

        // Assert
        File.Exists(dummyFile).Should().BeFalse();
    }

    [Fact]
    public void DisponibilidadMotores_VerificaPrioridadSegunEntorno()
    {
        _service.IsACadSharpAvailable.Should().BeTrue();
        if (_service.IsArcPyAvailable)
        {
            _service.ProveedorActivo.Should().Be("ArcPy (ArcGIS Pro)");
        }
        else
        {
            _service.ProveedorActivo.Should().Be("ACadSharp (Autónomo)");
        }

        // Si se fuerza un entorno sin Python/ArcPy, debe ser ACadSharp autónomo
        var serviceAutonomo = new CadImporterService(customPythonPath: "no_existe_python.exe", customCacheDirectory: _cacheDir);
        serviceAutonomo.IsArcPyAvailable.Should().BeFalse();
        serviceAutonomo.IsACadSharpAvailable.Should().BeTrue();
        serviceAutonomo.ProveedorActivo.Should().Be("ACadSharp (Autónomo)");
    }

    [Fact]
    public async Task ImportarCadAsync_RutaInexistente_RetornaResultadoFallido()
    {
        // Arrange
        string rutaInexistente = Path.Combine(_tempDir, "NoExiste.dwg");

        // Act
        var result = await _service.ImportarCadAsync(rutaInexistente);

        // Assert
        result.Success.Should().BeFalse();
        result.GeoPackagePath.Should().BeNull();
        result.MensajeError.Should().Contain("no existe");
    }

    [Fact]
    public async Task ImportarCadAsync_ConArchivoDxfReal_ExportaConACadSharpYLeeDeCache()
    {
        // Arrange: Crear un documento CAD real con varias entidades y capas
        string dxfFile = Path.Combine(_tempDir, "Levantamiento_Topografico.dxf");
        var doc = new CadDocument();

        var layerVias = new ACadSharp.Tables.Layer("VIAS");
        layerVias.Color = new ACadSharp.Color(255, 0, 0); // Rojo
        doc.Layers.Add(layerVias);

        var layerPredios = new ACadSharp.Tables.Layer("PREDIOS");
        layerPredios.Color = new ACadSharp.Color(0, 255, 0); // Verde
        doc.Layers.Add(layerPredios);

        var layerPuntos = new ACadSharp.Tables.Layer("PUNTOS_CONTROL");
        doc.Layers.Add(layerPuntos);

        // Línea
        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(1000, 2000, 0),
            EndPoint = new CSMath.XYZ(1500, 2500, 0),
            Layer = layerVias
        });

        // Polígono cerrado (LwPolyline)
        var poly = new LwPolyline
        {
            Layer = layerPredios,
            IsClosed = true
        };
        poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(100, 100)));
        poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(200, 100)));
        poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(200, 200)));
        poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(100, 200)));
        doc.Entities.Add(poly);

        // Punto
        doc.Entities.Add(new Point
        {
            Location = new CSMath.XYZ(1250, 2250, 0),
            Layer = layerPuntos
        });

        // Texto
        doc.Entities.Add(new TextEntity
        {
            Value = "Manzana 04",
            InsertPoint = new CSMath.XYZ(150, 150, 0),
            Height = 5.0,
            Layer = layerPredios
        });

        using (var writer = new DxfWriter(dxfFile, doc, false))
        {
            writer.Write();
        }

        File.Exists(dxfFile).Should().BeTrue();

        // Forzar servicio autónomo para probar la conversión C# directa
        var serviceAutonomo = new CadImporterService(customPythonPath: "no_existe.exe", customCacheDirectory: _cacheDir);

        // Act 1: Conversión inicial
        var res1 = await serviceAutonomo.ImportarCadAsync(dxfFile);

        // Assert 1
        res1.Success.Should().BeTrue(because: res1.MensajeError);
        res1.GeoPackagePath.Should().NotBeNull();
        File.Exists(res1.GeoPackagePath!).Should().BeTrue();
        new FileInfo(res1.GeoPackagePath!).Length.Should().BeGreaterThan(0);
        res1.Capas.Should().NotBeEmpty();
        res1.ProveedorUtilizado.Should().Be("ACadSharp (Autónomo)");

        // Validar que el GeoPackage generado es perfectamente válido para ArcGIS Maps SDK
        var gpkg = await Esri.ArcGISRuntime.Data.GeoPackage.OpenAsync(res1.GeoPackagePath!);
        try
        {
            gpkg.GeoPackageFeatureTables.Should().NotBeEmpty();
            gpkg.GeoPackageFeatureTables.Count.Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            gpkg.Close();
        }

        // Act 2: Carga posterior (debe resolver inmediatamente desde caché)
        var res2 = await serviceAutonomo.ImportarCadAsync(dxfFile);

        // Assert 2
        res2.Success.Should().BeTrue();
        res2.FromCache.Should().BeTrue();
        res2.GeoPackagePath.Should().Be(res1.GeoPackagePath);
    }

    [Fact]
    public async Task ObtenerCapasCadAsync_ConArchivoDxfReal_RetornaCapasYMetadatos()
    {
        // Arrange
        string dxfFile = Path.Combine(_tempDir, "Plano_Estratificacion.dxf");
        var doc = new CadDocument();

        var layerVias = new ACadSharp.Tables.Layer("EJES_VIALES");
        doc.Layers.Add(layerVias);

        doc.Entities.Add(new Line
        {
            StartPoint = new CSMath.XYZ(10, 20, 0),
            EndPoint = new CSMath.XYZ(30, 40, 0),
            Layer = layerVias
        });

        using (var writer = new DxfWriter(dxfFile, doc, false))
        {
            writer.Write();
        }

        // Act
        var capas = await _service.ObtenerCapasCadAsync(dxfFile);

        // Assert
        capas.Should().NotBeEmpty();
        capas.Should().Contain(c => c.Nombre == "EJES_VIALES" && c.CantidadElementos == 1);

        // Segunda llamada debe resolver desde caché JSON
        var capasCache = await _service.ObtenerCapasCadAsync(dxfFile);
        capasCache.Should().HaveCount(capas.Count);
    }

    [Theory]
    [InlineData("Point", "📍", "Puntos")]
    [InlineData("Polyline", "📏", "Líneas")]
    [InlineData("Polygon", "⬡", "Polígonos")]
    [InlineData("Text", "🔤", "Anotaciones / Textos")]
    public void CadCapaInfo_PropiedadesCalculadas_FormateanCorrectamente(string tipo, string iconoEsperado, string tipoTextoEsperado)
    {
        var info = new CadCapaInfo
        {
            Nombre = "CapaPrueba",
            TipoGeometria = tipo,
            CantidadElementos = 42,
            CrsNombre = "EPSG:9377"
        };

        info.Icono.Should().Be(iconoEsperado);
        info.TipoGeometriaTexto.Should().Be(tipoTextoEsperado);
        info.DetalleTexto.Should().Contain("42");
        info.DetalleCompleto.Should().Contain("EPSG:9377");
    }
}

