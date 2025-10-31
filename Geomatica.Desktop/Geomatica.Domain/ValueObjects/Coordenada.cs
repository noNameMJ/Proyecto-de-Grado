namespace Geomatica.Domain.ValueObjects;

public readonly struct Coordenada
{
    public double Lon { get; }
    public double Lat { get; }
    public Coordenada(double lon, double lat) { Lon = lon; Lat = lat; }
}