using Geomatica.Domain.Entities;
using Geomatica.Domain.ValueObjects;

namespace Geomatica.Domain.Repositories;

public interface IProyectoRepository
{
    Task<IReadOnlyList<Proyecto>> BuscarPorAOIAsync(Aoi aoi, DateTime? desde, DateTime? hasta, string? kw);
    Task<IReadOnlyList<Proyecto>> BuscarPorCodigosAsync(string? codDpto, string? codMpio,
                                                        DateTime? desde, DateTime? hasta, string? kw);
    // CRUD mínimos si los necesitas:
    Task<int> CrearAsync(Proyecto nuevo);
    Task<Proyecto?> ObtenerAsync(int id);
    Task<bool> EliminarAsync(int id);
}