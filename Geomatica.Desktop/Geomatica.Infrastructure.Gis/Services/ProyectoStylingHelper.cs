using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;

namespace Geomatica.Infrastructure.Gis.Services
{
    /// <summary>
    /// Proveedor de simbología cartográfica e identidad visual característica para proyectos geomáticos.
    /// Proporciona un icono/marcador institucional unificado (Pin de Proyecto UIS), etiquetas flotantes legibles
    /// y resaltado interactivo con huella geográfica.
    /// </summary>
    public static class ProyectoStylingHelper
    {
        private static MarkerSymbol? _simboloProyectoCache;

        /// <summary>
        /// Imagen PNG vectorizada y embebida en Base64 para el Pin de Proyecto UIS:
        /// Teardrop en verde institucional (#1B5E20), contorno blanco sólido de 3.0px para alto contraste
        /// sobre mapas satelitales/claros, disco interior blanco y núcleo dorado.
        /// </summary>
        private const string PinProyectoBase64Png =
            "iVBORw0KGgoAAAANSUhEUgAAADAAAAA4CAYAAAC7UXvqAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAATOSURBVGhD" +
            "7VldiFVVFD70EEFE9hBGgUiE2kNlkCElFAQRQUNBD0FQglASTFREBQlagdIMYZkIIZVE0d/DRD5YDzLRUPOQjZCF0FwQFRKVUGeYO3dm7qwV32nvw5rv/M35uef6" +
            "4AcfF+49e/2cvfba3943CK7gCi4vqOqDqrpDRHaJyKjhCL5X1c2qehOP6ytUdb2IfKkFICKHXDLXsL3GICIb8XY5uCIQkTOq+jjb7jlQIhwMcOr8af3652/15f2v" +
            "6pM7n4q45YPn9L2R9/XQkR95SAg3IyvYT+1A/YrIOAeAoDe+sklveWZ1LtcPbtDtX7yt5y6eW2JDRI6r6mr2WRvwhkTkqHX6e2ti2YEzb996p+7/4WNrDklcUNV1" +
            "7LsysNjcNEdASXBQZfjCvkHtzHdsEidq71QistsGD6ccSBU+9tYTnATKtJ4OhTZpg0f9cgB18OnhZ60bJPESx1IKtu5H//gp5jiND297NOxA+OTf0rjzm3dtAlgP" +
            "1UoJPdobxBTnLdgHXn8otVUieZQKj2H+efKvaEzlWXC7ZYjvxr+PObNE77d1nIa9B/fprVvWxsZ7ojkY7OCYCsEmgF7PzjzfOPCmdRpi8cwx7Z74RRdPH+Gf9LPD" +
            "n8ds9C0BlI198wh49sP7dWb7yojt4Tu0O3k4egbA+mBbtScgIo94S78eH485A/G9B4K3gTMXjn4VPQvZwbZAvCiDzRxTITh5HCIpAdRyhIWOtnffEwt6yUzsWqMy" +
            "fTYaktQUGk0AXcUj7+1Hs3AMR4P/AZHHNutOYJ23NPlPK+bMLt6F3w7Egk3i/OhwNCZJjqDdeqCEOaZCgDr0xpJq1u6eWKQcbBKRqAdaL9u0awoVwDEVQl4CUJQe" +
            "MntRZ95ZFQuYuXh+MhqTtEvXncAKb+nSzKWYMxCJeeSV0dzB16Jn0XrZFmh3YugwjqkwrDV2BrIImx/bkzgTcyMvhp3KA7qHbfELqeWAY62xM0/qHCoXTun8+Efh" +
            "gkVC2JUtcBBiGz1LQERmvbU0MYa1YLtHFrJOcThukp6qpkYBEfnUW0N9slNLdBWslSQgsLSy8YRg9ICM51hKwR3ko1lA72fHTOgcHHzQ5/GZpnss7aboUK0DWSBu" +
            "bxW3CSgZDqAq6RwwwjFUgjvU47AdArcJHEAV2h3dzXb12mfw6SxpEypDzCbdEVWT0FmwVytFzsdZtHdD7qqxntuIJLiL3GhBJ6nJIsQsUtvs/T2pvSPCppN1vs0j" +
            "Kc9R9tUTuCtGTHWIvN6eRsweobruWS5EZKv3ihLADsoBZhGzZiUDZpV99Bz2wivvyoVJF1iYzfrbZh7wB0cURYZOYrLeqXx5VQX2r6U8neTZE71TFkV1Uk/1TlkU" +
            "0Uk91TtlsVyd1IjeKYs8ndSo3imLLJ3UqN4pizSd1Be9UxZJOqkveqcsWCclHPSb0ztlMT09PchRA+12ey8/e7nghiAIcH9zVxAEm8Cpqam/bfCdTuffgYGBAff7" +
            "3UEQ3BYEwY1sqGlcb4O2HBoaet4mMDY2toefcdzQr0SuSwhmCScmJj7pdrtzrVYLO27sd2LjScAhB1GFN7ODJoC6vy8hmKJcGwTBVWy8KWBHXeNqmQPL4r0u8GvZ" +
            "YD9xtSutVRlc6ZKuBf8BYqM7QLz9gmIAAAAASUVORK5CYII=";

