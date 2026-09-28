using System;

namespace Geomatica.Desktop.Models
{
    public class CadCapaInfo
    {
        public string Nombre { get; set; } = string.Empty;
        public string TipoGeometria { get; set; } = string.Empty;
        public long CantidadElementos { get; set; }
        public string CrsNombre { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#0D47A1";
        public bool EsVisible { get; set; } = true;
        public string RutaArchivoCad { get; set; } = string.Empty;

        public string Icono => TipoGeometria.ToLowerInvariant() switch
        {
            "point" or "punto" or "puntos" or "multipoint" => "📍",
            "polyline" or "line" or "línea" or "lineas" => "📏",
            "polygon" or "polígono" or "poligonos" => "⬡",
            "text" or "texto" or "annotation" or "anotaciones" => "🔤",
            _ => "📐"
        };

        public string TipoGeometriaTexto => TipoGeometria.ToLowerInvariant() switch
        {
            "point" or "punto" or "multipoint" => "Puntos",
            "polyline" or "line" => "Líneas",
            "polygon" or "polígono" => "Polígonos",
            "text" or "texto" or "annotation" => "Anotaciones / Textos",
            _ => TipoGeometria
        };

        public string DetalleTexto => $"{TipoGeometriaTexto} • {CantidadElementos:N0} elementos";

        public string DetalleCompleto => string.IsNullOrWhiteSpace(CrsNombre)
            ? DetalleTexto
            : $"{DetalleTexto} • {CrsNombre}";
    }
}

