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
    Task InsertarAsync(string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null);
    Task<ProyectoDetalleDto?> ObtenerPorIdAsync(int idProyecto);
    Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null);
    Task EliminarAsync(int idProyecto, CancellationToken ct = default);
    Task EliminarAsync(int idProyecto, string? usuario, string? equipo = null, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaProyectoDto>> ObtenerHistorialProyectoAsync(int idProyecto, CancellationToken ct = default);
    Task AsegurarTablaAuditoriaAsync(CancellationToken ct = default);
    Task AsegurarColumnasProyectoAsync(CancellationToken ct = default);
}
