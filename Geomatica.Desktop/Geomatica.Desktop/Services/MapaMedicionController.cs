using System;
using System.Collections.Generic;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace Geomatica.Desktop.Services
{
    public sealed record MedicionResultado(string Resultado, string Detalle, bool HasResultado);

    public class MapaMedicionController
    {
        public GraphicsOverlay OverlayMedicion { get; } = new() { Id = "OverlayMedicion" };
        private readonly List<MapPoint> _puntosMedicion = new();

        private static readonly SimpleMarkerSymbol PuntoSymbol = new(
            SimpleMarkerSymbolStyle.Circle,
            System.Drawing.Color.FromArgb(255, 24, 134, 75),
            8);

        private static readonly SimpleLineSymbol LineSymbol = new(
            SimpleLineSymbolStyle.Solid,
            System.Drawing.Color.FromArgb(230, 24, 134, 75),
            3);

        public IReadOnlyList<MapPoint> Puntos => _puntosMedicion;

        public void Limpiar()
        {
            _puntosMedicion.Clear();
            OverlayMedicion.Graphics.Clear();
        }

        public MedicionResultado ProcesarNuevoPunto(MapPoint punto, string modoMedicion)
        {
            if (modoMedicion == "Ninguno")
            {
                return new MedicionResultado(string.Empty, string.Empty, false);
            }

            var puntoWgs84 = GeometryEngine.Project(punto, SpatialReferences.Wgs84) as MapPoint ?? punto;
            _puntosMedicion.Add(puntoWgs84);

            OverlayMedicion.Graphics.Clear();

            // Dibujar vértices
            foreach (var p in _puntosMedicion)
            {
                OverlayMedicion.Graphics.Add(new Graphic(p, PuntoSymbol));
            }

            if (modoMedicion == "Distancia")
            {
                if (_puntosMedicion.Count >= 2)
                {
                    var polyline = new Polyline(_puntosMedicion, SpatialReferences.Wgs84);
                    OverlayMedicion.Graphics.Add(new Graphic(polyline, LineSymbol));

                    var longitudMetros = GeometryEngine.LengthGeodetic(polyline, LinearUnits.Meters, GeodeticCurveType.Geodesic);
                    if (longitudMetros >= 1000)
                    {
                        var km = longitudMetros / 1000.0;
                        return new MedicionResultado(
                            $"{km:F2} km",
                            $"{longitudMetros:N0} metros ({_puntosMedicion.Count} puntos)",
                            true);
                    }
                    else
                    {
                        return new MedicionResultado(
                            $"{longitudMetros:F1} m",
                            $"{_puntosMedicion.Count} puntos marcados",
                            true);
                    }
                }
                else
                {
                    return new MedicionResultado(
                        "1 punto marcado",
                        "Haga clic en otro punto para calcular la distancia",
                        true);
                }
            }
            else if (modoMedicion == "Area")
            {
                if (_puntosMedicion.Count >= 3)
                {
                    var polygon = new Polygon(_puntosMedicion, SpatialReferences.Wgs84);
                    var fillSymbol = new SimpleFillSymbol(
                        SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(80, 24, 134, 75),
                        LineSymbol);

                    OverlayMedicion.Graphics.Add(new Graphic(polygon, fillSymbol));

                    var areaM2 = Math.Abs(GeometryEngine.AreaGeodetic(polygon, AreaUnits.SquareMeters, GeodeticCurveType.Geodesic));
                    var ha = areaM2 / 10_000.0;
                    var km2 = areaM2 / 1_000_000.0;

                    if (areaM2 >= 1_000_000)
                    {
                        return new MedicionResultado(
                            $"{km2:F2} km²",
                            $"{ha:N1} ha | {areaM2:N0} m² ({_puntosMedicion.Count} vértices)",
                            true);
                    }
                    else if (areaM2 >= 10_000)
                    {
                        return new MedicionResultado(
                            $"{ha:F2} ha",
                            $"{areaM2:N0} m² ({_puntosMedicion.Count} vértices)",
                            true);
                    }
                    else
                    {
                        return new MedicionResultado(
                            $"{areaM2:N1} m²",
                            $"{_puntosMedicion.Count} vértices",
                            true);
                    }
                }
                else if (_puntosMedicion.Count == 2)
                {
                    var polyline = new Polyline(_puntosMedicion, SpatialReferences.Wgs84);
                    OverlayMedicion.Graphics.Add(new Graphic(polyline, LineSymbol));
                    return new MedicionResultado(
                        "2 vértices marcados",
                        "Agregue al menos 3 vértices para calcular el área",
                        true);
                }
                else
                {
                    return new MedicionResultado(
                        "1 vértice marcado",
                        "Agregue al menos 3 vértices para calcular el área",
                        true);
                }
            }

            return new MedicionResultado(string.Empty, string.Empty, false);
        }
    }
}

