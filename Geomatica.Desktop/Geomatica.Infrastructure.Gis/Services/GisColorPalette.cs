using System;
using System.Collections.Generic;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;

namespace Geomatica.Infrastructure.Gis.Services
{
    public record GisColorInfo(System.Drawing.Color Color, string Hex, string Nombre);

    /// <summary>
    /// Paleta cromática equilibrada de alta distinción visual para capas vectoriales en SIG.
    /// Evita tonalidades oscuras o negras para asegurar máxima visibilidad sobre cualquier mapa base
    /// (imágenes satelitales, calles, topográfico, modo claro u oscuro).
    /// </summary>
    public static class GisColorPalette
    {
        public static readonly IReadOnlyList<GisColorInfo> Colores = new[]
        {
            new GisColorInfo(System.Drawing.Color.FromArgb(233, 30, 99),   "#E91E63", "Rosa Vibrante"),      // 1
            new GisColorInfo(System.Drawing.Color.FromArgb(30, 136, 229),  "#1E88E5", "Azul Eléctrico"),     // 2
            new GisColorInfo(System.Drawing.Color.FromArgb(251, 140, 0),   "#FB8C00", "Naranja Intenso"),    // 3
            new GisColorInfo(System.Drawing.Color.FromArgb(67, 160, 71),   "#43A047", "Verde Esmeralda"),    // 4
            new GisColorInfo(System.Drawing.Color.FromArgb(142, 36, 170),  "#8E24AA", "Púrpura Imperial"),   // 5
            new GisColorInfo(System.Drawing.Color.FromArgb(0, 172, 193),   "#00ACC1", "Turquesa"),           // 6
            new GisColorInfo(System.Drawing.Color.FromArgb(229, 57, 53),   "#E53935", "Rojo Carmesí"),       // 7
            new GisColorInfo(System.Drawing.Color.FromArgb(245, 158, 11),  "#F59E0B", "Ámbar Dorado"),       // 8
            new GisColorInfo(System.Drawing.Color.FromArgb(0, 137, 123),   "#00897B", "Verde Azulado"),      // 9
            new GisColorInfo(System.Drawing.Color.FromArgb(57, 73, 171),   "#3949AB", "Índigo"),             // 10
            new GisColorInfo(System.Drawing.Color.FromArgb(216, 27, 96),   "#D81B60", "Fucsia"),             // 11
            new GisColorInfo(System.Drawing.Color.FromArgb(124, 179, 66),  "#7CB342", "Verde Lima"),         // 12
            new GisColorInfo(System.Drawing.Color.FromArgb(141, 110, 99),  "#8D6E63", "Marrón Cálido"),      // 13
            new GisColorInfo(System.Drawing.Color.FromArgb(3, 155, 229),   "#039BE5", "Azul Cielo"),         // 14
            new GisColorInfo(System.Drawing.Color.FromArgb(126, 87, 194),  "#7E57C2", "Lavanda Intenso"),    // 15
            new GisColorInfo(System.Drawing.Color.FromArgb(255, 112, 67),  "#FF7043", "Coral")               // 16
        };

        /// <summary>
        /// Obtiene un color de la paleta según el índice especificado (con ciclo circular).
        /// </summary>
        public static GisColorInfo ObtenerColor(int indice)
        {
            int i = Math.Abs(indice) % Colores.Count;
            return Colores[i];
        }

        /// <summary>
        /// Obtiene el siguiente color de la paleta e incrementa el contador.
        /// </summary>
        public static GisColorInfo ObtenerSiguienteColor(ref int contador)
        {
            var info = ObtenerColor(contador);
            contador++;
            return info;
        }

        /// <summary>
        /// Genera una simbología (SimpleRenderer) estética y de alto contraste según el tipo de geometría y color asignado.
        /// - Puntos: Marcador circular visible con borde blanco de contraste (apto para ortofotos y satélite).
        /// - Líneas: Trazo sólido continuo de 2.5px.
        /// - Polígonos: Relleno traslúcido (~28% opacidad) que permite apreciar el mapa inferior, con contorno sólido de 2.0px.
        /// </summary>
        public static SimpleRenderer CrearRendererParaGeometria(GeometryType geomType, System.Drawing.Color color)
        {
            switch (geomType)
            {
                case GeometryType.Point:
                case GeometryType.Multipoint:
                    var marker = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.FromArgb(235, color.R, color.G, color.B),
                        9.0)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1.5)
                    };
                    return new SimpleRenderer(marker);

                case GeometryType.Polyline:
                    var line = new SimpleLineSymbol(
                        SimpleLineSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(240, color.R, color.G, color.B),
                        2.5);
                    return new SimpleRenderer(line);

                case GeometryType.Polygon:
                case GeometryType.Envelope:
                    var fill = new SimpleFillSymbol(
                        SimpleFillSymbolStyle.Solid,
                        System.Drawing.Color.FromArgb(70, color.R, color.G, color.B),
                        new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.FromArgb(245, color.R, color.G, color.B), 2.0)
                    );
                    return new SimpleRenderer(fill);

                default:
                    var fallback = new SimpleMarkerSymbol(
                        SimpleMarkerSymbolStyle.Circle,
                        System.Drawing.Color.FromArgb(235, color.R, color.G, color.B),
                        8.0)
                    {
                        Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, System.Drawing.Color.White, 1.2)
                    };
                    return new SimpleRenderer(fallback);
            }
        }
    }
}

