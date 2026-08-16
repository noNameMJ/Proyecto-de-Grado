using Geomatica.Domain.Entities;

namespace Geomatica.Domain.Interfaces.Repositories;

public interface IProyectoRepository
{
    Task<IReadOnlyList<ProyectoGeomatico>> BuscarAsync(
        string? texto,
        DateTime? desde,
        DateTime? hasta,
        double? minX,
        double? minY,
        double? maxX,
        double? maxY,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null);
    Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto);
    Task<IReadOnlyList<string>> ObtenerTodosCodigosMunicipioAsync();
    Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
    Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
    Task InsertarAsync(string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo);
    Task<ProyectoDetalleDto?> ObtenerPorIdAsync(int idProyecto);
    Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo);
}
