using System.IO;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class FichaProyectoMetadatosTests
{
    private static ProyectoDetalleDto CrearDto(
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        string? entidades = null,
        string? representante = null,
        string? rutaArchivos = null)
    {
        return new ProyectoDetalleDto(
            Id: 10,
            Titulo: "Proyecto Metadatos",
            Descripcion: "Descripcion",
            FechaInicio: fechaInicio,
            PalabraClave: "sig, catastro",
            RutaArchivos: rutaArchivos,
            Lon: -73.1,
            Lat: 7.1,
            MunicipioCodigo: "68001",
            MunicipioNombre: "Bucaramanga",
            FechaFin: fechaFin,
            Entidades: entidades,
            Representante: representante
        );
    }

    [Theory]
    [InlineData("2022-03-01", "2024-11-15", "01/03/2022 — 15/11/2024")]
    [InlineData("2022-03-01", "2022-03-01", "01/03/2022")]
    [InlineData(null, "2024-05-20", "Finalizado: 20/05/2024")]
    [InlineData("2022-05-15", null, "15/05/2022")]
    [InlineData(null, null, "Sin fecha")]
    public void PeriodoTexto_CalculaFormatoCorrectoSegunFechas(string? fechaInicioStr, string? fechaFinStr, string esperado)
    {
        // Arrange
        DateTime? fechaInicio = fechaInicioStr != null ? DateTime.Parse(fechaInicioStr) : null;
        DateTime? fechaFin = fechaFinStr != null ? DateTime.Parse(fechaFinStr) : null;
        var dto = CrearDto(fechaInicio: fechaInicio, fechaFin: fechaFin);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.PeriodoTexto.Should().Be(esperado);
    }

    [Fact]
    public void MetadatosFechas_ExponePropiedadesFechaInicioYFin()
    {
        // Arrange
        var inicio = new DateTime(2023, 2, 10);
        var fin = new DateTime(2025, 8, 20);
        var dto = CrearDto(fechaInicio: inicio, fechaFin: fin);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.FechaInicio.Should().Be(inicio);
        vm.FechaFin.Should().Be(fin);
        vm.Fecha.Should().Be(inicio);
        vm.AnioFin.Should().Be(2025);
        vm.HasFechaInicio.Should().BeTrue();
        vm.HasFechaFin.Should().BeTrue();
        vm.FechaTexto.Should().Be("10/02/2023");
        vm.FechaFinTexto.Should().Be("20/08/2025");
    }

    [Fact]
    public void ActoresYGobernanza_CuandoAmbosPresentes_ExponePropiedadesYFlagsActivos()
    {
        // Arrange
        var dto = CrearDto(
            entidades: "UIS, Gobernación de Santander, IGAC",
            representante: "Ing. Carlos Mendoza (cmendoza@uis.edu.co)"
        );

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.Entidades.Should().Be("UIS, Gobernación de Santander, IGAC");
        vm.Representante.Should().Be("Ing. Carlos Mendoza (cmendoza@uis.edu.co)");
        vm.HasEntidades.Should().BeTrue();
        vm.HasRepresentante.Should().BeTrue();
        vm.HasActores.Should().BeTrue();
    }

    [Fact]
    public void ActoresYGobernanza_CuandoSoloEntidades_HasActoresEsTrueYRepresentanteEsFalse()
    {
        // Arrange
        var dto = CrearDto(entidades: "Alcaldía de Bucaramanga");

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.HasEntidades.Should().BeTrue();
        vm.HasRepresentante.Should().BeFalse();
        vm.HasActores.Should().BeTrue();
    }

    [Fact]
    public void ActoresYGobernanza_CuandoVacios_HasActoresEsFalse()
    {
        // Arrange
        var dto = CrearDto(entidades: "   ", representante: null);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.HasEntidades.Should().BeFalse();
        vm.HasRepresentante.Should().BeFalse();
        vm.HasActores.Should().BeFalse();
    }

    [Fact]
    public async Task CargarFormatosArchivosAsync_ConDirectorioExistente_CargaCategoriasDetectadas()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTest_FormatosVM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "cuenca.shp"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "ortofoto.tif"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "informe.pdf"), "dummy");

            var dto = CrearDto(rutaArchivos: tempDir);
            var service = new ProyectoArchivosService();
            var vm = new FichaProyectoViewModel(
                proyecto: dto,
                volverAction: () => { },
                archivosService: service
            );

            // Act
            await vm.CargarFormatosArchivosAsync();

            // Assert
            vm.HasFormatosDetectados.Should().BeTrue();
            vm.HasExtensionesDetectadas.Should().BeTrue();
            vm.TotalArchivosDetectados.Should().Be(3);
            vm.FormatosDetectados.Should().Contain(c => c != null && c.Categoria == "Vectorial" && c.CantidadArchivos == 1);
            vm.FormatosDetectados.Should().Contain(c => c != null && c.Categoria == "Raster / Ortofoto" && c.CantidadArchivos == 1);
            vm.FormatosDetectados.Should().Contain(c => c != null && c.Categoria == "Documentación" && c.CantidadArchivos == 1);

            // Validar extensiones individuales
            vm.ExtensionesDetectadas.Should().Contain(e => e != null && e.Extension == ".shp" && e.CantidadArchivos == 1);
            vm.ExtensionesDetectadas.Should().Contain(e => e != null && e.Extension == ".tif" && e.CantidadArchivos == 1);
            vm.ExtensionesDetectadas.Should().Contain(e => e != null && e.Extension == ".pdf" && e.CantidadArchivos == 1);
            vm.ResumenInsumosTexto.Should().Contain(".SHP (1)");
            vm.ResumenInsumosTexto.Should().Contain(".TIF (1)");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void FiltrarPorExtensionCommand_DebeDispararEventoConExtensionSeleccionada()
    {
        // Arrange
        var dto = CrearDto();
        var vm = new FichaProyectoViewModel(dto, () => { });
        string? extensionDisparada = null;
        vm.ExtensionSeleccionadaParaFiltrado += (sender, ext) => extensionDisparada = ext;

        // Act
        vm.FiltrarPorExtensionCommand.Execute(".tif");

        // Assert
        extensionDisparada.Should().Be(".tif");
    }

    [Fact]
    public async Task CargarFormatosArchivosAsync_SinRutaArchivos_NoRealizaEscaneo()
    {
        // Arrange
        var dto = CrearDto(rutaArchivos: null);
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act
        await vm.CargarFormatosArchivosAsync();

        // Assert
        vm.HasFormatosDetectados.Should().BeFalse();
        vm.FormatosDetectados.Should().BeEmpty();
        vm.TotalArchivosDetectados.Should().Be(0);
        vm.CargandoFormatos.Should().BeFalse();
    }
}
