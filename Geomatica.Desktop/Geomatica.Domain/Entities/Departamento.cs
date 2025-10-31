namespace Geomatica.Domain.Entities;

public sealed class Departamento
{
    public string CodDpto { get; }           // dpto_ccdgo
    public string Nombre { get; }
    public Departamento(string codDpto, string nombre)
    {
        if (string.IsNullOrWhiteSpace(codDpto)) throw new ArgumentException("codDpto vacío");
        CodDpto = codDpto;
        Nombre = nombre ?? "";
    }
}