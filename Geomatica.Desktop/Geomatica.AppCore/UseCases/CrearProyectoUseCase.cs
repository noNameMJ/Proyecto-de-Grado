using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class CrearProyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public CrearProyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task EjecutarAsync(
        string titulo,
        string? descripcion,
        DateTime? fechaInicio,
        string? palabraClave,
        string? ruta,
        string? geom,
        string? municipioCodigo,
        string? usuario = null,
        string? equipo = null,
        DateTime? fechaFin = null,
        string? entidades = null,
        string? representante = null)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new ArgumentException("El título del proyecto es obligatorio.", nameof(titulo));

        return _proyectoRepository.InsertarAsync(
            titulo,
            descripcion,
            fechaInicio,
            palabraClave,
            ruta,
            geom,
            municipioCodigo,
            usuario,
            equipo,
            fechaFin,
            entidades,
            representante);
    }
}