        /// <summary>
        /// Crea el símbolo característico unificado para los proyectos geomáticos.
        /// Utiliza el Pin de Proyecto UIS mediante PictureMarkerSymbol o un SimpleMarkerSymbol de respaldo.
        /// </summary>
        public static async Task<MarkerSymbol> ObtenerSimboloProyectoAsync()
        {
            if (_simboloProyectoCache != null) return _simboloProyectoCache;

            try
            {
                var bytes = Convert.FromBase64String(PinProyectoBase64Png);
                using var ms = new MemoryStream(bytes);
                var pms = await PictureMarkerSymbol.CreateAsync(ms);
                pms.Width = 26;
                pms.Height = 31;
                pms.OffsetY = 15.5; // Alinea la punta inferior exactamente sobre las coordenadas
                _simboloProyectoCache = pms;
                return pms;
            }
            catch
            {
                _simboloProyectoCache = CrearSimboloFallback();
                return _simboloProyectoCache;
            }
        }

        /// <summary>
        /// Símbolo vectorial de respaldo con forma circular verde UIS y contorno blanco de alto contraste.
        /// </summary>
        public static SimpleMarkerSymbol CrearSimboloFallback()
        {
            return new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(27, 94, 32), // Verde UIS
                11.0)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.White, 2.5)
            };
        }

        /// <summary>
        /// Genera el Renderer para la capa de proyectos en el mapa utilizando el icono característico unificado.
        /// </summary>
        public static async Task<SimpleRenderer> CrearRendererProyectosAsync()
        {
            var symbol = await ObtenerSimboloProyectoAsync();
            return new SimpleRenderer(symbol);
        }

        /// <summary>
        /// Genera el Graphic de etiqueta de texto para posicionar sobre el marcador en el mapa.
        /// Diseñado con un halo blanco de 2.0px para garantizar máxima legibilidad sobre ortofotos y mapas satelitales.
        /// </summary>
        public static Graphic CrearEtiquetaGraphic(MapPoint point, string codigo, string titulo)
        {
            string tituloCorto = titulo.Length > 24 ? titulo.Substring(0, 21).TrimEnd() + "..." : titulo;
            string textoEtiqueta = $"{codigo}\n{tituloCorto}";

            var textSymbol = new TextSymbol(
                textoEtiqueta,
                Color.FromArgb(20, 20, 20),
                9.0,
                Esri.ArcGISRuntime.Symbology.HorizontalAlignment.Center,
                Esri.ArcGISRuntime.Symbology.VerticalAlignment.Bottom)
            {
                OffsetY = 17.0, // Posicionado justo arriba de la cabeza del pin
                HaloColor = Color.FromArgb(245, 255, 255, 255),
                HaloWidth = 2.0
            };

            return new Graphic(point, textSymbol);
        }

        /// <summary>
        /// Genera los Graphics interactivos para resaltar un proyecto seleccionado:
        /// - Halo exterior brillante (cyan traslúcido)
        /// - Anillo medio de selección
        /// - Núcleo central luminoso
        /// - Polígono de extensión/huella geográfica delimitado en línea punteada (si el proyecto tiene bounding box)
        /// </summary>
        public static IReadOnlyList<Graphic> CrearResaltadoGraphics(
            MapPoint point,
            Envelope? extentGeografico = null)
        {
            var graphics = new List<Graphic>();

            // 1. Halo exterior brillante de amplio alcance (36px)
            var haloExterior = new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(50, 0, 229, 255), // Cyan brillante traslúcido
                36.0);
            graphics.Add(new Graphic(point, haloExterior));

            // 2. Anillo medio de foco
            var anilloMedio = new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(110, 0, 229, 255),
                22.0)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.FromArgb(255, 0, 229, 255), 2.5)
            };
            graphics.Add(new Graphic(point, anilloMedio));

            // 3. Núcleo central de oro/ámbar brillante
            var nucleo = new SimpleMarkerSymbol(
                SimpleMarkerSymbolStyle.Circle,
                Color.FromArgb(255, 255, 215, 0), // Oro
                9.0)
            {
                Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.White, 2.0)
            };
            graphics.Add(new Graphic(point, nucleo));

            // 4. Polígono de huella/cobertura geográfica (si está disponible)
            if (extentGeografico != null &&
                extentGeografico.Width > 0 &&
                extentGeografico.Height > 0)
            {
                var footprintFill = new SimpleFillSymbol(
                    SimpleFillSymbolStyle.Solid,
                    Color.FromArgb(30, 0, 229, 255), // Relleno cyan traslúcido suave
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Dash, Color.FromArgb(230, 0, 229, 255), 2.0)
                );
                graphics.Add(new Graphic(extentGeografico, footprintFill));
            }

            return graphics;
        }
    }
}

