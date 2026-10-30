using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class ObtenerHistorialArchivosProyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ObtenerHistorialArchivosProyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task<IReadOnlyList<AuditoriaArchivoDto>> EjecutarAsync(
        int idProyecto,
        int limite = 50,
        CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            return Task.FromResult<IReadOnlyList<AuditoriaArchivoDto>>(Array.Empty<AuditoriaArchivoDto>());

        return _proyectoRepository.ObtenerHistorialArchivosProyectoAsync(idProyecto, limite, ct);
    }
}

