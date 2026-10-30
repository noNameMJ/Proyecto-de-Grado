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
                if (OperatingSystem.IsWindows())
                {
                    var winIdentity = WindowsIdentity.GetCurrent()?.Name;
                    if (!string.IsNullOrWhiteSpace(winIdentity))
                        return winIdentity;
                }
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
                SELECT p.id_proyecto, p.titulo, p.fecha_inicio, p.palabra_clave, p.ruta_archivos,
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
                  AND (@desde IS NULL OR p.fecha_inicio >= @desde)
                  AND (@hasta IS NULL OR p.fecha_inicio <= @hasta)
                  AND (@texto IS NULL OR (p.palabra_clave ILIKE '%' || @texto || '%' OR p.titulo ILIKE '%' || @texto || '%' OR p.formato_datos ILIKE '%' || @texto || '%'))
                  AND (@dpto IS NULL OR m.dpto_ccdgo = @dpto OR pm.mpio_cdpmp LIKE @dpto || '%' OR m.mpio_cdpmp LIKE @dpto || '%')
                  AND (@mpio IS NULL OR pm.mpio_cdpmp = @mpio)
                  AND (
                      @area IS NULL
                      OR ST_Intersects(
                          ST_Transform(p.geom, 4326),
                          ST_SetSRID(ST_GeomFromGeoJSON(CAST(@area AS text)), 4326)
                      )
                      OR (
                          m.geom IS NOT NULL AND ST_Intersects(
                              ST_Transform(m.geom, 4326),
                              ST_SetSRID(ST_GeomFromGeoJSON(CAST(@area AS text)), 4326)
                          )
                      )
                  )
                GROUP BY p.id_proyecto, p.titulo, p.fecha_inicio, p.palabra_clave, p.ruta_archivos, p.geom
                ORDER BY p.fecha_inicio NULLS LAST, p.id_proyecto;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync(ct);
            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add(new NpgsqlParameter("@desde", NpgsqlDbType.Date) { Value = (object?)desde?.Date ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("@hasta", NpgsqlDbType.Date) { Value = (object?)hasta?.Date ?? DBNull.Value });
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
            var items = await BuscarAsync(keyword, desde, hasta);
            return items.Select(p => new ProyectoDto(p.Id, p.Titulo, p.Longitud, p.Latitud, p.RutaArchivos)).ToList();
        }

        public async Task<IReadOnlyList<ProyectoDto>> ListarPorDepartamentoAsync(string dptoCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null)
        {
            var items = await BuscarAsync(keyword, desde, hasta, dptoCodigo: dptoCcdgo);
            return items.Select(p => new ProyectoDto(p.Id, p.Titulo, p.Longitud, p.Latitud, p.RutaArchivos)).ToList();
        }

        public async Task<IReadOnlyList<ProyectoDto>> ListarPorMunicipioAsync(string mpioCcdgo, DateTime? desde = null, DateTime? hasta = null, string? keyword = null)
        {
            var items = await BuscarAsync(keyword, desde, hasta, mpioCodigo: mpioCcdgo);
            return items.Select(p => new ProyectoDto(p.Id, p.Titulo, p.Longitud, p.Latitud, p.RutaArchivos)).ToList();
        }

        public async Task<int> InsertarAsync(string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null, string? sistemaReferencia = null, string? formatoDatos = null, string? linaje = null)
        {
            // 1. Insertar proyecto
            // Using RETURNING id_proyecto to get the generated ID.
            var sqlProp = @"
                INSERT INTO geovisor.proyecto (titulo, descripcion, fecha_inicio, palabra_clave, ruta_archivos, geom, fecha_fin, entidades, representante, sistema_referencia, formato_datos, linaje)
                VALUES (@titulo, @desc, @fechaInicio, @kw, @ruta,
                        CASE WHEN @geom IS NOT NULL
                             THEN ST_GeomFromText(@geom, 4686)
                             ELSE NULL END,
                        @fechaFin, @entidades, @rep, @sisRef, @fmt, @linaje)
                RETURNING id_proyecto;";


            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();
            using var tran = await con.BeginTransactionAsync();

            try
            {
                int newId;
                using (var cmd = new NpgsqlCommand(sqlProp, con, tran))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("@titulo", NpgsqlDbType.Varchar, 200) { Value = titulo });
                    cmd.Parameters.Add(new NpgsqlParameter("@desc", NpgsqlDbType.Text) { Value = (object?)descripcion ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fechaInicio", NpgsqlDbType.Date) { Value = (object?)fechaInicio?.Date ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@kw", NpgsqlDbType.Text) { Value = (object?)palabraClave ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@ruta", NpgsqlDbType.Text) { Value = (object?)ruta ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@geom", NpgsqlDbType.Text) { Value = (object?)geom ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fechaFin", NpgsqlDbType.Date) { Value = (object?)fechaFin?.Date ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@entidades", NpgsqlDbType.Varchar, 255) { Value = (object?)entidades ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@rep", NpgsqlDbType.Varchar, 255) { Value = (object?)representante ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@sisRef", NpgsqlDbType.Varchar, 100) { Value = (object?)sistemaReferencia ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fmt", NpgsqlDbType.Varchar, 100) { Value = (object?)formatoDatos ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@linaje", NpgsqlDbType.Text) { Value = (object?)linaje ?? DBNull.Value });

                    var newIdObj = await cmd.ExecuteScalarAsync();
                    newId = Convert.ToInt32(newIdObj);
                    Debug.WriteLine($"[ProyectoRepository] Proyecto insertado con ID: {newId}");
                }

                // 2. Insertar relación con municipio(s) (soporta un código o múltiples códigos separados por coma)
                if (!string.IsNullOrWhiteSpace(municipioCodigo))
                {
                    var codigosMpio = municipioCodigo
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    const string sqlRel = @"
                        INSERT INTO geovisor.proyecto_municipio (id_proyecto, mpio_cdpmp)
                        VALUES (@id, @mun)
                        ON CONFLICT DO NOTHING;";

                    foreach (var cod in codigosMpio)
                    {
                        using var cmdRel = new NpgsqlCommand(sqlRel, con, tran);
                        cmdRel.Parameters.AddWithValue("@id", newId);
                        cmdRel.Parameters.AddWithValue("@mun", cod);
                        await cmdRel.ExecuteNonQueryAsync();
                    }
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
                    await tran.SaveAsync("audit_sp");
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", newId);
                    cmdAudit.Parameters.AddWithValue("@titulo", titulo);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    cmdAudit.Parameters.AddWithValue("@detalles", (object?)detallesAudit ?? DBNull.Value);
                    await cmdAudit.ExecuteNonQueryAsync();
                    await tran.ReleaseAsync("audit_sp");
                }
                catch (Exception exAudit)
                {
                    try { await tran.RollbackAsync("audit_sp"); } catch { }
                    Debug.WriteLine($"[ProyectoRepository] Advertencia al registrar auditoría en inserción: {exAudit.Message}");
                }

                await tran.CommitAsync();
                return newId;
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
                SELECT p.id_proyecto, p.titulo, p.descripcion, p.fecha_inicio, p.palabra_clave, p.ruta_archivos,
                       ST_X(ST_Centroid(ST_Transform(p.geom, 4326))) AS lon,
                       ST_Y(ST_Centroid(ST_Transform(p.geom, 4326))) AS lat,
                       STRING_AGG(m.mpio_cdpmp, ', ' ORDER BY d.dpto_cnmbr, m.mpio_cnmbr) AS mpio_codigos,
                       STRING_AGG(
                           CASE WHEN d.dpto_cnmbr IS NOT NULL
                                THEN m.mpio_cnmbr || ' (' || d.dpto_cnmbr || ')'
                                ELSE m.mpio_cnmbr
                           END,
                           ', ' ORDER BY d.dpto_cnmbr, m.mpio_cnmbr
                       ) AS mpio_nombres,
                       p.fecha_fin,
                       p.entidades,
                       p.representante,
                       p.sistema_referencia,
                       p.formato_datos,
                       p.linaje,
                       p.fecha_actualizacion
                FROM geovisor.proyecto p
                LEFT JOIN geovisor.proyecto_municipio pm ON pm.id_proyecto = p.id_proyecto
                LEFT JOIN geovisor.municipio m ON m.mpio_cdpmp = pm.mpio_cdpmp
                LEFT JOIN geovisor.departamento d ON d.dpto_ccdgo = m.dpto_ccdgo
                WHERE p.id_proyecto = @id
                GROUP BY p.id_proyecto;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();

            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = idProyecto });

            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                return new ProyectoDetalleDto(
                    Id: rd.GetInt32(0),
                    Titulo: rd.GetString(1),
                    Descripcion: rd.IsDBNull(2) ? null : rd.GetString(2),
                    FechaInicio: rd.IsDBNull(3) ? null : rd.GetDateTime(3),
                    PalabraClave: rd.IsDBNull(4) ? null : rd.GetString(4),
                    RutaArchivos: rd.IsDBNull(5) ? null : rd.GetString(5),
                    Lon: rd.IsDBNull(6) ? 0 : rd.GetDouble(6),
                    Lat: rd.IsDBNull(7) ? 0 : rd.GetDouble(7),
                    MunicipioCodigo: rd.IsDBNull(8) ? null : rd.GetString(8),
                    MunicipioNombre: rd.IsDBNull(9) ? null : rd.GetString(9),
                    FechaFin: rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                    Entidades: rd.IsDBNull(11) ? null : rd.GetString(11),
                    Representante: rd.IsDBNull(12) ? null : rd.GetString(12),
                    SistemaReferencia: rd.IsDBNull(13) ? null : rd.GetString(13),
                    FormatoDatos: rd.IsDBNull(14) ? null : rd.GetString(14),
                    Linaje: rd.IsDBNull(15) ? null : rd.GetString(15),
                    FechaActualizacion: rd.IsDBNull(16) ? null : rd.GetDateTime(16)
                );
            }
            return null;
        }

        public async Task ActualizarAsync(int idProyecto, string titulo, string? descripcion, DateTime? fechaInicio, string? palabraClave, string? ruta, string? geom, string? municipioCodigo, string? usuario = null, string? equipo = null, DateTime? fechaFin = null, string? entidades = null, string? representante = null, string? sistemaReferencia = null, string? formatoDatos = null, string? linaje = null)
        {
            const string sqlUpdate = @"
                UPDATE geovisor.proyecto
                SET titulo = @titulo,
                    descripcion = @desc,
                    fecha_inicio = @fechaInicio,
                    palabra_clave = @kw,
                    ruta_archivos = @ruta,
                    geom = CASE WHEN @geom IS NOT NULL
                                THEN ST_GeomFromText(@geom, 4686)
                                ELSE geom END,
                    fecha_fin = @fechaFin,
                    entidades = @entidades,
                    representante = @rep,
                    sistema_referencia = @sisRef,
                    formato_datos = @fmt,
                    linaje = @linaje,
                    fecha_actualizacion = CURRENT_TIMESTAMP
                WHERE id_proyecto = @id;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync();
            using var tran = await con.BeginTransactionAsync();

            try
            {
                using (var cmd = new NpgsqlCommand(sqlUpdate, con, tran))
                {
                    cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = idProyecto });
                    cmd.Parameters.Add(new NpgsqlParameter("@titulo", NpgsqlDbType.Varchar, 200) { Value = titulo });
                    cmd.Parameters.Add(new NpgsqlParameter("@desc", NpgsqlDbType.Text) { Value = (object?)descripcion ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fechaInicio", NpgsqlDbType.Date) { Value = (object?)fechaInicio?.Date ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@kw", NpgsqlDbType.Text) { Value = (object?)palabraClave ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@ruta", NpgsqlDbType.Text) { Value = (object?)ruta ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@geom", NpgsqlDbType.Text) { Value = (object?)geom ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fechaFin", NpgsqlDbType.Date) { Value = (object?)fechaFin?.Date ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@entidades", NpgsqlDbType.Varchar, 255) { Value = (object?)entidades ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@rep", NpgsqlDbType.Varchar, 255) { Value = (object?)representante ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@sisRef", NpgsqlDbType.Varchar, 100) { Value = (object?)sistemaReferencia ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@fmt", NpgsqlDbType.Varchar, 100) { Value = (object?)formatoDatos ?? DBNull.Value });
                    cmd.Parameters.Add(new NpgsqlParameter("@linaje", NpgsqlDbType.Text) { Value = (object?)linaje ?? DBNull.Value });
                    await cmd.ExecuteNonQueryAsync();
                }

                if (!string.IsNullOrWhiteSpace(municipioCodigo))
                {
                    var codigosMpio = municipioCodigo
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    const string sqlDeleteRel = "DELETE FROM geovisor.proyecto_municipio WHERE id_proyecto = @id;";
                    using (var cmdDel = new NpgsqlCommand(sqlDeleteRel, con, tran))
                    {
                        cmdDel.Parameters.AddWithValue("@id", idProyecto);
                        await cmdDel.ExecuteNonQueryAsync();
                    }

                    const string sqlInsertRel = @"
                        INSERT INTO geovisor.proyecto_municipio (id_proyecto, mpio_cdpmp)
                        VALUES (@id, @mun)
                        ON CONFLICT DO NOTHING;";

                    foreach (var cod in codigosMpio)
                    {
                        using var cmdIns = new NpgsqlCommand(sqlInsertRel, con, tran);
                        cmdIns.Parameters.AddWithValue("@id", idProyecto);
                        cmdIns.Parameters.AddWithValue("@mun", cod);
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
                    await tran.SaveAsync("audit_sp");
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", idProyecto);
                    cmdAudit.Parameters.AddWithValue("@titulo", titulo);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    cmdAudit.Parameters.AddWithValue("@detalles", (object?)detallesAudit ?? DBNull.Value);
                    await cmdAudit.ExecuteNonQueryAsync();
                    await tran.ReleaseAsync("audit_sp");
                }
                catch (Exception exAudit)
                {
                    try { await tran.RollbackAsync("audit_sp"); } catch { }
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
                    await tran.SaveAsync("audit_sp", ct);
                    using var cmdAudit = new NpgsqlCommand(sqlAudit, con, tran);
                    cmdAudit.Parameters.AddWithValue("@id", idProyecto);
                    cmdAudit.Parameters.AddWithValue("@titulo", tituloProyecto);
                    cmdAudit.Parameters.AddWithValue("@user", userAudit);
                    cmdAudit.Parameters.AddWithValue("@machine", machineAudit);
                    await cmdAudit.ExecuteNonQueryAsync(ct);
                    await tran.ReleaseAsync("audit_sp", ct);
                }
                catch (Exception exAudit)
                {
                    try { await tran.RollbackAsync("audit_sp", ct); } catch { }
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

        public async Task RegistrarAuditoriaAsync(
            int idProyecto,
            string tituloProyecto,
            string accion,
            string detalles,
            string? usuario = null,
            string? equipo = null,
            CancellationToken ct = default)
        {
            string userAudit = !string.IsNullOrWhiteSpace(usuario) ? usuario : ObtenerUsuarioActual();
            string machineAudit = !string.IsNullOrWhiteSpace(equipo) ? equipo : Environment.MachineName;

            const string sql = @"
                INSERT INTO geovisor.auditoria_proyecto (id_proyecto, titulo_proyecto, accion, usuario, equipo, fecha_hora, detalles)
                VALUES (@id, @titulo, @accion, @user, @machine, NOW(), @detalles);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("@id", idProyecto);
                cmd.Parameters.AddWithValue("@titulo", tituloProyecto);
                cmd.Parameters.AddWithValue("@accion", accion);
                cmd.Parameters.AddWithValue("@user", userAudit);
                cmd.Parameters.AddWithValue("@machine", machineAudit);
                cmd.Parameters.AddWithValue("@detalles", (object?)detalles ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al registrar auditoría ({accion}): {ex.Message}");
            }
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
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS fecha_inicio DATE;
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS fecha_fin DATE;
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS entidades VARCHAR(255);
                ALTER TABLE geovisor.proyecto ADD COLUMN IF NOT EXISTS representante VARCHAR(255);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                await cmd.ExecuteNonQueryAsync(ct);
                Debug.WriteLine("[ProyectoRepository] Columnas fecha_inicio, fecha_fin, entidades, representante verificadas/creadas exitosamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Advertencia al verificar columnas de proyecto: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<SubproyectoDto>> ObtenerSubproyectosAsync(int idProyecto, CancellationToken ct = default)
        {
            if (idProyecto <= 0) return Array.Empty<SubproyectoDto>();

            const string sql = @"
                SELECT s.id_subproyecto, s.id_proyecto, s.nombre, s.mpio_cdpmp, m.mpio_cnmbr, s.subcarpeta,
                       ST_X(s.geom) AS lon, ST_Y(s.geom) AS lat, s.descripcion
                FROM geovisor.subproyecto s
                LEFT JOIN geovisor.municipio m ON s.mpio_cdpmp = m.mpio_cdpmp
                WHERE s.id_proyecto = @idProyecto
                ORDER BY s.nombre;";

            var lista = new List<SubproyectoDto>();
            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    int idSub = reader.GetInt32(0);
                    int idProy = reader.GetInt32(1);
                    string nombre = reader.GetString(2);
                    string? mpioCod = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                    string? mpioNom = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
                    string subcarpeta = reader.GetString(5);
                    double lon = reader.GetDouble(6);
                    double lat = reader.GetDouble(7);
                    string? desc = reader.IsDBNull(8) ? null : reader.GetString(8);

                    lista.Add(new SubproyectoDto(idSub, idProy, nombre, mpioCod, mpioNom, subcarpeta, lon, lat, desc));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener subproyectos del proyecto {idProyecto}: {ex.Message}");
            }

            return lista;
        }

        public async Task<IReadOnlyDictionary<int, IReadOnlyList<SubproyectoDto>>> ObtenerSubproyectosPorProyectosAsync(IReadOnlyList<int> idsProyecto, CancellationToken ct = default)
        {
            var resultado = new Dictionary<int, List<SubproyectoDto>>();
            if (idsProyecto == null || idsProyecto.Count == 0)
                return new Dictionary<int, IReadOnlyList<SubproyectoDto>>();

            var idsValidos = idsProyecto.Where(id => id > 0).Distinct().ToList();
            if (idsValidos.Count == 0)
                return new Dictionary<int, IReadOnlyList<SubproyectoDto>>();

            const string sql = @"
                SELECT s.id_subproyecto, s.id_proyecto, s.nombre, s.mpio_cdpmp, m.mpio_cnmbr, s.subcarpeta,
                       ST_X(s.geom) AS lon, ST_Y(s.geom) AS lat, s.descripcion
                FROM geovisor.subproyecto s
                LEFT JOIN geovisor.municipio m ON s.mpio_cdpmp = m.mpio_cdpmp
                WHERE s.id_proyecto = ANY(@ids)
                ORDER BY s.id_proyecto, s.nombre;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("ids", idsValidos.ToArray());

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    int idSub = reader.GetInt32(0);
                    int idProy = reader.GetInt32(1);
                    string nombre = reader.GetString(2);
                    string? mpioCod = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                    string? mpioNom = reader.IsDBNull(4) ? null : reader.GetString(4).Trim();
                    string subcarpeta = reader.GetString(5);
                    double lon = reader.GetDouble(6);
                    double lat = reader.GetDouble(7);
                    string? desc = reader.IsDBNull(8) ? null : reader.GetString(8);

                    if (!resultado.TryGetValue(idProy, out var lista))
                    {
                        lista = new List<SubproyectoDto>();
                        resultado[idProy] = lista;
                    }
                    lista.Add(new SubproyectoDto(idSub, idProy, nombre, mpioCod, mpioNom, subcarpeta, lon, lat, desc));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener subproyectos en lote: {ex.Message}");
            }

            return resultado.ToDictionary(k => k.Key, k => (IReadOnlyList<SubproyectoDto>)k.Value);
        }

        public async Task<int> InsertarSubproyectoAsync(
            int idProyecto,
            string nombre,
            string? mpioCdpmp,
            string subcarpeta,
            double lon,
            double lat,
            string? descripcion = null,
            CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO geovisor.subproyecto (id_proyecto, nombre, mpio_cdpmp, subcarpeta, geom, descripcion)
                VALUES (@idProyecto, @nombre, @mpioCdpmp, @subcarpeta, ST_SetSRID(ST_MakePoint(@lon, @lat), 4686), @descripcion)
                RETURNING id_subproyecto;";

            using var con = new NpgsqlConnection(_cn);
            await con.OpenAsync(ct);
            using var cmd = new NpgsqlCommand(sql, con);
            cmd.Parameters.AddWithValue("idProyecto", idProyecto);
            cmd.Parameters.AddWithValue("nombre", nombre);
            cmd.Parameters.AddWithValue("mpioCdpmp", (object?)mpioCdpmp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("subcarpeta", subcarpeta);
            cmd.Parameters.AddWithValue("lon", lon);
            cmd.Parameters.AddWithValue("lat", lat);
            cmd.Parameters.AddWithValue("descripcion", (object?)descripcion ?? DBNull.Value);

            var res = await cmd.ExecuteScalarAsync(ct);
            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task ActualizarSubproyectoAsync(
            int idSubproyecto,
            string nombre,
            string? mpioCdpmp,
            string subcarpeta,
            double lon,
            double lat,
            string? descripcion = null,
            CancellationToken ct = default)
        {
            if (idSubproyecto <= 0) return;

            const string sql = @"
                UPDATE geovisor.subproyecto
                SET nombre = @nombre,
                    mpio_cdpmp = @mpioCdpmp,
                    subcarpeta = @subcarpeta,
                    geom = ST_SetSRID(ST_MakePoint(@lon, @lat), 4686),
                    descripcion = @descripcion
                WHERE id_subproyecto = @idSubproyecto;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idSubproyecto", idSubproyecto);
                cmd.Parameters.AddWithValue("nombre", nombre);
                cmd.Parameters.AddWithValue("mpioCdpmp", (object?)mpioCdpmp ?? DBNull.Value);
                cmd.Parameters.AddWithValue("subcarpeta", subcarpeta);
                cmd.Parameters.AddWithValue("lon", lon);
                cmd.Parameters.AddWithValue("lat", lat);
                cmd.Parameters.AddWithValue("descripcion", (object?)descripcion ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al actualizar subproyecto {idSubproyecto}: {ex.Message}");
                throw;
            }
        }

        public async Task EliminarSubproyectoAsync(int idSubproyecto, CancellationToken ct = default)
        {
            if (idSubproyecto <= 0) return;

            const string sql = "DELETE FROM geovisor.subproyecto WHERE id_subproyecto = @idSubproyecto;";
            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idSubproyecto", idSubproyecto);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al eliminar subproyecto {idSubproyecto}: {ex.Message}");
                throw;
            }
        }

        public async Task AsegurarTablaSubproyectosAsync(CancellationToken ct = default)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS geovisor.subproyecto (
                    id_subproyecto SERIAL PRIMARY KEY,
                    id_proyecto INT NOT NULL REFERENCES geovisor.proyecto(id_proyecto) ON DELETE CASCADE,
                    nombre VARCHAR(150) NOT NULL,
                    mpio_cdpmp CHAR(5) REFERENCES geovisor.municipio(mpio_cdpmp) ON DELETE SET NULL,
                    subcarpeta VARCHAR(255) NOT NULL,
                    geom public.geometry(Point, 4686) NOT NULL,
                    descripcion TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_subproyecto_geom ON geovisor.subproyecto USING gist (geom);
                CREATE INDEX IF NOT EXISTS idx_subproyecto_proyecto ON geovisor.subproyecto (id_proyecto);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                await cmd.ExecuteNonQueryAsync(ct);
                Debug.WriteLine("[ProyectoRepository] Tabla geovisor.subproyecto verificada/creada exitosamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Advertencia al verificar tabla subproyecto: {ex.Message}");
            }
        }

        #region Auditoria de Archivos (geovisor.auditoria_archivo)

        public async Task AsegurarTablaAuditoriaArchivoAsync(CancellationToken ct = default)
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS geovisor.auditoria_archivo (
                    id_auditoria_archivo SERIAL PRIMARY KEY,
                    id_proyecto INT NOT NULL REFERENCES geovisor.proyecto(id_proyecto) ON DELETE CASCADE,
                    ruta_relativa VARCHAR(1000) NOT NULL,
                    nombre_archivo VARCHAR(255) NOT NULL,
                    accion VARCHAR(50) NOT NULL,
                    usuario VARCHAR(150) NOT NULL,
                    equipo VARCHAR(100),
                    fecha_hora TIMESTAMPTZ DEFAULT NOW(),
                    tamano_bytes BIGINT,
                    fecha_archivo TIMESTAMPTZ,
                    detalles TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_proyecto_ruta ON geovisor.auditoria_archivo (id_proyecto, lower(ruta_relativa));
                CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_proyecto_fecha ON geovisor.auditoria_archivo (id_proyecto, fecha_hora DESC);
                CREATE INDEX IF NOT EXISTS idx_auditoria_archivo_eliminados ON geovisor.auditoria_archivo (id_proyecto, accion, fecha_hora DESC);

                CREATE OR REPLACE FUNCTION geovisor.fn_purgar_auditoria_archivo_vejez(
                    p_dias_retencion integer DEFAULT 7,
                    p_minimo_registros integer DEFAULT 5
                )
                RETURNS integer
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_borrados integer := 0;
                BEGIN
                    WITH registros_ordenados AS (
                        SELECT
                            id_auditoria_archivo,
                            ROW_NUMBER() OVER (
                                PARTITION BY id_proyecto, lower(ruta_relativa)
                                ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                            ) AS num_fila,
                            fecha_hora
                        FROM geovisor.auditoria_archivo
                    ),
                    candidatos_borrado AS (
                        SELECT id_auditoria_archivo
                        FROM registros_ordenados
                        WHERE num_fila > p_minimo_registros
                          AND fecha_hora < (NOW() - (p_dias_retencion || ' days')::interval)
                    )
                    DELETE FROM geovisor.auditoria_archivo
                    WHERE id_auditoria_archivo IN (SELECT id_auditoria_archivo FROM candidatos_borrado);

                    GET DIAGNOSTICS v_borrados = ROW_COUNT;
                    RETURN v_borrados;
                END;
                $function$;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                await cmd.ExecuteNonQueryAsync(ct);
                Debug.WriteLine("[ProyectoRepository] Tabla geovisor.auditoria_archivo y función de purga verificadas exitosamente.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Advertencia al verificar tabla auditoria_archivo: {ex.Message}");
            }
        }

        public async Task RegistrarAuditoriaArchivoAsync(
            int idProyecto,
            string rutaRelativa,
            string nombreArchivo,
            string accion,
            string? detalles,
            long? tamanoBytes = null,
            string? usuario = null,
            string? equipo = null,
            DateTime? fechaArchivo = null,
            CancellationToken ct = default)
        {
            if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativa)) return;

            string user = !string.IsNullOrWhiteSpace(usuario) ? usuario.Trim() : ObtenerUsuarioActual();
            string eq = !string.IsNullOrWhiteSpace(equipo) ? equipo.Trim() : Environment.MachineName;

            const string sql = @"
                INSERT INTO geovisor.auditoria_archivo (
                    id_proyecto, ruta_relativa, nombre_archivo, accion,
                    detalles, tamano_bytes, usuario, equipo, fecha_archivo, fecha_hora
                ) VALUES (
                    @idProyecto, @rutaRelativa, @nombreArchivo, @accion,
                    @detalles, @tamanoBytes, @usuario, @equipo, @fechaArchivo, NOW()
                );";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("rutaRelativa", rutaRelativa.Replace('\\', '/'));
                cmd.Parameters.AddWithValue("nombreArchivo", nombreArchivo);
                cmd.Parameters.AddWithValue("accion", accion.ToUpperInvariant().Trim());
                cmd.Parameters.AddWithValue("detalles", (object?)detalles ?? DBNull.Value);
                cmd.Parameters.AddWithValue("tamanoBytes", (object?)tamanoBytes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("usuario", user);
                cmd.Parameters.AddWithValue("equipo", eq);
                cmd.Parameters.AddWithValue("fechaArchivo", (object?)fechaArchivo ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al registrar auditoría de archivo para proyecto {idProyecto}: {ex.Message}");
                throw;
            }
        }

        public async Task<AuditoriaArchivoDto?> ObtenerUltimoEstadoArchivoAsync(
            int idProyecto,
            string rutaRelativa,
            CancellationToken ct = default)
        {
            if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativa)) return null;

            const string sql = @"
                SELECT id_auditoria_archivo, id_proyecto, ruta_relativa, nombre_archivo,
                       accion, usuario, equipo, fecha_hora, tamano_bytes, detalles, fecha_archivo
                FROM geovisor.auditoria_archivo
                WHERE id_proyecto = @idProyecto AND lower(ruta_relativa) = lower(@rutaRelativa)
                ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                LIMIT 1;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("rutaRelativa", rutaRelativa.Replace('\\', '/'));

                using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    return MapearAuditoriaArchivo(reader);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener último estado de archivo {rutaRelativa}: {ex.Message}");
            }

            return null;
        }

        public async Task ReasignarRutaAuditoriaArchivoAsync(
            int idProyecto,
            string rutaRelativaAntigua,
            string rutaRelativaNueva,
            string nombreArchivoNuevo,
            CancellationToken ct = default)
        {
            if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativaAntigua) || string.IsNullOrWhiteSpace(rutaRelativaNueva))
                return;

            const string sql = @"
                UPDATE geovisor.auditoria_archivo
                SET ruta_relativa = @rutaNueva,
                    nombre_archivo = @nombreNuevo
                WHERE id_proyecto = @idProyecto
                  AND lower(ruta_relativa) = lower(@rutaAntigua);";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("rutaAntigua", rutaRelativaAntigua.Replace('\\', '/'));
                cmd.Parameters.AddWithValue("rutaNueva", rutaRelativaNueva.Replace('\\', '/'));
                cmd.Parameters.AddWithValue("nombreNuevo", nombreArchivoNuevo);

                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al reasignar ruta de auditoría de archivo: {ex.Message}");
                throw;
            }
        }

        public async Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerHistorialArchivoAsync(
            int idProyecto,
            string rutaRelativa,
            int limite = 5,
            CancellationToken ct = default)
        {
            var lista = new List<AuditoriaArchivoDto>();
            if (idProyecto <= 0 || string.IsNullOrWhiteSpace(rutaRelativa)) return lista;

            const string sql = @"
                SELECT id_auditoria_archivo, id_proyecto, ruta_relativa, nombre_archivo,
                       accion, usuario, equipo, fecha_hora, tamano_bytes, detalles, fecha_archivo
                FROM geovisor.auditoria_archivo
                WHERE id_proyecto = @idProyecto AND lower(ruta_relativa) = lower(@rutaRelativa)
                ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                LIMIT @limite;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("rutaRelativa", rutaRelativa.Replace('\\', '/'));
                cmd.Parameters.AddWithValue("limite", limite > 0 ? limite : 5);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lista.Add(MapearAuditoriaArchivo(reader));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener historial de archivo {rutaRelativa}: {ex.Message}");
            }

            return lista;
        }

        public async Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerHistorialArchivosProyectoAsync(
            int idProyecto,
            int limite = 50,
            CancellationToken ct = default)
        {
            var lista = new List<AuditoriaArchivoDto>();
            if (idProyecto <= 0) return lista;

            const string sql = @"
                SELECT id_auditoria_archivo, id_proyecto, ruta_relativa, nombre_archivo,
                       accion, usuario, equipo, fecha_hora, tamano_bytes, detalles, fecha_archivo
                FROM geovisor.auditoria_archivo
                WHERE id_proyecto = @idProyecto
                ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                LIMIT @limite;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("limite", limite > 0 ? limite : 50);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lista.Add(MapearAuditoriaArchivo(reader));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener historial de archivos del proyecto {idProyecto}: {ex.Message}");
            }

            return lista;
        }

        public async Task<IReadOnlyList<AuditoriaArchivoDto>> ObtenerArchivosEliminadosProyectoAsync(
            int idProyecto,
            int limite = 50,
            CancellationToken ct = default)
        {
            var lista = new List<AuditoriaArchivoDto>();
            if (idProyecto <= 0) return lista;

            const string sql = @"
                SELECT id_auditoria_archivo, id_proyecto, ruta_relativa, nombre_archivo,
                       accion, usuario, equipo, fecha_hora, tamano_bytes, detalles, fecha_archivo
                FROM geovisor.auditoria_archivo
                WHERE id_proyecto = @idProyecto AND accion = 'ELIMINAR'
                ORDER BY fecha_hora DESC, id_auditoria_archivo DESC
                LIMIT @limite;";

            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                cmd.Parameters.AddWithValue("limite", limite > 0 ? limite : 50);

                using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lista.Add(MapearAuditoriaArchivo(reader));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al obtener archivos eliminados del proyecto {idProyecto}: {ex.Message}");
            }

            return lista;
        }

        public async Task<bool> TieneAuditoriaArchivoAsync(int idProyecto, CancellationToken ct = default)
        {
            if (idProyecto <= 0) return false;

            const string sql = "SELECT 1 FROM geovisor.auditoria_archivo WHERE id_proyecto = @idProyecto LIMIT 1;";
            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("idProyecto", idProyecto);
                var res = await cmd.ExecuteScalarAsync(ct);
                return res != null && res != DBNull.Value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al verificar existencia de auditoría para proyecto {idProyecto}: {ex.Message}");
                return false;
            }
        }

        public async Task<int> PurgarAuditoriaArchivoPorVejezAsync(int diasRetencion = 7, int minimoRegistros = 5, CancellationToken ct = default)
        {
            const string sql = "SELECT geovisor.fn_purgar_auditoria_archivo_vejez(@diasRetencion, @minimoRegistros);";
            try
            {
                using var con = new NpgsqlConnection(_cn);
                await con.OpenAsync(ct);
                using var cmd = new NpgsqlCommand(sql, con);
                cmd.Parameters.AddWithValue("diasRetencion", diasRetencion);
                cmd.Parameters.AddWithValue("minimoRegistros", minimoRegistros);

                var res = await cmd.ExecuteScalarAsync(ct);
                return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProyectoRepository] Error al purgar auditoría de archivos por vejez: {ex.Message}");
                return 0;
            }
        }

        private static AuditoriaArchivoDto MapearAuditoriaArchivo(NpgsqlDataReader reader)
        {
            int idAuditoria = reader.GetInt32(0);
            int idProyecto = reader.GetInt32(1);
            string rutaRelativa = reader.GetString(2);
            string nombreArchivo = reader.GetString(3);
            string accion = reader.GetString(4);
            string usuario = reader.GetString(5);
            string? equipo = reader.IsDBNull(6) ? null : reader.GetString(6);
            DateTime fechaHora = reader.GetDateTime(7);
            long? tamanoBytes = reader.IsDBNull(8) ? null : reader.GetInt64(8);
            string? detalles = reader.IsDBNull(9) ? null : reader.GetString(9);
            DateTime? fechaArchivo = reader.IsDBNull(10) ? null : reader.GetDateTime(10);

            return new AuditoriaArchivoDto(
                idAuditoria,
                idProyecto,
                rutaRelativa,
                nombreArchivo,
                accion,
                usuario,
                equipo,
                fechaHora,
                tamanoBytes,
                detalles,
                fechaArchivo);
        }

        #endregion
    }
}
