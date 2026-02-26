using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Geomatica.Data.Repositories
{
    public interface IProyectoRepository
    {
        Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null);
        Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto);
        Task<IReadOnlyList<string>> ObtenerTodosCodigosMunicipioAsync();
        Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
        Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null);
        Task InsertarAsync(string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo);
        Task<ProyectoDetalleDto?> ObtenerPorIdAsync(int idProyecto);
        Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo);
    }

    public sealed record ProyectoDto(int Id, string Titulo, double Lon, double Lat, string? RutaArchivos);
    public sealed record ProyectoDetalleDto(int Id, string Titulo, string? Descripcion, DateTime? Fecha, string? PalabraClave, string? RutaArchivos, double Lon, double Lat, string? MunicipioCodigo, string? MunicipioNombre);

    public sealed class ProyectoRepository : IProyectoRepository
    {
        private readonly string _cn;
        private readonly string _debugInfo;
        public ProyectoRepository(string connectionString)
        {
            _cn = connectionString;
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            _debugInfo = $"Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}";
        }

        public async Task<IReadOnlyList<ProyectoDto>> ListarAsync(DateTime? desde = null, DateTime? hasta = null, string? keyword = null, string? areaJson = null)
        {
            const string sql = @"
                SELECT p.id_proyecto, p.titulo,
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                WHERE p.geom IS NOT NULL
                  AND (@desde IS NULL OR p.fecha >= @desde)
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

            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres {_debugInfo}");

            using var con = new NpgsqlConnection(_cn);
            try
            {
                await con.OpenAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error abriendo conexión: {ex}");
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
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                JOIN geovisor.vw_proyecto_departamento vpd ON vpd.id_proyecto = p.id_proyecto
                WHERE p.geom IS NOT NULL
                  AND vpd.dpto_ccdgo = @dpto
                  AND (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres {_debugInfo} (ListarPorDepartamento)");

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
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       p.ruta_archivos
                FROM geovisor.proyecto p
                JOIN geovisor.proyecto_municipio pm ON pm.id_proyecto = p.id_proyecto
                WHERE p.geom IS NOT NULL
                  AND pm.mpio_cdpmp = @mpio
                  AND (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@kw IS NULL OR p.palabra_clave ILIKE '%'||@kw||'%')
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

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

        public async Task InsertarAsync(string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo)
        {
            // 1. Insertar proyecto
            // Using RETURNING id_proyecto to get the generated ID.
            var sqlProp = @"
                INSERT INTO geovisor.proyecto (titulo, descripcion, fecha, palabra_clave, ruta_archivos, geom)
                VALUES (@titulo, @desc, @fecha, @kw, @ruta, 
                        CASE WHEN @geom IS NOT NULL 
                             THEN ST_GeomFromText(@geom, 4686) 
                             ELSE NULL END)
                RETURNING id_proyecto;";


            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();
            using var tran = await con.BeginTransactionAsync();

            try
            {
                int newId;
                using (var cmd = new NpgsqlCommand(sqlProp, con, tran))
                {
                    cmd.Parameters.AddWithValue("@titulo", titulo);
                    cmd.Parameters.AddWithValue("@desc", (object?)descripcion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fecha", fecha);
                    cmd.Parameters.AddWithValue("@kw", (object?)palabraClave ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ruta", (object?)ruta ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@geom", (object?)geom ?? DBNull.Value);

                    var newIdObj = await cmd.ExecuteScalarAsync();
                    newId = Convert.ToInt32(newIdObj);
                    Debug.WriteLine($"[ProyectoRepository] Proyecto insertado con ID: {newId}");
                }

                // 2. Insertar relación con municipio
                if (!string.IsNullOrEmpty(municipioCodigo))
                {
                     var sqlRel = @"
                        INSERT INTO geovisor.proyecto_municipio (id_proyecto, mpio_cdpmp)
                        VALUES (@id, @mun);";
                     using var cmdRel = new NpgsqlCommand(sqlRel, con, tran);
                     cmdRel.Parameters.AddWithValue("@id", newId);
                     cmdRel.Parameters.AddWithValue("@mun", municipioCodigo);
                     await cmdRel.ExecuteNonQueryAsync();
                }

                await tran.CommitAsync();
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                Debug.WriteLine($"[ProyectoRepository] Error insertando proyecto: {ex}");
                throw;
            }
        }

        public async Task<IReadOnlyList<string>> ObtenerCodigosMunicipioAsync(IReadOnlyList<int> idsProyecto)
        {
            if (idsProyecto == null || idsProyecto.Count ==0) return Array.Empty<string>();

            const string sql = @"
                SELECT DISTINCT pm.mpio_cdpmp AS mpio
                FROM geovisor.proyecto_municipio pm
                WHERE pm.id_proyecto = ANY(@ids);";

            Debug.WriteLine($"[ProyectoRepository] Conectando a Postgres {_debugInfo}");

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

        public async Task<IReadOnlyList<string>> ObtenerTodosCodigosMunicipioAsync()
        {
            const string sql = @"
                SELECT DISTINCT pm.mpio_cdpmp
                FROM geovisor.proyecto_municipio pm;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();

            using var cmd = new NpgsqlCommand(sql, con);
            var list = new List<string>();
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync()) list.Add(rd.GetString(0));
            return list;
        }

        public async Task<ProyectoDetalleDto?> ObtenerPorIdAsync(int idProyecto)
        {
            const string sql = @"
                SELECT p.id_proyecto, p.titulo, p.descripcion, p.fecha, p.palabra_clave, p.ruta_archivos,
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       pm.mpio_cdpmp,
                       m.mpio_cnmbr
                FROM geovisor.proyecto p
                LEFT JOIN geovisor.proyecto_municipio pm ON pm.id_proyecto = p.id_proyecto
                LEFT JOIN geovisor.municipio m ON m.mpio_cdpmp = pm.mpio_cdpmp
                WHERE p.id_proyecto = @id
                LIMIT 1;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@id", idProyecto);

            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                return new ProyectoDetalleDto(
                    rd.GetInt32(0),
                    rd.GetString(1),
                    rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetDateTime(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? null : rd.GetString(5),
                    rd.IsDBNull(6) ? 0 : rd.GetDouble(6),
                    rd.IsDBNull(7) ? 0 : rd.GetDouble(7),
                    rd.IsDBNull(8) ? null : rd.GetString(8),
                    rd.IsDBNull(9) ? null : rd.GetString(9)
                );
            }
            return null;
        }

        public async Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo)
        {
            const string sqlUpdate = @"
                UPDATE geovisor.proyecto
                SET titulo = @titulo,
                    descripcion = @desc,
                    fecha = @fecha,
                    palabra_clave = @kw,
                    ruta_archivos = @ruta,
                    geom = CASE WHEN @geom IS NOT NULL
                                THEN ST_GeomFromText(@geom, 4686)
                                ELSE geom END
                WHERE id_proyecto = @id;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();
            using var tran = await con.BeginTransactionAsync();

            try
            {
                using (var cmd = new NpgsqlCommand(sqlUpdate, con, tran))
                {
                    cmd.Parameters.AddWithValue("@id", idProyecto);
                    cmd.Parameters.AddWithValue("@titulo", titulo);
                    cmd.Parameters.AddWithValue("@desc", (object?)descripcion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@fecha", fecha);
                    cmd.Parameters.AddWithValue("@kw", (object?)palabraClave ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ruta", (object?)ruta ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@geom", (object?)geom ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync();
                }

                if (!string.IsNullOrEmpty(municipioCodigo))
                {
                    const string sqlDeleteRel = "DELETE FROM geovisor.proyecto_municipio WHERE id_proyecto = @id;";
                    using (var cmdDel = new NpgsqlCommand(sqlDeleteRel, con, tran))
                    {
                        cmdDel.Parameters.AddWithValue("@id", idProyecto);
                        await cmdDel.ExecuteNonQueryAsync();
                    }

                    const string sqlInsertRel = "INSERT INTO geovisor.proyecto_municipio (id_proyecto, mpio_cdpmp) VALUES (@id, @mun);";
                    using (var cmdIns = new NpgsqlCommand(sqlInsertRel, con, tran))
                    {
                        cmdIns.Parameters.AddWithValue("@id", idProyecto);
                        cmdIns.Parameters.AddWithValue("@mun", municipioCodigo);
                        await cmdIns.ExecuteNonQueryAsync();
                    }
                }

                await tran.CommitAsync();
                Debug.WriteLine($"[ProyectoRepository] Proyecto {idProyecto} actualizado.");
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync();
                Debug.WriteLine($"[ProyectoRepository] Error actualizando proyecto {idProyecto}: {ex}");
                throw;
            }
        }
    }
}
