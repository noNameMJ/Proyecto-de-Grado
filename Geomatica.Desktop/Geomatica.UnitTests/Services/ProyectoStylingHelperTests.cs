using System;
using System.Drawing;
using System.Threading.Tasks;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Symbology;
using Geomatica.Desktop.ViewModels;
using Geomatica.Infrastructure.Gis.Services;
using Xunit;

namespace Geomatica.UnitTests.Services
{
    public class ProyectoStylingHelperTests
    {
        [Fact]
        public async Task ObtenerSimboloProyectoAsync_RetornaSimboloValido()
        {
            var simbolo = await ProyectoStylingHelper.ObtenerSimboloProyectoAsync();

            Assert.NotNull(simbolo);
            Assert.True(simbolo is PictureMarkerSymbol or SimpleMarkerSymbol);
        }

        [Fact]
        public void CrearSimboloFallback_GeneraMarcadorVerdeUisConContornoBlanco()
        {
            var fallback = ProyectoStylingHelper.CrearSimboloFallback();

            Assert.NotNull(fallback);
            Assert.Equal(SimpleMarkerSymbolStyle.Circle, fallback.Style);
            Assert.Equal(Color.FromArgb(27, 94, 32).ToArgb(), fallback.Color.ToArgb());
            Assert.NotNull(fallback.Outline);
            Assert.Equal(Color.White.ToArgb(), fallback.Outline.Color.ToArgb());
            Assert.True(fallback.Outline.Width >= 2.0);
        }

        [Fact]
        public async Task CrearRendererProyectosAsync_RetornaRendererConSimbolo()
        {
            var renderer = await ProyectoStylingHelper.CrearRendererProyectosAsync();

            Assert.NotNull(renderer);
            Assert.NotNull(renderer.Symbol);
        }

        [Fact]
        public void CrearEtiquetaGraphic_ConfiguraTextoConHaloDeContraste()
        {
            var pt = new MapPoint(-73.12, 7.14, SpatialReferences.Wgs84);
            var graphic = ProyectoStylingHelper.CrearEtiquetaGraphic(pt, "#PROY-0001", "Campus Central UIS");

            Assert.NotNull(graphic);
            Assert.Same(pt, graphic.Geometry);
            var textSymbol = Assert.IsType<TextSymbol>(graphic.Symbol);
            Assert.Contains("#PROY-0001", textSymbol.Text);
            Assert.Contains("Campus Central UIS", textSymbol.Text);
            Assert.True(textSymbol.HaloWidth >= 1.5, "El halo debe ser de al menos 1.5px para legibilidad sobre satélite.");
            Assert.Equal(Color.FromArgb(245, 255, 255, 255).ToArgb(), textSymbol.HaloColor.ToArgb());
        }

        [Fact]
        public void CrearResaltadoGraphics_GeneraHaloYNucleo_YPoligonoSiExisteExtent()
        {
            var pt = new MapPoint(-73.12, 7.14, SpatialReferences.Wgs84);

            // 1. Sin extent
            var sinExtent = ProyectoStylingHelper.CrearResaltadoGraphics(pt, null);
            Assert.Equal(3, sinExtent.Count);

            // 2. Con extent válido
            var env = new Envelope(-73.15, 7.10, -73.10, 7.15, SpatialReferences.Wgs84);
            var conExtent = ProyectoStylingHelper.CrearResaltadoGraphics(pt, env);
            Assert.Equal(4, conExtent.Count);

            // El último graphic debe ser el polígono de footprint con relleno traslúcido
            var footprintGraphic = conExtent[3];
            Assert.Same(env, footprintGraphic.Geometry);
            var fill = Assert.IsType<SimpleFillSymbol>(footprintGraphic.Symbol);
            Assert.True(fill.Color.A < 100, "El polígono de huella debe ser traslúcido para no tapar el mapa.");
        }

        [Fact]
        public void ProyectoItem_FormateaPropiedadesVisualesConMetadatosGenuinos()
        {
            var item = new FiltrosViewModel.ProyectoItem(
                42,
                "Levantamiento Cuenca Río Lebrija",
                -73.18,
                7.22,
                @"C:\Proyectos\VueloLebrija",
                new DateTime(2026, 4, 15),
                "cuenca, rio, topografia",
                "Ing. María Gómez",
                -73.25, 7.15, -73.10, 7.30);

            Assert.Equal("#PROY-0042", item.CodigoId);
            Assert.Equal("15/04/2026", item.FechaTexto);
            Assert.True(item.HasPalabrasClave);
            Assert.Equal("cuenca, rio, topografia", item.PalabrasClaveTexto);
            Assert.True(item.TieneExtentValido);
            Assert.Contains("7.2200°N", item.CoordenadasTexto);
            Assert.Contains("-73.1800°W", item.CoordenadasTexto);
        }

        [Fact]
        public void ProyectoItem_SinExtent_TieneExtentValidoEsFalso()
        {
            var item = new FiltrosViewModel.ProyectoItem(
                1,
                "Proyecto Punto Fijo",
                -73.12,
                7.14,
                null);

            Assert.False(item.TieneExtentValido);
            Assert.Equal("#PROY-0001", item.CodigoId);
            Assert.Equal("Sin fecha", item.FechaTexto);
            Assert.False(item.HasPalabrasClave);
        }
    }
}

