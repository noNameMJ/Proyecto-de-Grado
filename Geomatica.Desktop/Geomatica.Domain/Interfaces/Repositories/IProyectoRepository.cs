using Geomatica.Domain.Entities;

namespace Geomatica.Domain.Interfaces.Repositories;

public interface IProyectoRepository
{
    Task<IReadOnlyList<ProyectoGeomatico>> BuscarAsync(
        string? texto,
        DateTime? desde,
        DateTime? hasta,
        string? dptoCodigo = null,
        string? mpioCodigo = null,
        double? minX = null,
        double? minY = null,
        double? maxX = null,
        double? maxY = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null);
    Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto);
    Task<IReadOnlyList<string>> ObtenerTodosCodigosMunicipioAsync();
    Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
    Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
    Task<int> InsertarAsync(string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null, string? sistemaReferencia = null, string? formatoDatos = null, string? linaje = null);
    Task<ProyectoDetalleDto?> ObtenerPorIdAsync(int idProyecto);
    Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null, string? sistemaReferencia = null, string? formatoDatos = null, string? linaje = null);
    Task EliminarAsync(int idProyecto, CancellationToken ct = default);
    Task EliminarAsync(int idProyecto, string? usuario, string? equipo = null, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaProyectoDto>> ObtenerHistorialProyectoAsync(int idProyecto, CancellationToken ct = default);
    Task RegistrarAuditoriaAsync(int idProyecto, string tituloProyecto, string accion, string detalles, string? usuario = null, string? equipo = null, CancellationToken ct = default);
    Task AsegurarTablaAuditoriaAsync(CancellationToken ct = default);
    Task AsegurarColumnasProyectoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubproyectoDto>> ObtenerSubproyectosAsync(int idProyecto, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>>> ObtenerSubproyectosPorProyectosAsync(IReadOnlyList<int> idsProyecto, CancellationToken ct = default);
    Task<int> InsertarSubproyectoAsync(int idProyecto, string nombre, string? mpioCdpmp, string subcarpeta, double lon, double lat, string? descripcion = null, CancellationToken ct = default);
    Task ActualizarSubproyectoAsync(int idSubproyecto, string nombre, string? mpioCdpmp, string subcarpeta, double lon, double lat, string? descripcion = null, CancellationToken ct = default);
    Task EliminarSubproyectoAsync(int idSubproyecto, CancellationToken ct = default);
    Task AsegurarTablaSubproyectosAsync(CancellationToken ct = default);

    // Auditoría de Archivos (geovisor.auditoria_archivo)
    Task AsegurarTablaAuditoriaArchivoAsync(CancellationToken ct = default);
    Task RegistrarAuditoriaArchivoAsync(int idProyecto, string rutaRelativa, string nombreArchivo, string accion, string? detalles, long? tamanoBytes = null, string? usuario = null, string? equipo = null, DateTime? fechaArchivo = null, CancellationToken ct = default);
    Task<AuditoriaArchivoDto?> ObtenerUltimoEstadoArchivoAsync(int idProyecto, string rutaRelativa, CancellationToken ct = default);
    Task ReasignarRutaAuditoriaArchivoAsync(int idProyecto, string rutaRelativaAntigua, string rutaRelativaNueva, string nombreArchivoNuevo, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerHistorialArchivoAsync(int idProyecto, string rutaRelativa, int limite = 5, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerHistorialArchivosProyectoAsync(int idProyecto, int limite = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerArchivosEliminadosProyectoAsync(int idProyecto, int limite = 50, CancellationToken ct = default);
    Task<bool> TieneAuditoriaArchivoAsync(int idProyecto, CancellationToken ct = default);
    Task<int> PurgarAuditoriaArchivoPorVejezAsync(int diasRetencion = 7, int minimoRegistros = 5, CancellationToken ct = default);
}
