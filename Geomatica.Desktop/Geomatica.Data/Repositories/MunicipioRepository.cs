using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace Geomatica.Data.Repositories
{
 public interface IMunicipioRepository
 {
 Task<IReadOnlyList<MunicipioGeoJsonDto>> PorCodigosGeoJsonAsync(IReadOnlyList<string> codigos); // mpio_cdpmp
 Task<IReadOnlyList<MunicipioGeoJsonDto>> TodosGeoJsonAsync(int? limit = null); // para carga base
 Task<EnvelopeDto?> ExtentPorDepartamentoAsync(string dptoCcdgo); // dpto_ccdgo '68'
 Task<EnvelopeDto?> ExtentPorMunicipiosAsync(IReadOnlyList<string> codigos); // bbox combinado

 // Nuevo: listar departamentos
 Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentosAsync();
 
 // Nuevo: listar municipios por departamento (solo lista simple)
 Task<IReadOnlyList<MunicipioDto>> ListarMunicipiosPorDepartamentoAsync(string dptoCodigo);
 }

 public sealed record MunicipioGeoJsonDto(string Codigo, string Nombre, string GeoJson);
 public sealed record MunicipioDto(string Codigo, string Nombre);
 public sealed record EnvelopeDto(double West, double South, double East, double North);
 public sealed record DepartamentoDto(string Codigo, string Nombre);

 public sealed class MunicipioRepository : IMunicipioRepository
 {
 private readonly string _cn;
 public MunicipioRepository(string connectionString) => _cn = connectionString;

 public async Task<IReadOnlyList<MunicipioGeoJsonDto>> PorCodigosGeoJsonAsync(IReadOnlyList<string> codigos)
 {
 if (codigos == null || codigos.Count ==0) return Array.Empty<MunicipioGeoJsonDto>();
 const string sql = @"
 SELECT TRIM(m.mpio_cdpmp) AS codigo, m.mpio_cnmbr AS nombre,
 ST_AsGeoJSON(m.geom) AS geojson
 FROM geovisor.municipio m
 WHERE TRIM(m.mpio_cdpmp) = ANY(@cods);";

 var builder = new NpgsqlConnectionStringBuilder(_cn);
 Debug.WriteLine($"[MunicipioRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

 using var con = new NpgsqlConnection(_cn);
 try
 {
 await con.OpenAsync();
 }
 catch (Exception ex)
 {
 Debug.WriteLine($"[MunicipioRepository] Error abriendo conexión: {ex}");
 throw;
 }

 using var cmd = new NpgsqlCommand(sql, con);
 cmd.Parameters.Add("@cods", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = codigos.Select(c => c.Trim()).ToArray();

 var list = new List<MunicipioGeoJsonDto>();
 using var rd = await cmd.ExecuteReaderAsync();
 while (await rd.ReadAsync())
 {
 list.Add(new MunicipioGeoJsonDto(
 rd.GetString(0),
 rd.IsDBNull(1) ? "" : rd.GetString(1),
 rd.GetString(2)
));
 }
 return list;
 }

 public async Task<IReadOnlyList<MunicipioGeoJsonDto>> TodosGeoJsonAsync(int? limit = null)
 {
 var top = limit.HasValue ? "LIMIT @lim" : "";
 var sql = $@"
 SELECT TRIM(m.mpio_cdpmp) AS codigo, m.mpio_cnmbr AS nombre,
 ST_AsGeoJSON(m.geom) AS geojson
 FROM geovisor.municipio m
 ORDER BY m.dpto_ccdgo, m.mpio_cdpmp
 {top};";

 var builder = new NpgsqlConnectionStringBuilder(_cn);
 Debug.WriteLine($"[MunicipioRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

 using var con = new NpgsqlConnection(_cn);
 try
 {
 await con.OpenAsync();
 }
 catch (Exception ex)
 {
 Debug.WriteLine($"[MunicipioRepository] Error abriendo conexión: {ex}");
 throw;
 }

 using var cmd = new NpgsqlCommand(sql, con);
 if (limit.HasValue) cmd.Parameters.AddWithValue("@lim", limit.Value);

 var list = new List<MunicipioGeoJsonDto>();
 using var rd = await cmd.ExecuteReaderAsync();
 while (await rd.ReadAsync())
 {
 list.Add(new MunicipioGeoJsonDto(
 rd.GetString(0),
 rd.IsDBNull(1) ? "" : rd.GetString(1),
 rd.GetString(2)
));
 }
 return list;
 }

 public async Task<IReadOnlyList<DepartamentoDto>> ListarDepartamentosAsync()
 {
 const string sql = @"
 SELECT TRIM(d.dpto_ccdgo) AS codigo, d.dpto_cnmbr AS nombre
 FROM geovisor.departamento d
 ORDER BY d.dpto_cnmbr;";

 var builder = new NpgsqlConnectionStringBuilder(_cn);
 Debug.WriteLine($"[MunicipioRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

 using var con = new NpgsqlConnection(_cn);
 try
 {
 await con.OpenAsync();
 }
 catch (Exception ex)
 {
 Debug.WriteLine($"[MunicipioRepository] Error abriendo conexión (ListarDepartamentosAsync): {ex}");
 throw;
 }

 using var cmd = new NpgsqlCommand(sql, con);
 var list = new List<DepartamentoDto>();
 using var rd = await cmd.ExecuteReaderAsync();
 while (await rd.ReadAsync())
 {
 list.Add(new DepartamentoDto(rd.IsDBNull(0)?"":rd.GetString(0), rd.IsDBNull(1)?"":rd.GetString(1)));
 }
 return list;
 }

 public async Task<IReadOnlyList<MunicipioDto>> ListarMunicipiosPorDepartamentoAsync(string dptoCodigo)
 {
 const string sql = @"
 SELECT TRIM(m.mpio_cdpmp) AS codigo, m.mpio_cnmbr AS nombre
 FROM geovisor.municipio m
 WHERE TRIM(m.dpto_ccdgo) = TRIM(@dpto)
 ORDER BY m.mpio_cnmbr;";

 using var con = new NpgsqlConnection(_cn);
 await con.OpenAsync();
 using var cmd = new NpgsqlCommand(sql, con);
 cmd.Parameters.AddWithValue("@dpto", dptoCodigo);

 var list = new List<MunicipioDto>();
 using var rd = await cmd.ExecuteReaderAsync();
 while (await rd.ReadAsync())
 {
 list.Add(new MunicipioDto(
 rd.GetString(0),
 rd.IsDBNull(1) ? "" : rd.GetString(1)
 ));
 }
 return list;
 }

 public async Task<EnvelopeDto?> ExtentPorDepartamentoAsync(string dptoCcdgo)
 {
 const string sql = @"
 SELECT ST_XMin(e), ST_YMin(e), ST_XMax(e), ST_YMax(e)
 FROM (
 SELECT ST_Extent(m.geom)::box2d AS e
 FROM geovisor.municipio m
 WHERE TRIM(m.dpto_ccdgo) = TRIM(@dpto)
 ) q;";

 var builder = new NpgsqlConnectionStringBuilder(_cn);
 Debug.WriteLine($"[MunicipioRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

 using var con = new NpgsqlConnection(_cn);
 try
 {
 await con.OpenAsync();
 }
 catch (Exception ex)
 {
 Debug.WriteLine($"[MunicipioRepository] Error abriendo conexión (ExtentPorDepartamentoAsync): {ex}");
 throw;
 }

 using var cmd = new NpgsqlCommand(sql, con);
 cmd.Parameters.AddWithValue("@dpto", dptoCcdgo);

 using var rd = await cmd.ExecuteReaderAsync();
 if (await rd.ReadAsync() && !rd.IsDBNull(0))
 {
 return new EnvelopeDto(rd.GetDouble(0), rd.GetDouble(1), rd.GetDouble(2), rd.GetDouble(3));
 }
 return null;
 }

 public async Task<EnvelopeDto?> ExtentPorMunicipiosAsync(IReadOnlyList<string> codigos)
 {
 if (codigos == null || codigos.Count ==0) return null;
 const string sql = @"
 SELECT ST_XMin(e), ST_YMin(e), ST_XMax(e), ST_YMax(e)
 FROM (
 SELECT ST_Extent(m.geom)::box2d AS e
 FROM geovisor.municipio m
 WHERE TRIM(m.mpio_cdpmp) = ANY(@cods)
 ) q;";

 var builder = new NpgsqlConnectionStringBuilder(_cn);
 Debug.WriteLine($"[MunicipioRepository] Conectando a Postgres Host={builder.Host};Port={builder.Port};Database={builder.Database};User={builder.Username}");

 using var con = new NpgsqlConnection(_cn);
 try
 {
 await con.OpenAsync();
 }
 catch (Exception ex)
 {
 Debug.WriteLine($"[MunicipioRepository] Error abriendo conexión (ExtentPorMunicipiosAsync): {ex}");
 throw;
 }

 using var cmd = new NpgsqlCommand(sql, con);
 cmd.Parameters.Add("@cods", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = codigos.Select(c => c.Trim()).ToArray();

 using var rd = await cmd.ExecuteReaderAsync();
 if (await rd.ReadAsync() && !rd.IsDBNull(0))
 {
 return new EnvelopeDto(rd.GetDouble(0), rd.GetDouble(1), rd.GetDouble(2), rd.GetDouble(3));
 }
 return null;
 }
 }
}
