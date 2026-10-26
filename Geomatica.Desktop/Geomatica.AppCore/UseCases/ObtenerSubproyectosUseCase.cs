using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

/// <summary>
/// Caso de uso para obtener los subproyectos o sitios de obra georreferenciados (ej. embarcaderos)
/// vinculados a un proyecto principal.
/// </summary>
public sealed class ObtenerSubproyectosUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ObtenerSubproyectosUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task<IReadOnlyList<SubproyectoDto>> EjecutarAsync(int idProyecto, CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            return Task.FromResult<IReadOnlyList<SubproyectoDto>>(Array.Empty<SubproyectoDto>());

        return _proyectoRepository.ObtenerSubproyectosAsync(idProyecto, ct);
    }
}

