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
        string? dptoCodigo = null,
        string? mpioCodigo = null,
        double? minX = null,
        double? minY = null,
        double? maxX = null,
        double? maxY = null,
        CancellationToken ct = default)
        => _proyectoRepository.BuscarAsync(texto, desde, hasta, dptoCodigo, mpioCodigo, minX, minY, maxX, maxY, ct);
}
