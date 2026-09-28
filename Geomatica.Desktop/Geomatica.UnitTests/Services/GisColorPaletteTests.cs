using System;
using System.Drawing;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Desktop.Models;
using Geomatica.Infrastructure.Gis.Services;
using Xunit;

namespace Geomatica.UnitTests.Services
{
    public class GisColorPaletteTests
    {
        [Fact]
        public void Colores_Tiene16ColoresDistintivosYNoNegros()
        {
            // Arrange & Act
            var colores = GisColorPalette.Colores;

            // Assert
            Assert.Equal(16, colores.Count);

            foreach (var item in colores)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Hex));
                Assert.StartsWith("#", item.Hex);
                Assert.False(string.IsNullOrWhiteSpace(item.Nombre));

                // Ningún color de la paleta debe ser negro o cercano a negro
                // La suma de componentes RGB debe ser significativamente mayor a 100
                int sumaRgb = item.Color.R + item.Color.G + item.Color.B;
                Assert.True(sumaRgb > 120, $"El color {item.Nombre} ({item.Hex}) es demasiado oscuro para GIS.");

                // Ninguno debe ser Color.Black puro
                Assert.NotEqual(Color.Black.ToArgb(), item.Color.ToArgb());
            }
        }

        [Fact]
        public void ObtenerColor_CiclaCorrectamente()
        {
            // Act
            var c0 = GisColorPalette.ObtenerColor(0);
            var c16 = GisColorPalette.ObtenerColor(16);
            var c32 = GisColorPalette.ObtenerColor(32);
            var c1 = GisColorPalette.ObtenerColor(1);
            var c17 = GisColorPalette.ObtenerColor(17);

            // Assert
            Assert.Equal(c0.Hex, c16.Hex);
            Assert.Equal(c0.Hex, c32.Hex);
            Assert.Equal(c1.Hex, c17.Hex);
            Assert.NotEqual(c0.Hex, c1.Hex);
        }

        [Fact]
        public void ObtenerColor_ManejaIndicesNegativos()
        {
            // Act & Assert
            var cNeg = GisColorPalette.ObtenerColor(-1);
            Assert.NotNull(cNeg);
            Assert.Equal(GisColorPalette.ObtenerColor(1).Hex, cNeg.Hex);
        }

        [Fact]
        public void ObtenerSiguienteColor_IncrementaContadorYCicla()
        {
            // Arrange
            int contador = 0;

            // Act
            var primerColor = GisColorPalette.ObtenerSiguienteColor(ref contador);
            Assert.Equal(1, contador);

            var segundoColor = GisColorPalette.ObtenerSiguienteColor(ref contador);
            Assert.Equal(2, contador);

            // Assert
            Assert.NotEqual(primerColor.Hex, segundoColor.Hex);
        }

        [Fact]
        public void CrearRendererParaGeometria_Punto_GeneraMarcadorConBordeContraste()
        {
            // Arrange
            var color = Color.FromArgb(233, 30, 99); // Rosa

            // Act
            var renderer = GisColorPalette.CrearRendererParaGeometria(GeometryType.Point, color);

            // Assert
            Assert.NotNull(renderer);
            var marker = Assert.IsType<SimpleMarkerSymbol>(renderer.Symbol);
            Assert.Equal(SimpleMarkerSymbolStyle.Circle, marker.Style);
            Assert.Equal(color.R, marker.Color.R);
            Assert.Equal(color.G, marker.Color.G);
            Assert.Equal(color.B, marker.Color.B);
            Assert.NotNull(marker.Outline);
            Assert.Equal(Color.White.ToArgb(), marker.Outline.Color.ToArgb());
        }

        [Fact]
        public void CrearRendererParaGeometria_Linea_GeneraTrazoSolidoVisible()
        {
            // Arrange
            var color = Color.FromArgb(30, 136, 229); // Azul eléctrico

            // Act
            var renderer = GisColorPalette.CrearRendererParaGeometria(GeometryType.Polyline, color);

            // Assert
            Assert.NotNull(renderer);
            var line = Assert.IsType<SimpleLineSymbol>(renderer.Symbol);
            Assert.Equal(SimpleLineSymbolStyle.Solid, line.Style);
            Assert.Equal(2.5, line.Width);
            Assert.Equal(color.R, line.Color.R);
            Assert.Equal(color.G, line.Color.G);
            Assert.Equal(color.B, line.Color.B);
        }

        [Fact]
        public void CrearRendererParaGeometria_Poligono_GeneraRellenoTranslucidoYContorno()
        {
            // Arrange
            var color = Color.FromArgb(67, 160, 71); // Verde esmeralda

            // Act
            var renderer = GisColorPalette.CrearRendererParaGeometria(GeometryType.Polygon, color);

            // Assert
            Assert.NotNull(renderer);
            var fill = Assert.IsType<SimpleFillSymbol>(renderer.Symbol);
            Assert.Equal(SimpleFillSymbolStyle.Solid, fill.Style);
            // El canal alfa del relleno debe ser traslúcido (~70 de 255) para permitir ver el mapa base
            Assert.True(fill.Color.A < 150, "El relleno del polígono debe ser traslúcido para no tapar ortofotos ni mapas.");
            Assert.NotNull(fill.Outline);
            Assert.Equal(2.0, fill.Outline.Width);
        }

        [Fact]
        public void CapaUsuarioItem_ReflejaPropiedadesColorYBadgeDetalle()
        {
            // Arrange
            var item = new CapaUsuarioItem
            {
                Nombre = "Vias_Principales.shp",
                TipoGeometria = "Líneas",
                CantidadElementos = 142,
                ColorHex = "#FB8C00",
                ColorNombre = "Naranja Intenso",
                ColorSimbolo = Color.FromArgb(251, 140, 0)
            };

            // Assert
            Assert.True(item.HasColorSimbolo);
            Assert.Equal("#FB8C00", item.ColorBadgeBrush);
            Assert.Contains("Líneas", item.BadgeDetalle);
            Assert.Contains("142 elementos", item.BadgeDetalle);
            Assert.Contains("Naranja Intenso", item.BadgeDetalle);
        }

        [Fact]
        public void CapaUsuarioItem_SinColor_MuestraValoresPorDefecto()
        {
            // Arrange
            var item = new CapaUsuarioItem
            {
                Nombre = "Ortofoto_2026.tif",
                TipoGeometria = "Ráster",
                CantidadElementos = 1
            };

            // Assert
            Assert.False(item.HasColorSimbolo);
            Assert.Equal("#1976D2", item.ColorBadgeBrush);
            Assert.Equal("Ráster • 1 elementos", item.BadgeDetalle);
        }
    }
}

