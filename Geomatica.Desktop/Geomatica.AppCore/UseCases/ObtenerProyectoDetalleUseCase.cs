using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class ObtenerProyectoDetalleUseCase
{
    private readonly IProyectoRepository _proyectoRepository;

    public ObtenerProyectoDetalleUseCase(IProyectoRepository proyectoRepository)
    {
        _proyectoRepository = proyectoRepository ?? throw new ArgumentNullException(nameof(proyectoRepository));
    }

    public Task<ProyectoDetalleDto?> EjecutarAsync(int idProyecto)
    {
        if (idProyecto <= 0)
            return Task.FromResult<ProyectoDetalleDto?>(null);

        return _proyectoRepository.ObtenerPorIdAsync(idProyecto);
    }
}

