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
    }

    public class ArchivoVirtual : NodoArchivoVirtual
    {
        public ArchivoVirtual() { EsCarpeta = false; }
        public long TamanoBytes { get; set; }
        public DateTime FechaModificacion { get; set; }
        public string Extension { get; set; } = string.Empty;
    }
}