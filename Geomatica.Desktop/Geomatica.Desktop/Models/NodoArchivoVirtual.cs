using System;
using System.Collections.Generic;

namespace Geomatica.Desktop.Models
{
    public abstract class NodoArchivoVirtual
    {
        public string Nombre { get; set; } = string.Empty;
        // La ruta relativa es lo único que conocerá la UI (ej. "/Documentos/informe.pdf")
        public string RutaRelativaVirtual { get; set; } = string.Empty;
        // La carpeta o subdirectorio relativo donde reside el archivo (ej. "Datos_Espaciales/Ortofotos")
        public string UbicacionRelativa { get; set; } = string.Empty;
        public bool EsCarpeta { get; set; }
        public virtual string Icono => EsCarpeta ? "📁" : "📄";
    }

    public class CarpetaVirtual : NodoArchivoVirtual
    {
        public CarpetaVirtual() { EsCarpeta = true; }
        // Útil si quieres cargar el árbol completo de una vez (aunque para Lazy Load lo manejaríamos distinto)
        public List<NodoArchivoVirtual> Hijos { get; set; } = new();

        public string TamanoTexto => "";
        public string FechaTexto => "";
        public string Extension => "Carpeta";
        public override string Icono => "📁";
    }

    public class ArchivoVirtual : NodoArchivoVirtual
    {
        public ArchivoVirtual() { EsCarpeta = false; }
        public long TamanoBytes { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Extension { get; set; } = string.Empty;

        public override string Icono
        {
            get
            {
                var ext = (Extension ?? string.Empty).Trim().ToLowerInvariant();
                if (!ext.StartsWith(".") && ext.Length > 0) ext = "." + ext;
                return ext switch
                {
                    ".gdb" or ".geodatabase" => "🗃️",
                    ".shp" or ".gpkg" or ".geojson" or ".kml" or ".kmz" or ".tab" or ".mif" or ".gml" => "🗺️",
                    ".tif" or ".tiff" or ".img" or ".dem" or ".asc" or ".ecw" or ".jp2" or ".sid" => "🛰️",
                    ".las" or ".laz" or ".zlas" or ".xyz" or ".pts" or ".ply" => "☁️",
                    ".dwg" or ".dxf" or ".dgn" => "📐",
                    ".pdf" => "📕",
                    ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "🗜️",
                    ".xlsx" or ".xls" or ".csv" => "📊",
                    ".doc" or ".docx" or ".txt" => "📝",
                    _ => "📄"
                };
            }
        }

        public string TamanoTexto => TamanoBytes switch
        {
            < 1024 => $"{TamanoBytes} B",
            < 1024 * 1024 => $"{TamanoBytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{TamanoBytes / (1024.0 * 1024):F1} MB",
            _ => $"{TamanoBytes / (1024.0 * 1024 * 1024):F2} GB"
        };

        public string FechaTexto => FechaModificacion.ToString("dd/MM/yyyy HH:mm");
    }
}