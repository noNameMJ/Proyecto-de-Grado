namespace Geomatica.Domain.Entities;

public sealed record ProyectoDto(int Id, string Titulo, double Lon, double Lat, string? RutaArchivos);

public sealed record ProyectoDetalleDto(
    int Id,
    string Titulo,
    string? Descripcion,
    DateTime? FechaInicio,
    string? PalabraClave,
    string? RutaArchivos,
    double Lon,
    double Lat,
    string? MunicipioCodigo,
    string? MunicipioNombre,
    DateTime? FechaFin = null,
    string? Entidades = null,
    string? Representante = null)
{
    /// <summary>
    /// Propiedad de compatibilidad con código existente que esperaba 'Fecha'.
    /// </summary>
    public DateTime? Fecha => FechaInicio;

    /// <summary>
    /// Propiedad de compatibilidad con código que esperaba 'AnioFin' como entero.
    /// </summary>
    public int? AnioFin => FechaFin?.Year;
}
