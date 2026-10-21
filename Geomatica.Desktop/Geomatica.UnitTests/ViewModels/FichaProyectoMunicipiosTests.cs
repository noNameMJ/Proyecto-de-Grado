using FluentAssertions;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Entities;
using Xunit;

namespace Geomatica.UnitTests.ViewModels;

public class FichaProyectoMunicipiosTests
{
    private static ProyectoDetalleDto CrearDto(string? municipioNombre, string? municipioCodigo)
    {
        return new ProyectoDetalleDto(
            Id: 99,
            Titulo: "Proyecto Test Multi-municipios",
            Descripcion: "Descripción de prueba",
            FechaInicio: new DateTime(2023, 1, 1),
            PalabraClave: "sig, catastro",
            RutaArchivos: null,
            Lon: -73.1,
            Lat: 7.1,
            MunicipioCodigo: municipioCodigo,
            MunicipioNombre: municipioNombre
        );
    }

    [Fact]
    public void MunicipioUnico_ConfiguraPropiedadesYModoPocosMunicipios()
    {
        // Arrange
        var dto = CrearDto("Bucaramanga (Santander)", "68001");

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.TotalMunicipios.Should().Be(1);
        vm.TotalDepartamentos.Should().Be(1);
        vm.HasPocosMunicipios.Should().BeTrue();
        vm.EsPlurimunicipal.Should().BeFalse();
        vm.HasMultiplesDepartamentos.Should().BeFalse();
        vm.MunicipioChipTexto.Should().Be("Bucaramanga (Santander)");
        vm.MunicipioChipTooltip.Should().Be("Bucaramanga (Santander)");
        vm.MunicipiosTodos.Should().HaveCount(1);
        vm.MunicipiosTodos[0].Codigo.Should().Be("68001");
        vm.MunicipiosTodos[0].Nombre.Should().Be("Bucaramanga");
        vm.MunicipiosTodos[0].Departamento.Should().Be("Santander");
    }

    [Fact]
    public void PocosMunicipios_HastaTres_MuestraNombresEnChip()
    {
        // Arrange
        var dto = CrearDto(
            "Bucaramanga (Santander), Floridablanca (Santander), Girón (Santander)",
            "68001, 68276, 68307"
        );

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.TotalMunicipios.Should().Be(3);
        vm.TotalDepartamentos.Should().Be(1);
        vm.HasPocosMunicipios.Should().BeTrue();
        vm.EsPlurimunicipal.Should().BeFalse();
        vm.MunicipioChipTexto.Should().Be("Bucaramanga, Floridablanca, Girón");
        vm.MunicipiosTodos.Should().HaveCount(3);
        vm.MunicipiosTodos[1].Nombre.Should().Be("Floridablanca");
        vm.MunicipiosTodos[1].Codigo.Should().Be("68276");
    }

    [Fact]
    public void Plurimunicipal_MuchosMunicipios_ActivaModoExplorerYFiltros()
    {
        // Arrange: Generar 10 municipios en 3 departamentos
        var nombres = new List<string>();
        var codigos = new List<string>();

        for (int i = 1; i <= 5; i++)
        {
            nombres.Add($"MpioAntioquia{i} (Antioquia)");
            codigos.Add($"0500{i}");
        }
        for (int i = 1; i <= 3; i++)
        {
            nombres.Add($"MpioSantander{i} (Santander)");
            codigos.Add($"6800{i}");
        }
        for (int i = 1; i <= 2; i++)
        {
            nombres.Add($"MpioBoyaca{i} (Boyacá)");
            codigos.Add($"1500{i}");
        }

        var dto = CrearDto(string.Join(", ", nombres), string.Join(", ", codigos));

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.TotalMunicipios.Should().Be(10);
        vm.TotalDepartamentos.Should().Be(3);
        vm.EsPlurimunicipal.Should().BeTrue();
        vm.HasPocosMunicipios.Should().BeFalse();
        vm.HasMultiplesDepartamentos.Should().BeTrue();
        vm.MunicipioChipTexto.Should().Be("10 municipios (3 dptos.)");
        vm.MunicipioChipTooltip.Should().Contain("10 municipios en 3 departamento(s)");
        vm.MunicipiosFiltrados.Should().HaveCount(10);

        // Departamentos en combo: "Todos los departamentos" + 3 departamentos ordenados por cantidad
        vm.DepartamentosFiltro.Should().HaveCount(4);
        vm.DepartamentosFiltro[0].Nombre.Should().Be("Todos los departamentos");
        vm.DepartamentosFiltro[0].EsTodos.Should().BeTrue();
        vm.DepartamentosFiltro[1].Nombre.Should().Be("Antioquia");
        vm.DepartamentosFiltro[1].Cantidad.Should().Be(5);
    }

