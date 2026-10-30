namespace Geomatica.Domain.Entities;

public sealed record AuditoriaArchivoDto(
    int IdAuditoriaArchivo,
    int IdProyecto,
    string RutaRelativa,
    string NombreArchivo,
    string Accion,
    string Usuario,
    string? Equipo,
    DateTime FechaHora,
    long? TamanoBytes,
    string? Detalles,
    DateTime? FechaArchivo = null)
{
    public string FechaHoraTexto => FechaHora.ToString("dd/MM/yyyy HH:mm:ss");

    public string TamanoTexto
    {
        get
        {
            if (!TamanoBytes.HasValue || TamanoBytes.Value < 0) return "-";
            double bytes = TamanoBytes.Value;
            if (bytes < 1024) return $"{bytes:N0} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public string AccionBadgeColor => Accion.ToUpperInvariant() switch
    {
        "CREACION" or "CREADO" or "SUBIR" or "SUBIR_ARCHIVO" => "#1A5C34",
        "MODIFICACION" or "MODIFICADO" => "#8A6707",
        "ELIMINACION" or "ELIMINAR" or "ELIMINADO" => "#C62828",
        "MOVER" or "MOVIDO" => "#1565C0",
        "RENOMBRAR" or "RENOMBRADO" => "#E65100",
        "COPIAR" or "COPIADO" => "#6A1B9A",
        _ => "#4B5563"
    };

    public string AccionBadgeBg => Accion.ToUpperInvariant() switch
    {
        "CREACION" or "CREADO" or "SUBIR" or "SUBIR_ARCHIVO" => "#E8F4EC",
        "MODIFICACION" or "MODIFICADO" => "#FFF8E1",
        "ELIMINACION" or "ELIMINAR" or "ELIMINADO" => "#FFEBEE",
        "MOVER" or "MOVIDO" => "#E3F2FD",
        "RENOMBRAR" or "RENOMBRADO" => "#FFF3E0",
        "COPIAR" or "COPIADO" => "#F3E5F5",
        _ => "#F3F4F6"
    };

    public string AccionBadgeBorder => Accion.ToUpperInvariant() switch
    {
        "CREACION" or "CREADO" or "SUBIR" or "SUBIR_ARCHIVO" => "#B2DFBF",
        "MODIFICACION" or "MODIFICADO" => "#EBDCA8",
        "ELIMINACION" or "ELIMINAR" or "ELIMINADO" => "#FFCDD2",
        "MOVER" or "MOVIDO" => "#BBDEFB",
        "RENOMBRAR" or "RENOMBRADO" => "#FFE0B2",
        "COPIAR" or "COPIADO" => "#E1BEE7",
        _ => "#E5E7EB"
    };

    public string AccionIcono => Accion.ToUpperInvariant() switch
    {
        "CREACION" or "CREADO" => "✨",
        "SUBIR" or "SUBIR_ARCHIVO" => "⬆️",
        "MODIFICACION" or "MODIFICADO" => "✏️",
        "ELIMINACION" or "ELIMINAR" or "ELIMINADO" => "🗑️",
        "MOVER" or "MOVIDO" => "🔄",
        "RENOMBRAR" or "RENOMBRADO" => "🏷️",
        "COPIAR" or "COPIADO" => "📋",
        _ => "📝"
    };

    public bool EsEliminado =>
        Accion.Equals("ELIMINAR", StringComparison.OrdinalIgnoreCase) ||
        Accion.Equals("ELIMINACION", StringComparison.OrdinalIgnoreCase) ||
        Accion.Equals("ELIMINADO", StringComparison.OrdinalIgnoreCase);
}

