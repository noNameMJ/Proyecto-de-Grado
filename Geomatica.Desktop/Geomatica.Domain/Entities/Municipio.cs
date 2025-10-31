namespace Geomatica.Domain.Entities;
public sealed class Municipio
{
    // Usa la clave que elegiste: concatenado nacional (mpio_cdpmp) o PK compuesta reflejada en Data
    public string CodMpio { get; }           // mpio_cdpmp
    public string CodDpto { get; }           // relación lógica al dpto
    public string Nombre { get; }
    public Municipio(string codMpio, string codDpto, string nombre)
    {
        if (string.IsNullOrWhiteSpace(codMpio)) throw new ArgumentException("codMpio vacío");
        if (string.IsNullOrWhiteSpace(codDpto)) throw new ArgumentException("codDpto vacío");
        CodMpio = codMpio;
        CodDpto = codDpto;
        Nombre = nombre ?? "";
    }
}