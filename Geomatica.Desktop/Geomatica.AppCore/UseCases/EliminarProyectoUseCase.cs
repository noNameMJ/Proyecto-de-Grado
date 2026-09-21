using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class EliminarProyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public EliminarProyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task EjecutarAsync(int idProyecto, CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            throw new ArgumentException("El identificador del proyecto debe ser mayor a cero.", nameof(idProyecto));

        return _proyectoRepository.EliminarAsync(idProyecto, ct);
    }

    public Task EjecutarAsync(int idProyecto, string? usuario, string? equipo = null, CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            throw new ArgumentException("El identificador del proyecto debe ser mayor a cero.", nameof(idProyecto));

        return _proyectoRepository.EliminarAsync(idProyecto, usuario, equipo, ct);
    }
}

