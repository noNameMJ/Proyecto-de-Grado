using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Npgsql;
using System.Text;

public class ProyectoRepository : IProyectoRepository
{
    private readonly string _cs;
    public ProyectoRepository(string connectionString) => _cs = connectionString;

    public async Task<IReadOnlyList<ProyectoGeomatico>> BuscarAsync(
        string? texto, DateTime? desde, DateTime? hasta,
        double? minX, double? minY, double? maxX, double? maxY,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        var sb = new StringBuilder(@"
            SELECT id, titulo, fecha, palabras_clave, responsable, ruta_archivos, tipo_recurso,
                   ST_XMin(geom) AS minx, ST_YMin(geom) AS miny,
                   ST_XMax(geom) AS maxx, ST_YMax(geom) AS maxy
            FROM proyectos_geom
            WHERE 1=1 ");

        if (!string.IsNullOrWhiteSpace(texto))
        { sb.Append(" AND (unaccent(lower(titulo)) LIKE unaccent(lower(@q)) OR unaccent(lower(palabras_clave)) LIKE unaccent(lower(@q))) "); cmd.Parameters.AddWithValue("q", $"%{texto}%"); }
        if (desde.HasValue) { sb.Append(" AND fecha >= @desde "); cmd.Parameters.AddWithValue("desde", desde.Value); }
        if (hasta.HasValue) { sb.Append(" AND fecha <= @hasta "); cmd.Parameters.AddWithValue("hasta", hasta.Value); }
        if (minX.HasValue && minY.HasValue && maxX.HasValue && maxY.HasValue)
        {
            sb.Append(" AND ST_Intersects(geom, ST_MakeEnvelope(@minx,@miny,@maxx,@maxy, 4326)) ");
            cmd.Parameters.AddWithValue("minx", minX.Value);
            cmd.Parameters.AddWithValue("miny", minY.Value);
            cmd.Parameters.AddWithValue("maxx", maxX.Value);
            cmd.Parameters.AddWithValue("maxy", maxY.Value);
        }
        sb.Append(" ORDER BY fecha DESC LIMIT 200; ");
        cmd.CommandText = sb.ToString();

        var list = new List<ProyectoGeomatico>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ProyectoGeomatico
            {
                Id = reader.GetGuid(0),
                Titulo = reader.GetString(1),
                Fecha = reader.GetDateTime(2),
                PalabrasClave = reader.GetString(3),
                Responsable = reader.GetString(4),
                RutaArchivos = reader.GetString(5),
                TipoRecurso = reader.GetString(6),
                MinX = reader.GetDouble(7),
                MinY = reader.GetDouble(8),
                MaxX = reader.GetDouble(9),
                MaxY = reader.GetDouble(10)
            });
        }
        return list;
    }
}
