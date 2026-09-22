using System;

namespace Geomatica.Desktop.Models
{
    public class GdbCapaInfo
    {
        public string Nombre { get; set; } = string.Empty;
        public string TipoGeometria { get; set; } = string.Empty;
        public long CantidadElementos { get; set; }
        public string CrsNombre { get; set; } = string.Empty;
        public bool EsTabla { get; set; }
        public bool EsAdjunto { get; set; }
        public bool EsRelacion { get; set; }
        public string? TablaOrigen { get; set; }
        public string? TablaDestino { get; set; }
        public string? Cardinalidad { get; set; }
        public string RutaGdb { get; set; } = string.Empty;

        public string Icono => EsRelacion ? "🔗" : EsAdjunto ? "📎" : EsTabla ? "📋" : TipoGeometria.ToLowerInvariant() switch
        {
            "point" or "punto" or "puntos" or "multipoint" => "📍",
            "polyline" or "line" or "línea" or "lineas" => "📏",
            "polygon" or "polígono" or "poligonos" => "⬡",
            _ => "🗺️"
        };

        public string TipoGeometriaTexto => EsRelacion ? "Clase de Relación" : EsAdjunto ? "Tabla de Adjuntos" : EsTabla ? "Tabla No Espacial" : TipoGeometria.ToLowerInvariant() switch
        {
            "point" or "punto" or "multipoint" => "Puntos",
            "polyline" or "line" => "Líneas",
            "polygon" or "polígono" => "Polígonos",
            _ => TipoGeometria
        };

        public string DetalleTexto => EsRelacion
            ? (!string.IsNullOrWhiteSpace(TablaOrigen) && !string.IsNullOrWhiteSpace(TablaDestino)
                ? $"Relación {Cardinalidad ?? "1:N"} ({TablaOrigen} ➔ {TablaDestino})"
                : $"Relación {Cardinalidad ?? "1:N"}")
            : EsAdjunto
                ? $"{CantidadElementos:N0} fotos / archivos adjuntos"
                : EsTabla
                    ? $"{CantidadElementos:N0} registros"
                    : $"{TipoGeometriaTexto} • {CantidadElementos:N0} elementos";

        public string DetalleCompleto => string.IsNullOrWhiteSpace(CrsNombre)
            ? DetalleTexto
            : $"{DetalleTexto} • {CrsNombre}";
    }
}

