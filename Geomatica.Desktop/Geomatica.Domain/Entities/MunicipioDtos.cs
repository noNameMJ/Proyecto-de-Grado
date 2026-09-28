namespace Geomatica.Domain.Entities;

public sealed record MunicipioGeoJsonDto(string Codigo, string Nombre, string? GeoJson);
public sealed record MunicipioDto(string Codigo, string Nombre);
public sealed record EnvelopeDto(double West, double South, double East, double North);
public sealed record DepartamentoDto(string Codigo, string Nombre);
public sealed record MunicipioUbicacionDto(
    string MunicipioCodigo,
    string MunicipioNombre,
    string DepartamentoCodigo,
    string DepartamentoNombre,
    string? GeoJson);

