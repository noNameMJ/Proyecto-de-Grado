using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class RegistrarAuditoriaArchivoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public RegistrarAuditoriaArchivoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task EjecutarAsync(
        int idProyecto,
        string rutaRelativa,
        string nombreArchivo,
        string accion,
        string? detalles,
        long? tamanoBytes = null,
        string? usuario = null,
        string? equipo = null,
        DateTime? fechaArchivo = null,
        CancellationToken ct = default)
    {
        if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativa))
            return Task.CompletedTask;

        return _proyectoRepository.RegistrarAuditoriaArchivoAsync(
            idProyecto,
            rutaRelativa,
            nombreArchivo,
            accion,
            detalles,
            tamanoBytes,
            usuario,
            equipo,
            fechaArchivo,
            ct);
    }
}

