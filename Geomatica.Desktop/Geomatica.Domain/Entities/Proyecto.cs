namespace Geomatica.Domain.Entities;
public sealed class Proyecto
{
    public int Id { get; }
    public string Titulo { get; }
    public DateTime? Fecha { get; }
    public string? PalabraClave { get; }
    public string? CodMpio { get; }          // referencia a municipio por código elegido
    public string? CodDpto { get; }          // opcional si decides redundancia controlada
    public double Lon { get; }               // derivado de geom para UI rápida
    public double Lat { get; }

    public Proyecto(int id, string titulo, DateTime? fecha, string? palabraClave,
                    string? codMpio, string? codDpto, double lon, double lat)
    {
        if (string.IsNullOrWhiteSpace(titulo)) throw new ArgumentException("Título requerido");
        Id = id; Titulo = titulo; Fecha = fecha; PalabraClave = palabraClave;
        CodMpio = codMpio; CodDpto = codDpto; Lon = lon; Lat = lat;
    }
}