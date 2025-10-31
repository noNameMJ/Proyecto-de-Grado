using Geomatica.Domain.Entities;
using Geomatica.Domain.Repositories;
using Geomatica.Domain.ValueObjects;

namespace Geomatica.AppCore.UseCases;

public sealed class BuscarProyectosUseCase
{
    private readonly IProyectoRepository _repo;
    public BuscarProyectosUseCase(IProyectoRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Proyecto>> PorAOIAsync(string? wkt, DateTime? desde, DateTime? hasta, string? kw)
        => _repo.BuscarPorAOIAsync(Aoi.FromWkt(wkt), desde, hasta, kw);

    public Task<IReadOnlyList<Proyecto>> PorCodigosAsync(string? codDpto, string? codMpio, DateTime? desde, DateTime? hasta, string? kw)
        => _repo.BuscarPorCodigosAsync(codDpto, codMpio, desde, hasta, kw);
}
