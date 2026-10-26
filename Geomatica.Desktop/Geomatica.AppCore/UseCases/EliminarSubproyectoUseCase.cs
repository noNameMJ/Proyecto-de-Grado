using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

/// <summary>
/// Caso de uso para eliminar un subproyecto puntual de un proyecto.
/// </summary>
public sealed class EliminarSubproyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public EliminarSubproyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public async Task EjecutarAsync(int idSubproyecto, CancellationToken ct = default)
    {
        if (idSubproyecto <= 0)
            throw new ArgumentException("El ID de subproyecto debe ser mayor a 0.", nameof(idSubproyecto));

        await _proyectoRepository.EliminarSubproyectoAsync(idSubproyecto, ct);
    }
}

