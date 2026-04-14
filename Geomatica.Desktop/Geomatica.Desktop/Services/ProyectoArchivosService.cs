using System;
using System.Collections.Generic;
using System.IO;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.Services
{
    public class ProyectoArchivosService
    {
        // Las carpetas inmutables que siempre deben existir
        private readonly string[] _carpetasBase = { 
            "Datos_Espaciales", 
            "Documentos", 
            "Entregables", 
            "Otros" 
        };

        /// <summary>
        /// Crea la estructura inicial en el servidor para un nuevo proyecto.
        /// </summary>
        public void CrearEstructuraProyecto(string rutaRaizProyecto)
        {
            try
            {
                if (!Directory.Exists(rutaRaizProyecto))
                {
                    Directory.CreateDirectory(rutaRaizProyecto);
                }

                foreach (var carpeta in _carpetasBase)
                {
                    string rutaCompleta = Path.Combine(rutaRaizProyecto, carpeta);
                    if (!Directory.Exists(rutaCompleta))
                    {
                        Directory.CreateDirectory(rutaCompleta);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // El usuario de AD actual no tiene permisos de escritura en la red
                throw new Exception("Su usuario no tiene permisos para crear carpetas en el servidor del proyecto.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al estructurar carpetas: {ex.Message}");
            }
        }

        /// <summary>
        /// Lista el contenido de una ruta virtual dada para mostrar en la interfaz. 
        /// </summary>
        public List<NodoArchivoVirtual> ListarContenidoVirtual(string rutaRaizProyecto, string rutaRelativa = "")
        {
            var nodos = new List<NodoArchivoVirtual>();
            string rutaFisica = Path.Combine(rutaRaizProyecto, rutaRelativa.TrimStart('/', '\\'));

            try
            {
                if (!Directory.Exists(rutaFisica))
                    return nodos;

                var dirInfo = new DirectoryInfo(rutaFisica);

                // Listar Carpetas
                foreach (var dir in dirInfo.GetDirectories())
                {
                    nodos.Add(new CarpetaVirtual
                    {
                        Nombre = dir.Name,
                        RutaRelativaVirtual = Path.Combine(rutaRelativa, dir.Name).Replace('\\', '/')
                    });
                }

                // Listar Archivos (Ligero, solo metadatos)
                foreach (var file in dirInfo.GetFiles())
                {
                    nodos.Add(new ArchivoVirtual
                    {
                        Nombre = file.Name,
                        RutaRelativaVirtual = Path.Combine(rutaRelativa, file.Name).Replace('\\', '/'),
                        TamanoBytes = file.Length,
                        FechaModificacion = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Capturamos el bloqueo de AD limpiamente para que la UI no colapse
                throw new Exception("Acceso denegado: Su usuario no tiene permisos para ver esta carpeta.");
            }

            return nodos;
        }
    }
}