using FluentAssertions;
using Geomatica.Desktop.Services;
using System;
using System.IO;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class ProyectoArchivosServiceEstructuraTests : IDisposable
{
    private readonly ProyectoArchivosService _service;
    private readonly string _tempDir;

    public ProyectoArchivosServiceEstructuraTests()
    {
        _service = new ProyectoArchivosService();
        _tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaEstructuraTests_" + Guid.NewGuid().ToString("N"));
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
            // Ignorado
        }
    }

    [Fact]
    public void TieneCarpetas_RutaInvalidaOInexistente_RetornaFalse()
    {
        _service.TieneCarpetas(null).Should().BeFalse();
        _service.TieneCarpetas("").Should().BeFalse();
        _service.TieneCarpetas(Path.Combine(_tempDir, "RutaQueNoExiste123")).Should().BeFalse();
    }

    [Fact]
    public void TieneCarpetas_CarpetaVaciaOSoloConArchivos_RetornaFalse()
    {
        // Arrange
        string dirVacio = Path.Combine(_tempDir, "CarpetaVacia");
        Directory.CreateDirectory(dirVacio);

        string dirSoloArchivos = Path.Combine(_tempDir, "CarpetaSoloArchivos");
        Directory.CreateDirectory(dirSoloArchivos);
        File.WriteAllText(Path.Combine(dirSoloArchivos, "notas.txt"), "prueba");

        // Act & Assert
        _service.TieneCarpetas(dirVacio).Should().BeFalse();
        _service.TieneCarpetas(dirSoloArchivos).Should().BeFalse();
    }

    [Fact]
    public void TieneCarpetas_ConSubcarpetasExistentes_RetornaTrue()
    {
        // Arrange
        string dirConCarpetas = Path.Combine(_tempDir, "CarpetaConSubcarpetas");
        Directory.CreateDirectory(Path.Combine(dirConCarpetas, "SubcarpetaExistente"));

        // Act & Assert
        _service.TieneCarpetas(dirConCarpetas).Should().BeTrue();
    }

    [Fact]
    public void GestionarEstructuraCarpetas_SiExistenCarpetas_NoCreaEstructuraNiPregunta()
    {
        // Arrange: carpeta que ya tiene una subcarpeta previa
        string dirProyecto = Path.Combine(_tempDir, "ProyectoConCarpetas");
        Directory.CreateDirectory(Path.Combine(dirProyecto, "MiCarpetaPrevia"));

        bool fueConsultado = false;

        // Act
        bool resultado = _service.GestionarEstructuraCarpetas(dirProyecto, () =>
        {
            fueConsultado = true;
            return true;
        });

        // Assert: no debe consultar ni debe crear las carpetas base estándar
        resultado.Should().BeFalse();
        fueConsultado.Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Datos_Espaciales")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Documentos")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Entregables")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Otros")).Should().BeFalse();
    }

    [Fact]
    public void GestionarEstructuraCarpetas_SiNoExistenCarpetasYUsuarioAcepta_CreaEstructuraEstandar()
    {
        // Arrange: carpeta sin subcarpetas
        string dirProyecto = Path.Combine(_tempDir, "ProyectoSinCarpetas");
        Directory.CreateDirectory(dirProyecto);

        bool fueConsultado = false;

        // Act: el usuario responde SÍ (true)
        bool resultado = _service.GestionarEstructuraCarpetas(dirProyecto, () =>
        {
            fueConsultado = true;
            return true;
        });

        // Assert
        resultado.Should().BeTrue();
        fueConsultado.Should().BeTrue();
        Directory.Exists(Path.Combine(dirProyecto, "Datos_Espaciales")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirProyecto, "Documentos")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirProyecto, "Entregables")).Should().BeTrue();
        Directory.Exists(Path.Combine(dirProyecto, "Otros")).Should().BeTrue();
    }

    [Fact]
    public void GestionarEstructuraCarpetas_SiNoExistenCarpetasYUsuarioRechaza_NoCreaSubcarpetas()
    {
        // Arrange: carpeta sin subcarpetas
        string dirProyecto = Path.Combine(_tempDir, "ProyectoSinCarpetasRechazado");
        Directory.CreateDirectory(dirProyecto);

        bool fueConsultado = false;

        // Act: el usuario responde NO (false)
        bool resultado = _service.GestionarEstructuraCarpetas(dirProyecto, () =>
        {
            fueConsultado = true;
            return false;
        });

        // Assert
        resultado.Should().BeFalse();
        fueConsultado.Should().BeTrue();
        Directory.Exists(dirProyecto).Should().BeTrue();
        Directory.Exists(Path.Combine(dirProyecto, "Datos_Espaciales")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Documentos")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Entregables")).Should().BeFalse();
        Directory.Exists(Path.Combine(dirProyecto, "Otros")).Should().BeFalse();
    }
}

