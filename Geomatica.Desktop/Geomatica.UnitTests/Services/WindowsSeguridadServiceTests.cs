using System.Collections.Generic;
using FluentAssertions;
using Geomatica.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class WindowsSeguridadServiceTests
{
    [Fact]
    public void EsAdministrador_CuandoUsuarioPerteneceAGrupoGeomaticaAdm_RetornaTrue()
    {
        var service = new WindowsSeguridadService(
            configuration: null,
            usuarioActualProvider: () => @"GEOMATICAAD\marbin.arevalo",
            rolChecker: rol => rol == "G4_GEOMATICA_ADM",
            adminLocalChecker: () => false);

        service.EsAdministrador().Should().BeTrue();
        service.PuedeEliminarProyectos().Should().BeTrue();
        service.PuedeEditarProyectos().Should().BeTrue();
        service.ObtenerUsuarioActual().Should().Be(@"GEOMATICAAD\marbin.arevalo");
        service.ObtenerRolesAdministradorUsuario().Should().Contain("G4_GEOMATICA_ADM");
    }

    [Fact]
    public void EsAdministrador_CuandoUsuarioEsUsuarioEstandarSinGrupoAdmin_RetornaFalse()
    {
        var service = new WindowsSeguridadService(
            configuration: null,
            usuarioActualProvider: () => @"GEOMATICAAD\juan.moreno",
            rolChecker: rol => rol == "GEO_7912", // pertenece a grupo de proyecto ordinario
            adminLocalChecker: () => false);

        service.EsAdministrador().Should().BeFalse();
        service.PuedeEliminarProyectos().Should().BeFalse();
        service.PuedeEditarProyectos().Should().BeFalse();
        service.ObtenerRolesAdministradorUsuario().Should().BeEmpty();
    }

    [Fact]
    public void EsAdministrador_CuandoUsuarioEsAdministradorLocalYEstaPermitido_RetornaTrue()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Seguridad:PermitirAdminLocal"] = "true"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var service = new WindowsSeguridadService(
            configuration: config,
            usuarioActualProvider: () => @"LOCAL\Admin",
            rolChecker: _ => false,
            adminLocalChecker: () => true);

        service.EsAdministrador().Should().BeTrue();
        service.PuedeEliminarProyectos().Should().BeTrue();
        service.PuedeEditarProyectos().Should().BeTrue();
        service.ObtenerRolesAdministradorUsuario().Should().Contain("BUILTIN\\Administrators");
    }

    [Fact]
    public void EsAdministrador_CuandoPermitirAdminLocalEsFalse_RetornaFalse()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Seguridad:PermitirAdminLocal"] = "false"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var service = new WindowsSeguridadService(
            configuration: config,
            usuarioActualProvider: () => @"LOCAL\Admin",
            rolChecker: _ => false,
            adminLocalChecker: () => true);

        service.EsAdministrador().Should().BeFalse();
        service.PuedeEliminarProyectos().Should().BeFalse();
        service.PuedeEditarProyectos().Should().BeFalse();
    }

    [Fact]
    public void Constructor_CargaGruposPersonalizadosDesdeConfiguracion()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Seguridad:GruposAdministradores:0"] = "GRUPO_ESPECIAL_ADMIN",
            ["Seguridad:GruposAdministradores:1"] = "SUPERVISORES_GEOMATICA"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var service = new WindowsSeguridadService(
            configuration: config,
            usuarioActualProvider: () => @"GEOMATICAAD\carlos.garcia",
            rolChecker: rol => rol == "SUPERVISORES_GEOMATICA",
            adminLocalChecker: () => false);

        service.ObtenerGruposAdministradoresConfigurados().Should().Contain(new[] { "GRUPO_ESPECIAL_ADMIN", "SUPERVISORES_GEOMATICA" });
        service.EsAdministrador().Should().BeTrue();
        service.PuedeEliminarProyectos().Should().BeTrue();
        service.PuedeEditarProyectos().Should().BeTrue();
    }
}

