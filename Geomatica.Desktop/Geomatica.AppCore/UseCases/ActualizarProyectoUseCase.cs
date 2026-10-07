using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class ActualizarProyectoUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ActualizarProyectoUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task EjecutarAsync(
        int idProyecto,
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
        string? representante = null,
        string? sistemaReferencia = null,
        string? formatoDatos = null,
        string? linaje = null)
    {
        if (idProyecto <= 0)
            throw new ArgumentException("El identificador del proyecto debe ser mayor a cero.", nameof(idProyecto));

        if (string.IsNullOrWhiteSpace(titulo))
            throw new ArgumentException("El título del proyecto es obligatorio.", nameof(titulo));

        return _proyectoRepository.ActualizarAsync(
            idProyecto,
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
            representante,
            sistemaReferencia,
            formatoDatos,
            linaje);
    }
}

