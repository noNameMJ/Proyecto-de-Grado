using System;
using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using FluentAssertions;
using Xunit;

namespace Geomatica.UnitTests.Services;

public class ACadSharpSmokeTests
{
    [Fact]
    public void ACadSharp_PuedeInstanciarDocumentoVacio()
    {
        var doc = new CadDocument();
        doc.Should().NotBeNull();
        doc.Entities.Should().NotBeNull();
        doc.Layers.Should().NotBeNull();
    }

    [Fact]
    public void ACadSharp_PuedeCrearYLeerEntidadesBasicas()
    {
        var doc = new CadDocument();
        var layerVias = new ACadSharp.Tables.Layer("VIAS");
        doc.Layers.Add(layerVias);

        var linea = new Line
        {
            StartPoint = new CSMath.XYZ(10, 20, 0),
            EndPoint = new CSMath.XYZ(30, 40, 0),
            Layer = layerVias
        };
        doc.Entities.Add(linea);

        var punto = new Point
        {
            Location = new CSMath.XYZ(15, 25, 0),
            Layer = layerVias
        };
        doc.Entities.Add(punto);

        doc.Entities.Should().HaveCount(2);
    }

    [Fact]
    public void ACadSharp_PuedeEscribirYLeerDxfRoundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_cad_{Guid.NewGuid():N}.dxf");
        try
        {
            var doc = new CadDocument();
            var layerPredios = new ACadSharp.Tables.Layer("PREDIOS");
            doc.Layers.Add(layerPredios);

            var poly = new LwPolyline
            {
                Layer = layerPredios,
                IsClosed = true
            };
            poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(0, 0)));
            poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(10, 0)));
            poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(10, 10)));
            poly.Vertices.Add(new LwPolyline.Vertex(new CSMath.XY(0, 10)));
            doc.Entities.Add(poly);

            using (var writer = new DxfWriter(tempFile, doc, false))
            {
                writer.Write();
            }

            File.Exists(tempFile).Should().BeTrue();
            new FileInfo(tempFile).Length.Should().BeGreaterThan(0);

            using var reader = new DxfReader(tempFile);
            var readDoc = reader.Read();
            readDoc.Should().NotBeNull();
            readDoc.Entities.Should().HaveCount(1);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void ACadSharp_VerificaPropiedadesEntidadesAvanzadas()
    {
        var doc = new CadDocument();
        var circle = new Circle
        {
            Center = new CSMath.XYZ(100, 200, 0),
            Radius = 25.5
        };
        doc.Entities.Add(circle);

        var arc = new Arc
        {
            Center = new CSMath.XYZ(300, 400, 0),
            Radius = 15.0,
            StartAngle = 0.0,
            EndAngle = Math.PI / 2
        };
        doc.Entities.Add(arc);

        var text = new TextEntity
        {
            Value = "Texto Predio",
            InsertPoint = new CSMath.XYZ(50, 60, 0),
            Height = 2.5,
            Rotation = 0.0
        };
        doc.Entities.Add(text);

        var mtext = new MText
        {
            Value = "MText Bloque",
            InsertPoint = new CSMath.XYZ(70, 80, 0),
            Height = 3.0
        };
        doc.Entities.Add(mtext);

        doc.Entities.Should().HaveCount(4);

        var col = new ACadSharp.Color(255, 128, 64);
        col.R.Should().Be(255);
        col.G.Should().Be(128);
        col.B.Should().Be(64);
    }
}
