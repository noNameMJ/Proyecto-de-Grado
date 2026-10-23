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
        "ELIMINACION" or "ELIMINAR" => "#C62828",
        "MOVER" => "#1565C0",
        "RENOMBRAR" => "#E65100",
        "CREAR_CARPETA" => "#2E7D32",
        "SUBIR_ARCHIVO" or "SUBIR" => "#00838F",
        "COPIAR" => "#6A1B9A",
        _ => "#4B5563"
    };

    public string AccionBadgeBg => Accion switch
    {
        "CREACION" => "#E8F4EC",
        "MODIFICACION" => "#FFF8E1",
        "ELIMINACION" or "ELIMINAR" => "#FFEBEE",
        "MOVER" => "#E3F2FD",
        "RENOMBRAR" => "#FFF3E0",
        "CREAR_CARPETA" => "#E8F5E9",
        "SUBIR_ARCHIVO" or "SUBIR" => "#E0F7FA",
        "COPIAR" => "#F3E5F5",
        _ => "#F3F4F6"
    };

    public string AccionBadgeBorder => Accion switch
    {
        "CREACION" => "#B2DFBF",
        "MODIFICACION" => "#EBDCA8",
        "ELIMINACION" or "ELIMINAR" => "#FFCDD2",
        "MOVER" => "#BBDEFB",
        "RENOMBRAR" => "#FFE0B2",
        "CREAR_CARPETA" => "#C8E6C9",
        "SUBIR_ARCHIVO" or "SUBIR" => "#B2EBF2",
        "COPIAR" => "#E1BEE7",
        _ => "#E5E7EB"
    };

    public string AccionIcono => Accion switch
    {
        "CREACION" => "✨",
        "MODIFICACION" => "✏️",
        "ELIMINACION" or "ELIMINAR" => "🗑️",
        "MOVER" => "🔄",
        "RENOMBRAR" => "🏷️",
        "CREAR_CARPETA" => "📁",
        "SUBIR_ARCHIVO" or "SUBIR" => "⬆️",
        "COPIAR" => "📋",
        _ => "📝"
    };
}
