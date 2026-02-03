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
        Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
        Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
    }

    public sealed record ProyectoDto(int Id, string Titulo, double Lon, double Lat, string? RutaArchivos);

    public sealed class ProyectoRepository : IProyectoRepository
    {
        private readonly string _cn;
        public ProyectoRepository(string connectionString) => _cn = connectionString;

        public async Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null)
        {
            // Use p.geom as-is (assume SRID is correct)
            const string sql = @"
                SELECT p.id_proyecto, p.titulo,
                       ST_X(ST_Centroid(p.geom)) AS lon, ST_Y(ST_Centroid(p.geom)) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                WHERE (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                  AND (
                      @area IS NULL
                      OR ST_Intersects(
                          p.geom,
                          ST_SetSRID(ST_GeomFromGeoJSON(CAST(@area AS text)), ST_SRID(p.geom))
                      )
                  )
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            var builder = new NpgsqlConnectionStringBuilder(_cn);
            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

            using var con = new NpgsqlConnection(_cn);
            try
            {
                await con.OpenAsync();

                // Log SRID of the proyecto.geom column for debugging
                try
                {
                    using var sridCmd = new NpgsqlCommand("SELECT DISTINCT ST_SRID(geom) FROM geovisor.proyecto LIMIT1;", con);
                    var sridObj = await sridCmd.ExecuteScalarAsync();
                    Debug.WriteLine($"[ProyectoRepository] proyecto.geom SRID sample: {sridObj}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProyectoRepository] No se pudo obtener SRID de proyecto.geom: {ex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error abriendo conexión: {ex}");
                Debug.WriteLine($"[ProyectoRepository] Connection target: Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");
                throw;
            }

            using var cmd = new NpgsqlCommand(sql, con);
            // Explicit parameter types to avoid Postgres ambiguity
            cmd.Parameters.Add(new NpgsqlParameter("@desde", NpgsqlDbType.Timestamp) { Value = (object?)desde ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@hasta", NpgsqlDbType.Timestamp) { Value = (object?)hasta ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@kw", NpgsqlDbType.Text) { Value = (object?)keyword ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@area", NpgsqlDbType.Text) { Value = (object?)areaJson ?? DBNull.Value });

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

                Debug.WriteLine($"[ProyectoRepository] Proyectos cargados: {list.Count}");
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

        public async Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null)
        {
            // Use view vw_proyecto_departamento if available to map proyectos to departamentos
            const string sql = @"
                SELECT p.id_proyecto, p.titulo,
                       ST_X(ST_Centroid(p.geom)) AS lon, ST_Y(ST_Centroid(p.geom)) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                JOIN geovisor.vw_proyecto_departamento vpd ON vpd.id_proyecto = p.id_proyecto
                WHERE TRIM(vpd.dpto_ccdgo) = TRIM(@dpto)
                  AND (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            var builder = new NpgsqlConnectionStringBuilder(_cn);
            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username} (ListarPorDepartamento)");

            using var con = new NpgsqlConnection(_cn);
            try
            {
                await con.OpenAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error abriendo conexión (ListarPorDepartamentoAsync): {ex}");
                throw;
            }

            using var cmd = new NpgsqlCommand(sql, con);
            // Explicit parameter types
            cmd.Parameters.Add(new NpgsqlParameter("@dpto", NpgsqlDbType.Text) { Value = dptoCcdgo ?? string.Empty });
            cmd.Parameters.Add(new NpgsqlParameter("@desde", NpgsqlDbType.Timestamp) { Value = (object?)desde ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@hasta", NpgsqlDbType.Timestamp) { Value = (object?)hasta ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@kw", NpgsqlDbType.Text) { Value = (object?)keyword ?? DBNull.Value });

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

                Debug.WriteLine($"[ProyectoRepository] Proyectos por departamento cargados: {list.Count}");
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error ejecutando consulta (ListarPorDepartamentoAsync): {ex}");
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

        public async Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null)
        {
            const string sql = @"
                SELECT p.id_proyecto, p.titulo,
                       ST_X(ST_Centroid(p.geom)) AS lon, ST_Y(ST_Centroid(p.geom)) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                JOIN geovisor.proyecto_municipio pm ON pm.id_proyecto = p.id_proyecto
                WHERE TRIM(pm.mpio_cdpmp) = TRIM(@mpio)
                  AND (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            var builder = new NpgsqlConnectionStringBuilder(_cn);
            
            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add(new NpgsqlParameter("@mpio", NpgsqlDbType.Text) { Value = mpioCcdgo ?? string.Empty });
            cmd.Parameters.Add(new NpgsqlParameter("@desde", NpgsqlDbType.Timestamp) { Value = (object?)desde ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@hasta", NpgsqlDbType.Timestamp) { Value = (object?)hasta ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@kw", NpgsqlDbType.Text) { Value = (object?)keyword ?? DBNull.Value });

            try
            {
                var list = new List<ProyectoDto>();
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    list.Add(new ProyectoDto(
                        rd.GetInt32(0),
                        rd.GetString(1),
                        rd.IsDBNull(2) ? 0 : rd.GetDouble(2),
                        rd.IsDBNull(3) ? 0 : rd.GetDouble(3),
                        rd.IsDBNull(4) ? null : rd.GetString(4)
                    ));
                }
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error ejecutando consulta (ListarPorMunicipioAsync): {ex}");
                Debug.WriteLine($"[ProyectoRepository] SQL: {cmd.CommandText}");
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
