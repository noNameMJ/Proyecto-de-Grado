using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;

namespace Geomatica.Desktop.Services;

/// <summary>
/// Implementación del servicio de seguridad basada en tokens de Windows y grupos de Active Directory (AD).
/// Evalúa pertenencia a grupos administrativos como 'G4_GEOMATICA_ADM' en tiempo de ejecución.
/// </summary>
public class WindowsSeguridadService : ISeguridadService
{
    public const string DefaultGrupoGeomaticaAdm = "G4_GEOMATICA_ADM";
    public const string DefaultGrupoAdministradores = "Administradores";
    public const string DefaultGrupoAdminsDominio = "Admins. del dominio";

    private readonly List<string> _gruposAdministradores;
    private readonly bool _permitirAdminLocal;
    private readonly Func<string>? _usuarioActualProvider;
    private readonly Func<string, bool>? _rolChecker;
    private readonly Func<bool>? _adminLocalChecker;

    /// <summary>
    /// Constructor principal para inyección de dependencias en tiempo de ejecución.
    /// </summary>
    public WindowsSeguridadService(IConfiguration? configuration = null)
        : this(configuration, null, null, null)
    {
    }

    /// <summary>
    /// Constructor extendido para pruebas unitarias deterministas y desacoplamiento de entorno.
    /// </summary>
    public WindowsSeguridadService(
        IConfiguration? configuration,
        Func<string>? usuarioActualProvider,
        Func<string, bool>? rolChecker,
        Func<bool>? adminLocalChecker)
    {
        _usuarioActualProvider = usuarioActualProvider;
        _rolChecker = rolChecker;
        _adminLocalChecker = adminLocalChecker;

        var seccionGrupos = configuration?.GetSection("Seguridad:GruposAdministradores");
        var gruposConfigurados = seccionGrupos?.GetChildren()
            .Select(c => c.Value)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (gruposConfigurados != null && gruposConfigurados.Count > 0)
        {
            _gruposAdministradores = gruposConfigurados;
        }
        else
        {
            _gruposAdministradores = new List<string>
            {
                DefaultGrupoGeomaticaAdm,
                DefaultGrupoAdministradores,
                DefaultGrupoAdminsDominio
            };
        }

        var permitirLocal = configuration?["Seguridad:PermitirAdminLocal"];
        _permitirAdminLocal = string.IsNullOrWhiteSpace(permitirLocal) || (bool.TryParse(permitirLocal, out var val) && val);
    }

    public string ObtenerUsuarioActual()
    {
        if (_usuarioActualProvider != null)
        {
            return _usuarioActualProvider();
        }

        try
        {
            var identity = WindowsIdentity.GetCurrent();
            return identity?.Name ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
        catch
        {
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
    }

    public IReadOnlyList<string> ObtenerGruposAdministradoresConfigurados()
    {
        return _gruposAdministradores.AsReadOnly();
    }

    public bool EsAdministrador()
    {
        try
        {
            // 1. Evaluar si pertenece a alguno de los grupos administrativos configurados
            foreach (var grupo in _gruposAdministradores)
            {
                if (EstaEnRol(grupo))
                    return true;
            }

            // 2. Evaluar rol de Administrador local integrado si está permitido
            if (_permitirAdminLocal)
            {
                if (_adminLocalChecker != null)
                {
                    if (_adminLocalChecker()) return true;
                }
                else
                {
                    var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                    if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public bool PuedeEliminarProyectos()
    {
        return EsAdministrador();
    }

    public bool PuedeEditarProyectos()
    {
        return EsAdministrador();
    }

    public IReadOnlyList<string> ObtenerRolesAdministradorUsuario()
    {
        var rolesActivos = new List<string>();
        try
        {
            foreach (var grupo in _gruposAdministradores)
            {
                if (EstaEnRol(grupo))
                {
                    rolesActivos.Add(grupo);
                }
            }

            if (_permitirAdminLocal)
            {
                bool esAdminLocal = _adminLocalChecker != null
                    ? _adminLocalChecker()
                    : new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

                if (esAdminLocal)
                {
                    rolesActivos.Add("BUILTIN\\Administrators");
                }
            }
        }
        catch
        {
            // Proteger contra fallos nativos
        }

        return rolesActivos;
    }

    private bool EstaEnRol(string nombreGrupo)
    {
        if (string.IsNullOrWhiteSpace(nombreGrupo)) return false;

        if (_rolChecker != null)
        {
            return _rolChecker(nombreGrupo);
        }

        try
        {
            var identity = WindowsIdentity.GetCurrent();
            if (identity == null) return false;

            var principal = new WindowsPrincipal(identity);

            // Comprobación directa con IsInRole (soporta nombre simple y DOMAIN\Grupo)
            if (principal.IsInRole(nombreGrupo))
                return true;

            // Si se especificó sin dominio, intentar también con el dominio del usuario actual
            if (!nombreGrupo.Contains('\\') && !string.IsNullOrWhiteSpace(Environment.UserDomainName))
            {
                var grupoConDominio = $"{Environment.UserDomainName}\\{nombreGrupo}";
                if (principal.IsInRole(grupoConDominio))
                    return true;
            }

            // Fallback por grupos en el token si existiera diferencia de formato
            if (identity.Groups != null)
            {
                foreach (var groupSid in identity.Groups)
                {
                    try
                    {
                        var ntAccount = groupSid.Translate(typeof(NTAccount)).Value;
                        if (string.Equals(ntAccount, nombreGrupo, StringComparison.OrdinalIgnoreCase) ||
                            ntAccount.EndsWith($"\\{nombreGrupo}", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // SIDs huérfanos pueden disparar IdentityNotMappedException; ignorar
                    }
                }
            }
        }
        catch
        {
            // Protección contra excepciones de token
        }

        return false;
    }
}
