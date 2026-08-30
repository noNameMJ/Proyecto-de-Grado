using System;
using System.Collections.Generic;

namespace Geomatica.Desktop.Models
{
    public abstract class NodoArchivoVirtual
    {
        public string Nombre { get; set; } = string.Empty;
        // La ruta relativa es lo único que conocerá la UI (ej. "/Documentos/informe.pdf")
        public string RutaRelativaVirtual { get; set; } = string.Empty;
        public bool EsCarpeta { get; set; }
    }

    public class CarpetaVirtual : NodoArchivoVirtual
    {
        public CarpetaVirtual() { EsCarpeta = true; }
        // Útil si quieres cargar el árbol completo de una vez (aunque para Lazy Load lo manejaríamos distinto)
        public List<NodoArchivoVirtual> Hijos { get; set; } = new();

        public string TamanoTexto => "";
        public string FechaTexto => "";
        public string Extension => "Carpeta";
    }

    public class ArchivoVirtual : NodoArchivoVirtual
    {
        public ArchivoVirtual() { EsCarpeta = false; }
        public long TamanoBytes { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Extension { get; set; } = string.Empty;

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