using Geomatica.Domain.Entities;

namespace Geomatica.Domain.Repositories;

public interface IAdministrativoRepository
{
    Task<IReadOnlyList<Departamento>> DepartamentosAsync();
    Task<IReadOnlyList<Municipio>> MunicipiosAsync(string? codDpto = null);
}