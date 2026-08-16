namespace Geomatica.Domain.Entities;

public sealed record ProyectoDto(int Id, string Titulo, double Lon, double Lat, string? RutaArchivos);

public sealed record ProyectoDetalleDto(
    int Id,
    string Titulo,
    string? Descripcion,
    DateTime? Fecha,
    string? PalabraClave,
    string? RutaArchivos,
    double Lon,
    double Lat,
    string? MunicipioCodigo,
    string? MunicipioNombre);
