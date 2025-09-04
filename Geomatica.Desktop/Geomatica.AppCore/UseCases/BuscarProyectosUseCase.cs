using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

public class BuscarProyectosUseCase
{
    private readonly IProyectoRepository _repo;
    public BuscarProyectosUseCase(IProyectoRepository repo) => _repo = repo;

    public Task<IReadOnlyList<ProyectoGeomatico>> EjecutarAsync(
        string? texto, DateTime? desde, DateTime? hasta,
        double? minX, double? minY, double? maxX, double? maxY,
        CancellationToken ct = default)
        => _repo.BuscarAsync(texto, desde, hasta, minX, minY, maxX, maxY, ct);
}
