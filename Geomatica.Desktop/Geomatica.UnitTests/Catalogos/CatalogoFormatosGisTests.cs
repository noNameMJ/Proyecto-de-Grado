using FluentAssertions;
using Geomatica.Domain.Catalogos;
using Xunit;

namespace Geomatica.UnitTests.Catalogos;

public class CatalogoFormatosGisTests
{
    [Theory]
    [InlineData("shp", ".shp")]
    [InlineData(".SHP", ".shp")]
    [InlineData("  .TIF  ", ".tif")]
    [InlineData(".laz", ".laz")]
    [InlineData("dwg", ".dwg")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizarExtension_DebeEstandarizarCorrectamente(string? input, string esperado)
    {
        CatalogoFormatosGis.NormalizarExtension(input).Should().Be(esperado);
        input.NormalizarExtension().Should().Be(esperado);
    }

    [Theory]
    [InlineData(".shp", CatalogoFormatosGis.CategoriaVectorial)]
    [InlineData(".gpkg", CatalogoFormatosGis.CategoriaVectorial)]
    [InlineData(".tif", CatalogoFormatosGis.CategoriaRaster)]
    [InlineData(".img", CatalogoFormatosGis.CategoriaRaster)]
    [InlineData(".las", CatalogoFormatosGis.CategoriaLidar)]
    [InlineData(".laz", CatalogoFormatosGis.CategoriaLidar)]
    [InlineData(".dwg", CatalogoFormatosGis.CategoriaCad)]
    [InlineData(".pdf", CatalogoFormatosGis.CategoriaDocumentacion)]
    [InlineData(".zip", CatalogoFormatosGis.CategoriaComprimidos)]
    [InlineData(".prj", CatalogoFormatosGis.CategoriaAuxiliar)]
    [InlineData(".unknown_ext", CatalogoFormatosGis.CategoriaOtros)]
    public void ObtenerCategoria_DebeClasificarFormatoSegunCatalogo(string extension, string categoriaEsperada)
    {
        CatalogoFormatosGis.ObtenerCategoria(extension).Should().Be(categoriaEsperada);
    }

    [Fact]
    public void EsEspacial_DebeIdentificarFormatosGis()
    {
        CatalogoFormatosGis.EsEspacial(".shp").Should().BeTrue();
        CatalogoFormatosGis.EsEspacial(".tif").Should().BeTrue();
        CatalogoFormatosGis.EsEspacial(".las").Should().BeTrue();
        CatalogoFormatosGis.EsEspacial(".dwg").Should().BeTrue();
        CatalogoFormatosGis.EsEspacial(".pdf").Should().BeFalse();
        CatalogoFormatosGis.EsEspacial(".zip").Should().BeFalse();
    }

    [Fact]
    public void ObtenerExtensionesPorCategoria_DebeRetornarConjuntosConsistentes()
    {
        var extsVector = CatalogoFormatosGis.ObtenerExtensionesPorCategoria("vectorial");
        extsVector.Should().Contain(".shp");
        extsVector.Should().Contain(".gpkg");

        var extsRaster = CatalogoFormatosGis.ObtenerExtensionesPorCategoria("raster");
        extsRaster.Should().Contain(".tif");
        extsRaster.Should().Contain(".ecw");

        var extsLidar = CatalogoFormatosGis.ObtenerExtensionesPorCategoria("lidar");
        extsLidar.Should().Contain(".las");
        extsLidar.Should().Contain(".laz");

        var extsDocs = CatalogoFormatosGis.ObtenerExtensionesPorCategoria("docs");
        extsDocs.Should().Contain(".pdf");
        extsDocs.Should().Contain(".zip");
    }

    [Theory]
    [InlineData(null, true, "📁")]
    [InlineData(".gdb", false, "🗃️")]
    [InlineData(".shp", false, "🗺️")]
    [InlineData(".tif", false, "🛰️")]
    [InlineData(".las", false, "☁️")]
    [InlineData(".dwg", false, "📐")]
    [InlineData(".pdf", false, "📕")]
    [InlineData(".zip", false, "🗜️")]
    [InlineData(".prj", false, "📎")]
    [InlineData(".xyz_raro", false, "📄")]
    public void ObtenerIcono_DebeAsignarEmojiCanónico(string? ext, bool esCarpeta, string iconoEsperado)
    {
        CatalogoFormatosGis.ObtenerIcono(ext, esCarpeta).Should().Be(iconoEsperado);
    }
}
