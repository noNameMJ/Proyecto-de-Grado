namespace Geomatica.Domain.Entities;

public sealed record AuditoriaProyectoDto(
    int IdAuditoria,
    int? IdProyecto,
    string TituloProyecto,
    string Accion,
    string Usuario,
    string? Equipo,
    DateTime FechaHora,
    string? Detalles)
{
    public string FechaHoraTexto => FechaHora.ToString("dd/MM/yyyy HH:mm:ss");

    public string AccionBadgeColor => Accion switch
    {
        "CREACION" => "#1A5C34",
        "MODIFICACION" => "#8A6707",
        "ELIMINACION" => "#C62828",
        _ => "#4B5563"
    };

    public string AccionBadgeBg => Accion switch
    {
        "CREACION" => "#E8F4EC",
        "MODIFICACION" => "#FFF8E1",
        "ELIMINACION" => "#FFEBEE",
        _ => "#F3F4F6"
    };

    public string AccionBadgeBorder => Accion switch
    {
        "CREACION" => "#B2DFBF",
        "MODIFICACION" => "#EBDCA8",
        "ELIMINACION" => "#FFCDD2",
        _ => "#E5E7EB"
    };

    public string AccionIcono => Accion switch
    {
        "CREACION" => "✨",
        "MODIFICACION" => "✏️",
        "ELIMINACION" => "🗑️",
        _ => "📝"
    };
}
