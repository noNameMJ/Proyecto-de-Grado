using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Npgsql;
using NpgsqlTypes;
using System.Text;
using System.Diagnostics;

namespace Geomatica.Data.Repositories
{
    public interface IProyectoRepository
    {
        Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null);
        Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto);
    }

    public sealed record ProyectoDto(int Id, string Titulo, double Lon, double Lat, string? RutaArchivos);

    public sealed class ProyectoRepository : IProyectoRepository
    {
        private readonly string _cn;
        public ProyectoRepository(string connectionString) => _cn = connectionString;

        public async Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null)
        {
            const string sql = @"
                SELECT p.id_proyecto, p.titulo,
                       ST_X(p.geom) AS lon, ST_Y(p.geom) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                WHERE (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                  AND (
                      @area IS NULL
                      OR ST_Intersects(
                          p.geom,
                          ST_SetSRID(ST_GeomFromGeoJSON(@area),4686)
                      )
                  )
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            var builder = new NpgsqlConnectionStringBuilder(_cn);
            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

            using var con = new NpgsqlConnection(_cn);
            try
            {
                await con.OpenAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error abriendo conexión: {ex}");
                Debug.WriteLine($"[ProyectoRepository] Connection target: Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");
                throw;
            }

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@desde", (object?)desde ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hasta", (object?)hasta ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kw", (object?)keyword ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@area", (object?)areaJson ?? DBNull.Value);

            try
            {
                var list = new List<ProyectoDto>();
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    list.Add(new ProyectoDto(
                        rd.GetInt32(0),
                        rd.GetString(1),
                        rd.IsDBNull(2) ?0 : rd.GetDouble(2),
                        rd.IsDBNull(3) ?0 : rd.GetDouble(3),
                        rd.IsDBNull(4) ? null : rd.GetString(4)
                    ));
                }
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error ejecutando consulta: {ex}");
                Debug.WriteLine($"[ProyectoRepository] SQL: {cmd.CommandText}");
                if (cmd.Parameters != null && cmd.Parameters.Count >0)
                {
                    foreach (NpgsqlParameter p in cmd.Parameters)
                    {
                        Debug.WriteLine($"[ProyectoRepository] Param: {p.ParameterName} = {p.Value}");
                    }
                }
                throw;
            }
        }

        public async Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto)
        {
            if (idsProyecto == null || idsProyecto.Count ==0) return Array.Empty<string>();

            const string sql = @"
                SELECT DISTINCT TRIM(pm.mpio_cdpmp) AS mpio
                FROM geovisor.proyecto_municipio pm
                WHERE pm.id_proyecto = ANY(@ids);";

            var builder = new NpgsqlConnectionStringBuilder(_cn);
            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

            using var con = new NpgsqlConnection(_cn);
            try
            {
                await con.OpenAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error abriendo conexión (ObtenerCodigosMunicipioAsync): {ex}");
                throw;
            }

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add("@ids", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = idsProyecto.ToArray();

            try
            {
                var list = new List<string>();
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) list.Add(rd.GetString(0));
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error ejecutando consulta (ObtenerCodigosMunicipioAsync): {ex}");
                Debug.WriteLine($"[ProyectoRepository] SQL: {cmd.CommandText}");
                throw;
            }
        }
    }
}
