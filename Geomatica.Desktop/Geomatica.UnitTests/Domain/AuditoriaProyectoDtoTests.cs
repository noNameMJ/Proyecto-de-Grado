using FluentAssertions;
using Geomatica.Domain.Entities;
using Xunit;

namespace Geomatica.UnitTests.Domain;

public class AuditoriaProyectoDtoTests
{
    [Theory]
    [InlineData("CREACION", "#1A5C34", "#E8F4EC", "#B2DFBF", "✨")]
    [InlineData("MODIFICACION", "#8A6707", "#FFF8E1", "#EBDCA8", "✏️")]
    [InlineData("ELIMINACION", "#C62828", "#FFEBEE", "#FFCDD2", "🗑️")]
    [InlineData("OTRO", "#4B5563", "#F3F4F6", "#E5E7EB", "📝")]
    public void AuditoriaProyectoDto_Acciones_DebenTenerEstilosEIconosApropiados(
        string accion,
        string colorEsperado,
        string bgEsperado,
        string borderEsperado,
        string iconoEsperado)
    {
        // Arrange
        var fecha = new DateTime(2026, 9, 21, 14, 30, 45, DateTimeKind.Utc);
        var dto = new AuditoriaProyectoDto(
            IdAuditoria: 1,
            IdProyecto: 10,
            TituloProyecto: "Proyecto Prueba",
            Accion: accion,
            Usuario: @"GEOMATICAAD\usuario.prueba",
            Equipo: "WS-UIS-01",
            FechaHora: fecha,
            Detalles: "Detalles de prueba"
        );

        // Assert
        dto.AccionBadgeColor.Should().Be(colorEsperado);
        dto.AccionBadgeBg.Should().Be(bgEsperado);
        dto.AccionBadgeBorder.Should().Be(borderEsperado);
        dto.AccionIcono.Should().Be(iconoEsperado);
        dto.FechaHoraTexto.Should().Be("21/09/2026 14:30:45");
        dto.Usuario.Should().Be(@"GEOMATICAAD\usuario.prueba");
        dto.Equipo.Should().Be("WS-UIS-01");
        dto.Detalles.Should().Be("Detalles de prueba");
    }
}

