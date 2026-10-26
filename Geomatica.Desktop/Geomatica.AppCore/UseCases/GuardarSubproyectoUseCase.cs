using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

/// <summary>
/// Caso de uso para registrar o actualizar un subproyecto puntual (ej. embarcadero) con coordenadas físicas
/// y subcarpeta dentro del proyecto matriz.
/// </summary>
public sealed class GuardarSubproyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public GuardarSubproyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public async Task<int> EjecutarAsync(
        int idProyecto,
        string nombre,
        string? mpioCdpmp,
        string subcarpeta,
        double lon,
        double lat,
        string? descripcion = null,
        CancellationToken ct = default)
    {
        if (idProyecto <= 0)
            throw new ArgumentException("El ID de proyecto debe ser mayor a 0.", nameof(idProyecto));

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del subproyecto es obligatorio.", nameof(nombre));

        if (string.IsNullOrWhiteSpace(subcarpeta))
            throw new ArgumentException("La subcarpeta del subproyecto es obligatoria.", nameof(subcarpeta));

        // Normalizar subcarpeta (sin diagonales iniciales ni finales, y barras estándar)
        string subcarpetaNormalizada = subcarpeta.Trim().Replace('\\', '/').Trim('/');

        return await _proyectoRepository.InsertarSubproyectoAsync(
            idProyecto,
            nombre.Trim(),
            string.IsNullOrWhiteSpace(mpioCdpmp) ? null : mpioCdpmp.Trim(),
            subcarpetaNormalizada,
            lon,
            lat,
            string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            ct);
    }

    public async Task ActualizarAsync(
        int idSubproyecto,
        string nombre,
        string? mpioCdpmp,
        string subcarpeta,
        double lon,
        double lat,
        string? descripcion = null,
        CancellationToken ct = default)
    {
        if (idSubproyecto <= 0)
            throw new ArgumentException("El ID de subproyecto debe ser mayor a 0.", nameof(idSubproyecto));
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del subproyecto es obligatorio.", nameof(nombre));
        if (string.IsNullOrWhiteSpace(subcarpeta))
            throw new ArgumentException("La subcarpeta del subproyecto es obligatoria.", nameof(subcarpeta));

        string subcarpetaNormalizada = subcarpeta.Trim().Replace('\\', '/').Trim('/');

        await _proyectoRepository.ActualizarSubproyectoAsync(
            idSubproyecto,
            nombre.Trim(),
            string.IsNullOrWhiteSpace(mpioCdpmp) ? null : mpioCdpmp.Trim(),
            subcarpetaNormalizada,
            lon,
            lat,
            string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            ct);
    }

    public async Task ActualizarAsync(
        int idSubproyecto,
        int idProyecto,
        string nombre,
        string? mpioCdpmp,
        string subcarpeta,
        double lon,
        double lat,
        string? descripcion = null,
        CancellationToken ct = default)
    {
        await ActualizarAsync(idSubproyecto, nombre, mpioCdpmp, subcarpeta, lon, lat, descripcion, ct);
    }

    public async Task EjecutarAsync(
        int idSubproyecto,
        int idProyecto,
        string nombre,
        string? mpioCdpmp,
        string subcarpeta,
        double lon,
        double lat,
        string? descripcion = null,
        CancellationToken ct = default)
    {
        await ActualizarAsync(idSubproyecto, nombre, mpioCdpmp, subcarpeta, lon, lat, descripcion, ct);
    }
}
