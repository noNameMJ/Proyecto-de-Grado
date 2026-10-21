namespace Geomatica.Desktop.Services;

/// <summary>
/// Contrato para la gestión de seguridad, identidades y roles de usuario basados en Active Directory y Windows.
/// Permite determinar las capacidades administrativas y permisos de eliminación en el sistema.
/// </summary>
public interface ISeguridadService
{
    /// <summary>
    /// Determina si el usuario actual pertenece a alguno de los grupos administrativos configurados en el Directorio Activo (AD) o en el equipo local.
    /// </summary>
    bool EsAdministrador();

    /// <summary>
    /// Determina si el usuario actual cuenta con autorización para eliminar proyectos de la base de datos del sistema.
    /// </summary>
    bool PuedeEliminarProyectos();

    /// <summary>
    /// Obtiene el nombre del usuario actual en formato DOMINIO\usuario o usuario.
    /// </summary>
    string ObtenerUsuarioActual();

    /// <summary>
    /// Obtiene la lista de nombres de grupos administradores configurados reconocidos en el sistema.
    /// </summary>
    IReadOnlyList<string> ObtenerGruposAdministradoresConfigurados();

    /// <summary>
    /// Obtiene la lista de roles administrativos a los que pertenece el usuario actual.
    /// </summary>
    IReadOnlyList<string> ObtenerRolesAdministradorUsuario();
}

