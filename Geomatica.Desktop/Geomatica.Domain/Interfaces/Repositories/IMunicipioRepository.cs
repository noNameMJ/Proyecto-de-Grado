using Geomatica.Domain.Entities;

namespace Geomatica.Domain.Interfaces.Repositories;

public interface IMunicipioRepository
{
    Task<IReadOnlyList<MunicipioGeoJsonDto>> PorCodigosGeoJsonAsync(IReadOnlyList<string> codigos); // mpio_cdpmp
    Task<IReadOnlyList<MunicipioGeoJsonDto>> TodosGeoJsonAsync(int? limit = null); // para carga base

    Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentosAsync();
    Task<IReadOnlyList<MunicipioDto>> ListarTodosMunicipiosAsync();
    Task<IReadOnlyList<MunicipioDto>> ListarMunicipiosPorDepartamentoAsync(string dptoCodigo);
    Task<EnvelopeDto?> ExtentPorDepartamentoAsync(string dptoCcdgo);
    Task<EnvelopeDto?> ExtentPorMunicipiosAsync(IReadOnlyList<string> codigos);
    Task<MunicipioUbicacionDto?> ObtenerPorPuntoAsync(double lon, double lat);
    Task<bool> PuntoEstaEnMunicipioAsync(string municipioCodigo, double lon, double lat);
}

