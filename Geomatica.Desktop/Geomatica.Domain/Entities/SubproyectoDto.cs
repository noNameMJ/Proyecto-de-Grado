namespace Geomatica.Domain.Entities;

/// <summary>
/// Representa un subproyecto, frente de obra, tramo o sitio puntual de infraestructura
/// asociado a un proyecto matriz, con ubicación geográfica física y subcarpeta propia.
/// </summary>
public sealed record SubproyectoDto(
    int IdSubproyecto,
    int IdProyecto,
    string Nombre,
    string? MunicipioCodigo,
    string? MunicipioNombre,
    string Subcarpeta,
    double Lon,
    double Lat,
    string? Descripcion = null)
{
    /// <summary>
    /// Formato legible de coordenadas geográficas en grados decimales (estándar internacional con punto).
    /// </summary>
    public string CoordenadasTexto => $"{Lat.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)}° N, {Lon.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)}° W";

    /// <summary>
    /// Etiqueta territorial combinada con nombre de municipio si está disponible.
    /// </summary>
    public string UbicacionMunicipioTexto => !string.IsNullOrWhiteSpace(MunicipioNombre)
        ? (!string.IsNullOrWhiteSpace(MunicipioCodigo) ? $"{MunicipioNombre} ({MunicipioCodigo})" : MunicipioNombre)
        : "Sin municipio asignado";
}
