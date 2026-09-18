using System;
using System.Collections.Generic;
using System.Drawing;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.UI;
using FluentAssertions;
using Geomatica.Desktop.Models;
using Xunit;

namespace Geomatica.UnitTests.Models
{
    public class CapaUsuarioItemLifecycleTests
    {
        [Fact]
        public void Dispose_LiberaBuffers3DYOvelaysGraficos()
        {
            // Arrange
            var overlayPuntos = new GraphicsOverlay();
            overlayPuntos.Graphics.Add(new Graphic(new MapPoint(0, 0, SpatialReferences.Wgs84)));

            var overlayGuia = new GraphicsOverlay();
            overlayGuia.Graphics.Add(new Graphic(new MapPoint(1, 1, SpatialReferences.Wgs84)));

            var puntos = new List<(MapPoint PtWgs84, Color Color)>
            {
                (new MapPoint(0, 0, 10, SpatialReferences.Wgs84), Color.Red),
                (new MapPoint(1, 1, 20, SpatialReferences.Wgs84), Color.Blue)
            };

            var item = new CapaUsuarioItem
            {
                Nombre = "TestLidar.las",
                OverlayPuntos3D = overlayPuntos,
                OverlayGuia3D = overlayGuia,
                PuntosMuestreados3D = puntos
            };

            // Act
            item.Dispose();

            // Assert
            item.PuntosMuestreados3D.Should().BeNull("el buffer de puntos debe ser liberado de memoria");
            item.OverlayPuntos3D.Should().BeNull("el overlay de puntos debe quedar desvinculado");
            item.OverlayGuia3D.Should().BeNull("el overlay de guía debe quedar desvinculado");
            overlayPuntos.Graphics.Should().BeEmpty("los gráficos del overlay deben limpiarse");
            overlayGuia.Graphics.Should().BeEmpty("los gráficos de la guía deben limpiarse");
        }

        [Fact]
        public void IsVisible_ActualizaOverlaysGraficosYEjecutaCallback()
        {
            // Arrange
            var overlayPuntos = new GraphicsOverlay();
            var overlayGuia = new GraphicsOverlay();
            bool callbackEjecutado = false;
            bool valorReportado = true;

            var item = new CapaUsuarioItem
            {
                OverlayPuntos3D = overlayPuntos,
                OverlayGuia3D = overlayGuia,
                OnVisibilityChangedAction = v =>
                {
                    callbackEjecutado = true;
                    valorReportado = v;
                }
            };

            // Act - Ocultar
            item.IsVisible = false;

            // Assert
            overlayPuntos.IsVisible.Should().BeFalse();
            overlayGuia.IsVisible.Should().BeFalse();
            callbackEjecutado.Should().BeTrue();
            valorReportado.Should().BeFalse();

            // Act - Mostrar
            item.IsVisible = true;
            overlayPuntos.IsVisible.Should().BeTrue();
            overlayGuia.IsVisible.Should().BeTrue();
            valorReportado.Should().BeTrue();
        }

        [Fact]
        public void Opacidad_ActualizaOverlaysGraficos()
        {
            // Arrange
            var overlayPuntos = new GraphicsOverlay();
            var overlayGuia = new GraphicsOverlay();

            var item = new CapaUsuarioItem
            {
                OverlayPuntos3D = overlayPuntos,
                OverlayGuia3D = overlayGuia,
                Opacidad = 1.0
            };

            // Act
            item.Opacidad = 0.65;

            // Assert
            overlayPuntos.Opacity.Should().BeApproximately(0.65, 0.001);
            overlayGuia.Opacity.Should().BeApproximately(0.65, 0.001);
        }
    }
}
