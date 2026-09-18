using Geomatica.Desktop.Models;

namespace Geomatica.UnitTests.Models;

public class NodoArchivoVirtualTests
{
    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1,0 KB")]
    [InlineData(1024 * 1024, "1,0 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5,00 GB")]
    public void TamanoTexto_DebeFormatearSegunEscala(long bytes, string formatoEsperadoConComa)
    {
        // Arrange
        var archivo = new ArchivoVirtual
        {
            Nombre = "terreno.las",
            TamanoBytes = bytes
        };

        // Act
        var texto = archivo.TamanoTexto;

        // Assert (soportar tanto separador coma como punto dependiendo de la cultura regional)
        texto.Replace('.', ',').Should().Be(formatoEsperadoConComa);
    }

    [Fact]
    public void CarpetaVirtual_DebeTenerPropiedadesEsperadas()
    {
        // Arrange & Act
        var carpeta = new CarpetaVirtual
        {
            Nombre = "Ortomosaicos",
            RutaRelativaVirtual = "/Ortomosaicos"
        };

        // Assert
        carpeta.EsCarpeta.Should().BeTrue();
        carpeta.Extension.Should().Be("Carpeta");
        carpeta.TamanoTexto.Should().BeEmpty();
        carpeta.Hijos.Should().NotBeNull();
        carpeta.Hijos.Should().BeEmpty();
    }
}