    [Fact]
    public void Plurimunicipal_FiltrarPorTexto_ActualizaListaYConteo()
    {
        // Arrange
        var dto = CrearDto(
            "Medellín (Antioquia), Envigado (Antioquia), Bucaramanga (Santander), Floridablanca (Santander)",
            "05001, 05266, 68001, 68276"
        );
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act: Filtrar por "buca"
        vm.FiltroMunicipioTexto = "buca";

        // Assert
        vm.MunicipiosFiltrados.Should().HaveCount(1);
        vm.MunicipiosFiltrados[0].Nombre.Should().Be("Bucaramanga");
        vm.TextoConteoMunicipiosFiltrados.Should().Be("Mostrando 1 de 4 municipios");
        vm.TieneFiltroActivo.Should().BeTrue();
        vm.HasNoMunicipiosFiltrados.Should().BeFalse();
    }

    [Fact]
    public void Plurimunicipal_FiltrarPorDepartamento_FiltraSoloEseDepartamento()
    {
        // Arrange
        var dto = CrearDto(
            "Medellín (Antioquia), Envigado (Antioquia), Bucaramanga (Santander), Floridablanca (Santander)",
            "05001, 05266, 68001, 68276"
        );
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act: Seleccionar departamento Santander
        var dptoSantander = vm.DepartamentosFiltro.First(d => d.Nombre == "Santander");
        vm.FiltroDepartamentoSeleccionado = dptoSantander;

        // Assert
        vm.MunicipiosFiltrados.Should().HaveCount(2);
        vm.MunicipiosFiltrados.Should().OnlyContain(m => m.Departamento == "Santander");
        vm.TieneFiltroActivo.Should().BeTrue();
    }

    [Fact]
    public void Plurimunicipal_LimpiarFiltros_RestableceTodosLosItems()
    {
        // Arrange
        var dto = CrearDto(
            "Medellín (Antioquia), Envigado (Antioquia), Bucaramanga (Santander), Floridablanca (Santander)",
            "05001, 05266, 68001, 68276"
        );
        var vm = new FichaProyectoViewModel(dto, () => { });
        vm.FiltroMunicipioTexto = "Medellín";
        vm.MunicipiosFiltrados.Should().HaveCount(1);

        // Act
        vm.LimpiarFiltrosMunicipioCommand.Execute(null);

        // Assert
        vm.FiltroMunicipioTexto.Should().BeEmpty();
        vm.MunicipiosFiltrados.Should().HaveCount(4);
        vm.TieneFiltroActivo.Should().BeFalse();
    }

    [Fact]
    public void Plurimunicipal_SinResultados_ActivaHasNoMunicipiosFiltrados()
    {
        // Arrange
        var dto = CrearDto(
            "Medellín (Antioquia), Envigado (Antioquia), Bucaramanga (Santander), Floridablanca (Santander)",
            "05001, 05266, 68001, 68276"
        );
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Act
        vm.FiltroMunicipioTexto = "MunicipioInexistenteXYZ";

        // Assert
        vm.MunicipiosFiltrados.Should().BeEmpty();
        vm.HasNoMunicipiosFiltrados.Should().BeTrue();
    }

    [Fact]
    public void SinMunicipios_NoFallaYDejaListasVacias()
    {
        // Arrange
        var dto = CrearDto(null, null);

        // Act
        var vm = new FichaProyectoViewModel(dto, () => { });

        // Assert
        vm.TotalMunicipios.Should().Be(0);
        vm.TotalDepartamentos.Should().Be(0);
        vm.HasMunicipio.Should().BeFalse();
        vm.HasPocosMunicipios.Should().BeFalse();
        vm.EsPlurimunicipal.Should().BeFalse();
        vm.MunicipioChipTexto.Should().BeEmpty();
        vm.MunicipiosTodos.Should().BeEmpty();
        vm.MunicipiosFiltrados.Should().BeEmpty();
    }
}

