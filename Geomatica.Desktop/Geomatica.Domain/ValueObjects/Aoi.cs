namespace Geomatica.Domain.ValueObjects;

public sealed class Aoi
{
    public string? Wkt { get; }
    private Aoi(string? wkt) => Wkt = wkt;
    public static Aoi FromWkt(string? wkt) => new(wkt);
}