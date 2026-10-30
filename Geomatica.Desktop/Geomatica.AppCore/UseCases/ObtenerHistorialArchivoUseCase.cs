using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class ObtenerHistorialArchivoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ObtenerHistorialArchivoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task<IReadOnlyList<AuditoriaArchivoDto>> EjecutarAsync(
        int idProyecto,
        string rutaRelativa,
        int limite = 5,
        CancellationToken ct = default)
    {
        if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativa))
            return Task.FromResult<IReadOnlyList<AuditoriaArchivoDto>>(Array.Empty<AuditoriaArchivoDto>());

        return _proyectoRepository.ObtenerHistorialArchivoAsync(idProyecto, rutaRelativa, limite, ct);
    }
}

