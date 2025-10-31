using Geomatica.Domain.Entities;
using Geomatica.Domain.Repositories;

namespace Geomatica.AppCore.UseCases;

public sealed class CargarAdministrativosUseCase
{
    private readonly IAdministrativoRepository _repo;
    public CargarAdministrativosUseCase(IAdministrativoRepository repo) => _repo = repo;

    public Task<IReadOnlyList<Departamento>> DepartamentosAsync()
        => _repo.DepartamentosAsync();

    public Task<IReadOnlyList<Municipio>> MunicipiosAsync(string? codDpto = null)
        => _repo.MunicipiosAsync(codDpto);
}
