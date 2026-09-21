using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.Data.Repositories
{
    public sealed class ProyectoRepository : IProyectoRepository
    {
        private readonly string _cn;
        private readonly string _debugInfo;

        private static string ObtenerUsuarioActual()
        {
            try
            {
                var winIdentity = WindowsIdentity.GetCurrent()?.Name;
                if (!string.IsNullOrWhiteSpace(winIdentity))
                    return winIdentity;
            }
            catch
            {
                // Fallback si WindowsIdentity no está disponible en el entorno
            }
            return Environment.UserName;
        }

        public ProyectoRepository(string connectionString)
        {
            _cn = connectionString;
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            _debugInfo = $"Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}";
        }

        public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarAsync(
            string? texto,
            DateTime? desde,
            DateTime? hasta,
            string? dptoCodigo = null,
            string? mpioCodigo = null,
            double? minX = null,
            double? minY = null,
            double? maxX = null,
            double? maxY = null,
            CancellationToken ct = default)
        {
            if (minX.HasValue || minY.HasValue || maxX.HasValue || maxY.HasValue)
            {
                if (!minX.HasValue || !minY.HasValue || !maxX.HasValue || !maxY.HasValue)
                    throw new ArgumentException("El filtro espacial requiere los cuatro límites del envelope.");
            }

            const string sql = @"
                SELECT p.id_proyecto, p.titulo, p.fecha, p.palabra_clave, p.ruta_archivos,
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       ST_XMin(Box3D(ST_Transform(p.geom, 4326))) AS min_x,
                       ST_YMin(Box3D(ST_Transform(p.geom, 4326))) AS min_y,
                       ST_XMax(Box3D(ST_Transform(p.geom, 4326))) AS max_x,
                       ST_YMax(Box3D(ST_Transform(p.geom, 4326))) AS max_y
                FROM geovisor.proyecto p
                LEFT JOIN geovisor.proyecto_municipio pm ON pm.id_proyecto = p.id_proyecto
                LEFT JOIN geovisor.municipio m ON m.mpio_cdpmp = pm.mpio_cdpmp
                WHERE p.geom IS NOT NULL
                  AND (@desde IS NULL OR p.fecha >= @desde)
                  AND (@hasta IS NULL OR p.fecha <= @hasta)
                  AND (@texto IS NULL OR p.palabra_clave ILIKE '%' || @texto || '%')
                  AND (@dpto IS NULL OR m.dpto_ccdgo = @dpto)
                  AND (@mpio IS NULL OR pm.mpio_cdpmp = @mpio)
                  AND (
                      @area IS NULL
                      OR ST_Intersects(
                          ST_Transform(p.geom, 4326),
                          ST_SetSRID(ST_GeomFromGeoJSON(CAST(@area AS text)), 4326)
                      )
                  )
                GROUP BY p.id_proyecto, p.titulo, p.fecha, p.palabra_clave, p.ruta_archivos, p.geom
                ORDER BY p.fecha NULLS LAST, p.id_proyecto;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync(ct);
            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add(new NpgsqlParameter("@desde", NpgsqlDbType.Timestamp) { Value = (object?)desde ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@hasta", NpgsqlDbType.Timestamp) { Value = (object?)hasta ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@texto", NpgsqlDbType.Text) { Value = (object?)texto ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@dpto", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(dptoCodigo) ? DBNull.Value : (object)dptoCodigo });
            cmd.Parameters.Add(new NpgsqlParameter("@mpio", NpgsqlDbType.Text) { Value = string.IsNullOrWhiteSpace(mpioCodigo) ? DBNull.Value : (object)mpioCodigo });
            cmd.Parameters.Add(new NpgsqlParameter("@area", NpgsqlDbType.Text) { Value = (object?)CrearEnvelopeGeoJson(minX, minY, maxX, maxY) ?? DBNull.Value });

            var proyectos = new List<ProyectoGeomatico>();
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                proyectos.Add(new ProyectoGeomatico
                {
                    Id = reader.GetInt32(0),
                    Titulo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Fecha = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                    PalabrasClave = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    RutaArchivos = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Longitud = reader.IsDBNull(5) ? 0d : reader.GetDouble(5),
                    Latitud = reader.IsDBNull(6) ? 0d : reader.GetDouble(6),
                    MinX = reader.IsDBNull(7) ? 0d : reader.GetDouble(7),
                    MinY = reader.IsDBNull(8) ? 0d : reader.GetDouble(8),
                    MaxX = reader.IsDBNull(9) ? 0d : reader.GetDouble(9),
                    MaxY = reader.IsDBNull(10) ? 0d : reader.GetDouble(10)
                });
            }

            return proyectos;
        }

        private static string? CrearEnvelopeGeoJson(double? minX, double? minY, double? maxX, double? maxY)
        {
            if (!minX.HasValue) return null;

            var x1 = minX.Value.ToString(CultureInfo.InvariantCulture);
            var y1 = minY!.Value.ToString(CultureInfo.InvariantCulture);
            var x2 = maxX!.Value.ToString(CultureInfo.InvariantCulture);
            var y2 = maxY!.Value.ToString(CultureInfo.InvariantCulture);
            return "{\"type\":\"Polygon\",\"coordinates\":[[[" + x1 + "," + y1 + "],[" + x2 + "," + y1 + "],[" + x2 + "," + y2 + "],[" + x1 + "," + y2 + "],[" + x1 + "," + y1 + "]]]}";
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

        public async Task InsertarAsync(string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, int? anioFin = null, string? entidades = null, string? representante = null)
        {
            // 1. Insertar proyecto
            // Using RETURNING id_proyecto to get the generated ID.
            var sqlProp = @"
                INSERT INTO geovisor.proyecto (titulo, descripcion, fecha, palabra_clave, ruta_archivos, geom, anio_fin, entidades, representante)
                VALUES (@titulo, @desc, @fecha, @kw, @ruta, 
                        CASE WHEN @geom IS NOT NULL 
                             THEN ST_GeomFromText(@geom, 4686) 
                             ELSE NULL END,
                        @anioFin, @entidades, @rep)
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
                    cmd.Parameters.AddWithValue("@anioFin", (object?)anioFin ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@entidades", (object?)entidades ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rep", (object?)representante ?? DBNull.Value);

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

                // 3. Registrar auditoría de creación
                string userAudit = !string.IsNullOrWhiteSpace(usuario) ? usuario : ObtenerUsuarioActual();
                string machineAudit = !string.IsNullOrWhiteSpace(equipo) ? equipo : Environment.MachineName;
                string detallesAudit = $"Proyecto creado en el sistema. Ubicación: {(string.IsNullOrEmpty(municipioCodigo) ? "Sin municipio asignado" : municipioCodigo)}.";

                const string sqlAudit = @"
                    INSERT INTO geovisor.auditoria_proyecto (id_proyecto, titulo_proyecto, accion, usuario, equipo, fecha_hora, detalles)
                    VALUES (@id, @titulo, 'CREACION', @user, @machine, NOW(), @detalles);";

                try
                {
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", newId);
                    cmdAudit.Parameters.AddWithValue("@titulo", titulo);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    cmdAudit.Parameters.AddWithValue("@detalles", (object?)detallesAudit ?? DBNull.Value);
                    await cmdAudit.ExecuteNonQueryAsync();
                }
                catch (Exception exAudit)
                {
                    Debug.WriteLine($"[ProyectoRepository] Advertencia al registrar auditoría en inserción: {exAudit.Message}");
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
                       m.mpio_cnmbr,
                       p.anio_fin,
                       p.entidades,
                       p.representante
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
                    rd.IsDBNull(9) ? null : rd.GetString(9),
                    rd.IsDBNull(10) ? null : rd.GetInt32(10),
                    rd.IsDBNull(11) ? null : rd.GetString(11),
                    rd.IsDBNull(12) ? null : rd.GetString(12)
                );
            }
            return null;
        }

        public async Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime fecha, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, int? anioFin = null, string? entidades = null, string? representante = null)
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
                                ELSE geom END,
                    anio_fin = @anioFin,
                    entidades = @entidades,
                    representante = @rep
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
                    cmd.Parameters.AddWithValue("@anioFin", (object?)anioFin ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@entidades", (object?)entidades ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@rep", (object?)representante ?? DBNull.Value);
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

                // 3. Registrar auditoría de modificación
                string userAudit = !string.IsNullOrWhiteSpace(usuario) ? usuario : ObtenerUsuarioActual();
                string machineAudit = !string.IsNullOrWhiteSpace(equipo) ? equipo : Environment.MachineName;
                string detallesAudit = $"Proyecto actualizado. Ubicación: {(string.IsNullOrEmpty(municipioCodigo) ? "Sin cambios" : municipioCodigo)}.";

                const string sqlAudit = @"
                    INSERT INTO geovisor.auditoria_proyecto (id_proyecto, titulo_proyecto, accion, usuario, equipo, fecha_hora, detalles)
                    VALUES (@id, @titulo, 'MODIFICACION', @user, @machine, NOW(), @detalles);";

                try
                {
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", idProyecto);
                    cmdAudit.Parameters.AddWithValue("@titulo", titulo);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    cmdAudit.Parameters.AddWithValue("@detalles", (object?)detallesAudit ?? DBNull.Value);
                    await cmdAudit.ExecuteNonQueryAsync();
                }
                catch (Exception exAudit)
                {
                    Debug.WriteLine($"[ProyectoRepository] Advertencia al registrar auditoría en actualización: {exAudit.Message}");
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

        public Task EliminarAsync(int idProyecto, CancellationToken ct = default)
            => EliminarAsync(idProyecto, null, null, ct);

        public async Task EliminarAsync(int idProyecto, string? usuario, string? equipo = null, CancellationToken ct = default)
        {
            const string sqlSelectTitle = "SELECT titulo FROM geovisor.proyecto WHERE id_proyecto = @id LIMIT 1;";
            const string sqlDeleteRel = "DELETE FROM geovisor.proyecto_municipio WHERE id_proyecto = @id;";
            const string sqlDeleteProj = "DELETE FROM geovisor.proyecto WHERE id_proyecto = @id;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync(ct);
            using var tran = await con.BeginTransactionAsync(ct);

            try
            {
                // 1. Obtener título para preservarlo en el registro histórico
                string tituloProyecto = $"#PROY-{idProyecto:D4}";
                using (var cmdTitle = new NpgsqlCommand(sqlSelectTitle, con, tran))
                {
                    cmdTitle.Parameters.AddWithValue("@id", idProyecto);
                    var tObj = await cmdTitle.ExecuteScalarAsync(ct);
                    if (tObj != null && tObj != DBNull.Value)
                    {
                        tituloProyecto = Convert.ToString(tObj) ?? tituloProyecto;
                    }
                }

                // 2. Registrar auditoría de eliminación antes de borrar el proyecto
                string userAudit = !string.IsNullOrWhiteSpace(usuario) ? usuario : ObtenerUsuarioActual();
                string machineAudit = !string.IsNullOrWhiteSpace(equipo) ? equipo : Environment.MachineName;

                const string sqlAudit = @"
                    INSERT INTO geovisor.auditoria_proyecto (id_proyecto, titulo_proyecto, accion, usuario, equipo, fecha_hora, detalles)
                    VALUES (@id, @titulo, 'ELIMINACION', @user, @machine, NOW(), 'Proyecto eliminado del sistema.');";

                try
                {
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", idProyecto);
                    cmdAudit.Parameters.AddWithValue("@titulo", tituloProyecto);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    await cmdAudit.ExecuteNonQueryAsync(ct);
                }
                catch (Exception exAudit)
                {
                    Debug.WriteLine($"[ProyectoRepository] Advertencia al registrar auditoría en eliminación: {exAudit.Message}");
                }

                // 3. Borrar relaciones y proyecto
                using (var cmdRel = new NpgsqlCommand(sqlDeleteRel, con, tran))
                {
                    cmdRel.Parameters.AddWithValue("@id", idProyecto);
                    await cmdRel.ExecuteNonQueryAsync(ct);
                }

                using (var cmdProj = new NpgsqlCommand(sqlDeleteProj, con, tran))
                {
                    cmdProj.Parameters.AddWithValue("@id", idProyecto);
                    var affected = await cmdProj.ExecuteNonQueryAsync(ct);
                    Debug.WriteLine($"[ProyectoRepository] Proyecto {idProyecto} eliminado. Filas afectadas: {affected}");
                }

                await tran.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tran.RollbackAsync(ct);
                Debug.WriteLine($"[ProyectoRepository] Error eliminando proyecto {idProyecto}: {ex}");
                throw;
            }
        }

        public async Task<IReadOnlyList<AuditoriaProyectoDto>> ObtenerHistorialProyectoAsync(int idProyecto, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT id_auditoria, id_proyecto, titulo_proyecto, accion, usuario, equipo, fecha_hora, detalles
                FROM geovisor.auditoria_proyecto
                WHERE id_proyecto = @id
                ORDER BY fecha_hora DESC;";

            var lista = new List<AuditoriaProyectoDto>();
            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@id", idProyecto);

                using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    lista.Add(new AuditoriaProyectoDto(
                        IdAuditoria: rd.GetInt32(0),
                        IdProyecto: rd.IsDBNull(1) ? null : rd.GetInt32(1),
                        TituloProyecto: rd.GetString(2),
                        Accion: rd.GetString(3),
                        Usuario: rd.GetString(4),
                        Equipo: rd.IsDBNull(5) ? null : rd.GetString(5),
                        FechaHora: rd.GetDateTime(6),
                        Detalles: rd.IsDBNull(7) ? null : rd.GetString(7)
                    ));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error obteniendo historial de auditoría: {ex.Message}");
            }
            return lista;
        }

        public async Task AsegurarTablaAuditoriaAsync(CancellationToken ct = default)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS geovisor.auditoria_proyecto (
                    id_auditoria SERIAL PRIMARY KEY,
                    id_proyecto INT,
                    titulo_proyecto VARCHAR(255) NOT NULL,
                    accion VARCHAR(50) NOT NULL,
                    usuario VARCHAR(150) NOT NULL,
                    equipo VARCHAR(100),
                    fecha_hora TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
                    detalles TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_auditoria_id_proyecto ON geovisor.auditoria_proyecto(id_proyecto);
                CREATE INDEX IF NOT EXISTS idx_auditoria_fecha ON geovisor.auditoria_proyecto(fecha_hora DESC);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                await cmd.ExecuteNonQueryAsync(ct);
                Debug.WriteLine("[ProyectoRepository] Tabla geovisor.auditoria_proyecto verificada/creada exitosamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Advertencia al verificar tabla de auditoría: {ex.Message}");
            }
        }

        public async Task AsegurarColumnasProyectoAsync(CancellationToken ct = default)
        {
            const string sql = @"
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS anio_fin INT;
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS entidades VARCHAR(255);
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS representante VARCHAR(255);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                await cmd.ExecuteNonQueryAsync(ct);
                Debug.WriteLine("[ProyectoRepository] Columnas anio_fin, entidades, representante verificadas/creadas exitosamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Advertencia al verificar columnas de proyecto: {ex.Message}");
            }
        }
    }
}
