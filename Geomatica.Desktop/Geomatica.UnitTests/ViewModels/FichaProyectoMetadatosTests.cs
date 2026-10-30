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

    [Fact]
    public void SeccionesMetadatos_InicialmenteTodasContraidas()
    {
        // Arrange
        var dto = CrearDto();

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert - Todas las secciones deben iniciar retraídas / contraídas
        vm.Seccion1Expandida.Should().BeFalse();
        vm.Seccion2Expandida.Should().BeFalse();
        vm.Seccion3Expandida.Should().BeFalse();
        vm.Seccion4Expandida.Should().BeFalse();
        vm.Seccion5Expandida.Should().BeFalse();
        vm.Seccion6Expandida.Should().BeFalse();

        vm.ChevronSeccion1.Should().Be("▶");
        vm.ChevronSeccion2.Should().Be("▶");
        vm.ChevronSeccion3.Should().Be("▶");
        vm.ChevronSeccion4.Should().Be("▶");
        vm.ChevronSeccion5.Should().Be("▶");
        vm.ChevronSeccion6.Should().Be("▶");

        vm.TotalSeccionesExpandidas.Should().Be(0);
        vm.AlgunaSeccionExpandida.Should().BeFalse();
        vm.TodasSeccionesExpandidas.Should().BeFalse();
        vm.ResumenSeccionesExpandidasTexto.Should().Be("(todas contraídas)");
    }

    [Fact]
    public void ToggleSecciones_PermiteExpandirYContraerManualmenteCadaSeccion()
    {
        // Arrange
        var dto = CrearDto();
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act 1: Toggle Sección 1 (Identificación)
        vm.ToggleSeccion1Command.Execute(null);
        vm.Seccion1Expandida.Should().BeTrue();
        vm.ChevronSeccion1.Should().Be("▼");
        vm.TotalSeccionesExpandidas.Should().Be(1);
        vm.ResumenSeccionesExpandidasTexto.Should().Be("(1 de 6 expandida)");

        // Act 2: Toggle Sección 5 (Distribución)
        vm.ToggleSeccion5Command.Execute(null);
        vm.Seccion5Expandida.Should().BeTrue();
        vm.ChevronSeccion5.Should().Be("▼");
        vm.TotalSeccionesExpandidas.Should().Be(2);
        vm.ResumenSeccionesExpandidasTexto.Should().Be("(2 de 6 expandidas)");

        // Act 3: Toggle nuevamente Sección 1 -> se contrae
        vm.ToggleSeccion1Command.Execute(null);
        vm.Seccion1Expandida.Should().BeFalse();
        vm.ChevronSeccion1.Should().Be("▶");
        vm.TotalSeccionesExpandidas.Should().Be(1);
    }

    [Fact]
    public void ExpandirYContraerTodasSecciones_ModificaTodasLasSeccionesCorrectamente()
    {
        // Arrange
        var dto = CrearDto();
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act 1: Expandir todas
        vm.ExpandirTodasSeccionesCommand.Execute(null);

        // Assert 1
        vm.Seccion1Expandida.Should().BeTrue();
        vm.Seccion2Expandida.Should().BeTrue();
        vm.Seccion3Expandida.Should().BeTrue();
        vm.Seccion4Expandida.Should().BeTrue();
        vm.Seccion5Expandida.Should().BeTrue();
        vm.Seccion6Expandida.Should().BeTrue();
        vm.TotalSeccionesExpandidas.Should().Be(6);
        vm.TodasSeccionesExpandidas.Should().BeTrue();
        vm.ResumenSeccionesExpandidasTexto.Should().Be("(todas expandidas)");
        vm.ChevronSeccion1.Should().Be("▼");
        vm.ChevronSeccion6.Should().Be("▼");

        // Act 2: Contraer todas
        vm.ContraerTodasSeccionesCommand.Execute(null);

        // Assert 2
        vm.Seccion1Expandida.Should().BeFalse();
        vm.Seccion2Expandida.Should().BeFalse();
        vm.Seccion3Expandida.Should().BeFalse();
        vm.Seccion4Expandida.Should().BeFalse();
        vm.Seccion5Expandida.Should().BeFalse();
        vm.Seccion6Expandida.Should().BeFalse();
        vm.TotalSeccionesExpandidas.Should().Be(0);
        vm.TodasSeccionesExpandidas.Should().BeFalse();
        vm.ResumenSeccionesExpandidasTexto.Should().Be("(todas contraídas)");
        vm.ChevronSeccion1.Should().Be("▶");
        vm.ChevronSeccion6.Should().Be("▶");
    }

    [Fact]
    public async Task CargarFormatosArchivosAsync_AgrupaFormatosPrincipalesYAuxiliares()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTest_GruposVM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Vectorial: 1 shp principal + 3 auxiliares (shx, dbf, prj)
            File.WriteAllText(Path.Combine(tempDir, "cuenca.shp"), "dummy-shp-bytes");
            File.WriteAllText(Path.Combine(tempDir, "cuenca.shx"), "dummy-shx");
            File.WriteAllText(Path.Combine(tempDir, "cuenca.dbf"), "dummy-dbf");
            File.WriteAllText(Path.Combine(tempDir, "cuenca.prj"), "dummy-prj");

            // Raster: 1 tif principal + 1 aux (tfw)
            File.WriteAllText(Path.Combine(tempDir, "ortofoto.tif"), "dummy-tif-large-bytes");
            File.WriteAllText(Path.Combine(tempDir, "ortofoto.tfw"), "dummy-tfw");

            // Documentación: 1 pdf principal
            File.WriteAllText(Path.Combine(tempDir, "informe.pdf"), "dummy-pdf");

            var dto = CrearDto(rutaArchivos: tempDir);
            var service = new ProyectoArchivosService();
            var vm = new FichaProyectoViewModel(proyecto: dto, volverAction: () => { }, archivosService: service);

            // Act
            await vm.CargarFormatosArchivosAsync();

            // Assert
            vm.HasFormatosDetectados.Should().BeTrue();
            vm.TotalArchivosDetectados.Should().Be(7);
            vm.TotalCategoriasDetectadas.Should().Be(3);
            vm.TotalExtensionesPrincipales.Should().Be(3); // .shp, .tif, .pdf
            vm.TotalExtensionesAuxiliares.Should().Be(4); // .shx, .dbf, .prj, .tfw
            vm.TamanoTotalDetectadoBytes.Should().BeGreaterThan(0);
            vm.TamanoTotalDetectadoTexto.Should().NotBe("0 B");

            // Grupos clasificados
            vm.GruposFormatos.Should().HaveCount(3);
            var grupoVectorial = vm.GruposFormatos.FirstOrDefault(g => g.Categoria == "Vectorial");
            grupoVectorial.Should().NotBeNull();
            grupoVectorial!.ExtensionesPrincipales.Should().ContainSingle(e => e.Extension == ".shp");
            grupoVectorial.ExtensionesAuxiliares.Should().HaveCount(3);
            grupoVectorial.TieneExtensionesPrincipales.Should().BeTrue();
            grupoVectorial.TieneExtensionesAuxiliares.Should().BeTrue();
            grupoVectorial.TotalAuxiliaresConteo.Should().Be(3);
            grupoVectorial.TextoToggleAuxiliares.Should().Contain("Ver 3 archivos auxiliares y sidecars");

            // Toggle auxiliares
            grupoVectorial.MostrarAuxiliares.Should().BeFalse();
            grupoVectorial.ToggleAuxiliaresCommand.Execute(null);
            grupoVectorial.MostrarAuxiliares.Should().BeTrue();
            grupoVectorial.TextoToggleAuxiliares.Should().Contain("Ocultar 3 formatos auxiliares");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SeleccionarTabCategoriaCommand_Y_BusquedaFormatoTexto_FiltranCorrectamente()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "GeomaticaTest_FiltroTabsVM_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "red.shp"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "red.dbf"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "mapa.tif"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "doc.pdf"), "dummy");

            var dto = CrearDto(rutaArchivos: tempDir);
            var service = new ProyectoArchivosService();
            var vm = new FichaProyectoViewModel(proyecto: dto, volverAction: () => { }, archivosService: service);
            await vm.CargarFormatosArchivosAsync();

            // Tabs generadas: Todos, Vectorial, Raster / Ortofoto, Documentación
            vm.CategoriasTabs.Should().HaveCount(4);
            vm.CategoriasTabs.First(t => t.EsTodos).IsSelected.Should().BeTrue();
            vm.GruposFormatosFiltrados.Should().HaveCount(3);

            // Act 1: Seleccionar Tab "Vectorial"
            vm.SeleccionarTabCategoriaCommand.Execute("Vectorial");

            // Assert 1
            vm.FiltroCategoriaSeleccionada.Should().Be("Vectorial");
            vm.CategoriasTabs.First(t => t.Nombre == "Vectorial").IsSelected.Should().BeTrue();
            vm.CategoriasTabs.First(t => t.EsTodos).IsSelected.Should().BeFalse();
            vm.GruposFormatosFiltrados.Should().HaveCount(1);
            vm.GruposFormatosFiltrados[0].Categoria.Should().Be("Vectorial");

            // Act 2: Resetear a "Todos"
            vm.SeleccionarTabCategoriaCommand.Execute("Todos");
            vm.GruposFormatosFiltrados.Should().HaveCount(3);

            // Act 3: Buscar por texto "pdf"
            vm.BusquedaFormatoTexto = "pdf";
            vm.HasBusquedaFormatoTexto.Should().BeTrue();
            vm.GruposFormatosFiltrados.Should().HaveCount(1);
            vm.GruposFormatosFiltrados[0].Categoria.Should().Be("Documentación");

            // Act 4: Buscar formato auxiliar "dbf" -> debe auto-expandir MostrarAuxiliares
            vm.BusquedaFormatoTexto = "dbf";
            vm.GruposFormatosFiltrados.Should().HaveCount(1);
            vm.GruposFormatosFiltrados[0].Categoria.Should().Be("Vectorial");
            vm.GruposFormatosFiltrados[0].MostrarAuxiliares.Should().BeTrue();

            // Act 5: Limpiar búsqueda
            vm.LimpiarBusquedaFormatoCommand.Execute(null);
            vm.BusquedaFormatoTexto.Should().BeEmpty();
            vm.HasBusquedaFormatoTexto.Should().BeFalse();
            vm.GruposFormatosFiltrados.Should().HaveCount(3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ExplorarCategoriaCommand_DebeDispararEventoCategoriaSeleccionadaParaFiltrado()
    {
        // Arrange
        var dto = CrearDto();
        var vm = new FichaProyectoViewModel(dto, () => { });
        string? categoriaDisparada = null;
        vm.CategoriaSeleccionadaParaFiltrado += (sender, cat) => categoriaDisparada = cat;

        // Act
        vm.ExplorarCategoriaCommand.Execute("Vectorial");

        // Assert
        categoriaDisparada.Should().Be("Vectorial");
    }
}
