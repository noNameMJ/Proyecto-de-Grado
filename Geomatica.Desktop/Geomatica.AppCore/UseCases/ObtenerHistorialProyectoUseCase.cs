using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class ObtenerHistorialProyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ObtenerHistorialProyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task<IReadOnlyList<AuditoriaProyectoDto>> EjecutarAsync(int idProyecto, CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            return Task.FromResult<IReadOnlyList<AuditoriaProyectoDto>>(Array.Empty<AuditoriaProyectoDto>());

        return _proyectoRepository.ObtenerHistorialProyectoAsync(idProyecto, ct);
    }
}

