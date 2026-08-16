using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class BuscarProyectosUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public BuscarProyectosUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository;
    }

    public Task<IReadOnlyList<ProyectoGeomatico>> EjecutarAsync(
        string? texto,
        DateTime? desde,
        DateTime? hasta,
        double? minX,
        double? minY,
        double? maxX,
        double? maxY,
        CancellationToken ct = default)
        => _proyectoRepository.BuscarAsync(texto, desde, hasta, minX, minY, maxX, maxY, ct);
}
