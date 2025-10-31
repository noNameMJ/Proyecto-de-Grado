using Geomatica.Domain.Entities;
using Geomatica.Domain.Repositories;
using Npgsql;

namespace Geomatica.Data.Repositories;

public sealed class AdministrativoRepository : IAdministrativoRepository
{
    private readonly NpgsqlDataSource _ds;
    public AdministrativoRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<Departamento>> DepartamentosAsync()
    {
        const string sql = @"SELECT dpto_ccdgo, dpto_cnmbre FROM geovisor.departamento ORDER BY dpto_cnmbre;";
        await using var cmd = _ds.CreateCommand(sql);
        await using var rd = await cmd.ExecuteReaderAsync();
        var list = new List<Departamento>();
        while (await rd.ReadAsync())
            list.Add(new Departamento(rd.GetString(0), rd.GetString(1)));
        return list;
    }

    public async Task<IReadOnlyList<Municipio>> MunicipiosAsync(string? codDpto = null)
    {
        const string sql = @"
        SELECT mpio_cdpmp, dpto_ccdgo, mpio_cnmbre
        FROM geovisor.municipio
        WHERE (@dpto IS NULL OR dpto_ccdgo = @dpto)
        ORDER BY mpio_cnmbre;";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@dpto", (object?)codDpto ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync();
        var list = new List<Municipio>();
        while (await rd.ReadAsync())
            list.Add(new Municipio(rd.GetString(0), rd.GetString(1), rd.GetString(2)));
        return list;
    }

    public async Task<Departamento?> ObtenerDepartamentoAsync(string codDpto)
    {
        const string sql = @"SELECT dpto_ccdgo, dpto_cnmbre FROM geovisor.departamento WHERE dpto_ccdgo=@d;";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@d", codDpto);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (await rd.ReadAsync()) return new Departamento(rd.GetString(0), rd.GetString(1));
        return null;
    }

    public async Task<Municipio?> ObtenerMunicipioAsync(string codMpio)
    {
        const string sql = @"SELECT mpio_cdpmp, dpto_ccdgo, mpio_cnmbre FROM geovisor.municipio WHERE mpio_cdpmp=@m;";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@m", codMpio);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (await rd.ReadAsync()) return new Municipio(rd.GetString(0), rd.GetString(1), rd.GetString(2));
        return null;
    }
}
