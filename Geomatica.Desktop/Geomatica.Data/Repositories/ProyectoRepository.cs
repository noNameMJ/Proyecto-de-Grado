using Geomatica.Domain.Entities;
using Geomatica.Domain.Repositories;
using Geomatica.Domain.ValueObjects;
using Npgsql;

namespace Geomatica.Data.Repositories;

public sealed class ProyectoRepository : IProyectoRepository
{
    private readonly NpgsqlDataSource _ds;
    public ProyectoRepository(NpgsqlDataSource ds) => _ds = ds;

    public async Task<IReadOnlyList<Proyecto>> BuscarPorAOIAsync(Aoi aoi, DateTime? desde, DateTime? hasta, string? kw)
    {
        const string sql = @"
        WITH aoi AS (SELECT CASE WHEN @geo IS NOT NULL THEN ST_SetSRID(ST_GeomFromGeoJSON(@geo), 4686) END g)
        SELECT id_proyecto, titulo, fecha, palabra_clave,
               ST_X(geom) AS lon, ST_Y(geom) AS lat, cod_mpio, cod_dpto
        FROM geovisor.proyecto p, aoi
        WHERE (@wkt IS NULL OR ST_Intersects(p.geom, aoi.g))
          AND (@desde IS NULL OR p.fecha >= @desde)
          AND (@hasta IS NULL OR p.fecha <= @hasta)
          AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
        ORDER BY fecha NULLS LAST;";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@geo", (object?)aoi.Wkt ?? DBNull.Value); // si tu VO se llama Wkt, cambia a Geo
        cmd.Parameters.AddWithValue("@desde", (object?)desde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta", (object?)hasta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kw", (object?)kw ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync();

        var list = new List<Proyecto>();
        while (await rd.ReadAsync())
        {
            list.Add(new Proyecto(
                id: rd.GetInt32(0),
                titulo: rd.GetString(1),
                fecha: rd.IsDBNull(2) ? null : rd.GetDateTime(2),
                palabraClave: rd.IsDBNull(3) ? null : rd.GetString(3),
                codMpio: rd.IsDBNull(6) ? null : rd.GetString(6),
                codDpto: rd.IsDBNull(7) ? null : rd.GetString(7),
                lon: rd.GetDouble(4),
                lat: rd.GetDouble(5)
            ));
        }
        return list;
    }

    public async Task<IReadOnlyList<Proyecto>> BuscarPorCodigosAsync(string? codDpto, string? codMpio, DateTime? desde, DateTime? hasta, string? kw)
    {
        const string sql = @"
        SELECT id_proyecto, titulo, fecha, palabra_clave,
               ST_X(geom), ST_Y(geom), cod_mpio, cod_dpto
        FROM geovisor.proyecto
        WHERE (@dpto IS NULL OR cod_dpto = @dpto)
          AND (@mpio IS NULL OR cod_mpio = @mpio)
          AND (@desde IS NULL OR fecha >= @desde)
          AND (@hasta IS NULL OR fecha <= @hasta)
          AND (@kw IS NULL OR palabra_clave ILIKE '%'||@kw||'%')
        ORDER BY fecha NULLS LAST;";
        await using var cmd = _ds.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@dpto", (object?)codDpto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mpio", (object?)codMpio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desde", (object?)desde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta", (object?)hasta ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@kw", (object?)kw ?? DBNull.Value);
        await using var rd = await cmd.ExecuteReaderAsync();

        var list = new List<Proyecto>();
        while (await rd.ReadAsync())
        {
            list.Add(new Proyecto(
                id: rd.GetInt32(0),
                titulo: rd.GetString(1),
                fecha: rd.IsDBNull(2) ? null : rd.GetDateTime(2),
                palabraClave: rd.IsDBNull(3) ? null : rd.GetString(3),
                codMpio: rd.IsDBNull(6) ? null : rd.GetString(6),
                codDpto: rd.IsDBNull(7) ? null : rd.GetString(7),
                lon: rd.GetDouble(4),
                lat: rd.GetDouble(5)
            ));
        }
        return list;
    }

    // CRUD mínimos (implementa si tu app crea/edita proyectos)
    public Task<int> CrearAsync(Proyecto nuevo) => Task.FromResult(0);
    public Task<Proyecto?> ObtenerAsync(int id) => Task.FromResult<Proyecto?>(null);
    public Task<bool> EliminarAsync(int id) => Task.FromResult(false);
}
