using System;
using System.IO;

namespace Geomatica.Desktop.Models
{
    public class AdjuntoFotoInfo
    {
        public int AttachmentId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string ContentType { get; set; } = "image/jpeg";
        public long TamanoBytes { get; set; }
        public string RutaArchivoLocal { get; set; } = string.Empty;

        public bool EsImagen
        {
            get
            {
                if (!string.IsNullOrEmpty(ContentType) && ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    return true;

                string ext = Path.GetExtension(Nombre).ToLowerInvariant();
                return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff";
            }
        }

        public string TamanoLegible
        {
            get
            {
                if (TamanoBytes < 1024)
                    return $"{TamanoBytes} B";
                if (TamanoBytes < 1024 * 1024)
                    return $"{TamanoBytes / 1024.0:F1} KB";
                return $"{TamanoBytes / (1024.0 * 1024.0):F2} MB";
            }
        }
    }
}

