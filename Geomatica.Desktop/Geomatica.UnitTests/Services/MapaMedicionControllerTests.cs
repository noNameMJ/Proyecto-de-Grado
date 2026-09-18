using Esri.ArcGISRuntime.Geometry;
using Geomatica.Desktop.Services;

namespace Geomatica.UnitTests.Services;

public class MapaMedicionControllerTests
{
    [Fact]
    public void Limpiar_DebeRestablecerPuntosYGraficos()
    {
        // Arrange
        var controller = new MapaMedicionController();
        var punto = new MapPoint(-73.1198, 7.1396, SpatialReferences.Wgs84);
        controller.ProcesarNuevoPunto(punto, "Distancia");

        controller.Puntos.Should().HaveCount(1);
        controller.OverlayMedicion.Graphics.Should().NotBeEmpty();

        // Act
        controller.Limpiar();

        // Assert
        controller.Puntos.Should().BeEmpty();
        controller.OverlayMedicion.Graphics.Should().BeEmpty();
    }

    [Fact]
    public void ProcesarNuevoPunto_ConModoNinguno_NoDebeCalcularNiAgregar()
    {
        // Arrange
        var controller = new MapaMedicionController();
        var punto = new MapPoint(-73.1198, 7.1396, SpatialReferences.Wgs84);

        // Act
        var res = controller.ProcesarNuevoPunto(punto, "Ninguno");

        // Assert
        res.HasResultado.Should().BeFalse();
        controller.Puntos.Should().BeEmpty();
    }

    [Fact]
    public void ProcesarNuevoPunto_MedirDistancia_ConDosPuntos_DebeCalcularDistancia()
    {
        // Arrange
        var controller = new MapaMedicionController();
        var p1 = new MapPoint(-73.1198, 7.1396, SpatialReferences.Wgs84);
        var p2 = new MapPoint(-73.1188, 7.1406, SpatialReferences.Wgs84);

        // Act
        var res1 = controller.ProcesarNuevoPunto(p1, "Distancia");
        var res2 = controller.ProcesarNuevoPunto(p2, "Distancia");

        // Assert
        res1.Resultado.Should().Be("1 punto marcado");
        res2.HasResultado.Should().BeTrue();
        res2.Resultado.Should().MatchRegex(@"(m|km)");
        controller.Puntos.Should().HaveCount(2);
        // Debe haber 2 gráficos de puntos + 1 gráfico de la polilínea = 3 gráficos
        controller.OverlayMedicion.Graphics.Should().HaveCount(3);
    }

    [Fact]
    public void ProcesarNuevoPunto_MedirArea_ConTresPuntos_DebeCalcularSuperficie()
    {
        // Arrange
        var controller = new MapaMedicionController();
        var p1 = new MapPoint(-73.1200, 7.1300, SpatialReferences.Wgs84);
        var p2 = new MapPoint(-73.1100, 7.1300, SpatialReferences.Wgs84);
        var p3 = new MapPoint(-73.1100, 7.1400, SpatialReferences.Wgs84);

        // Act
        controller.ProcesarNuevoPunto(p1, "Area");
        controller.ProcesarNuevoPunto(p2, "Area");
        var resArea = controller.ProcesarNuevoPunto(p3, "Area");

        // Assert
        resArea.HasResultado.Should().BeTrue();
        resArea.Resultado.Should().MatchRegex(@"(m²|ha|km²)");
        controller.Puntos.Should().HaveCount(3);
        // Debe haber 3 gráficos de puntos + 1 gráfico del polígono = 4 gráficos
        controller.OverlayMedicion.Graphics.Should().HaveCount(4);
    }
}

