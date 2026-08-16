using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Geomatica.Domain.Entities
{
    public class ProyectoGeomatico
    {
        public int Id { get; set; }
        public string Titulo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string PalabrasClave { get; set; } = "";
        public string Responsable { get; set; } = "";
        public string RutaArchivos { get; set; } = "";
        public string TipoRecurso { get; set; } = ""; // LiDAR, Orto, Modelo3D
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double Longitud { get; set; }
        public double Latitud { get; set; }
    }

}
