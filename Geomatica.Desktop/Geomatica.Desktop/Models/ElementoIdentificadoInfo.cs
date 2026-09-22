using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Geometry;

namespace Geomatica.Desktop.Models
{
    public partial class ElementoIdentificadoInfo : ObservableObject
    {
        public string NombreCapa { get; set; } = string.Empty;
        public string TituloElemento { get; set; } = string.Empty;
        public string TipoIcono { get; set; } = "📍";
        public Geometry? Geometria { get; set; }
        public IReadOnlyList<KeyValuePair<string, string>> Atributos { get; set; } = new List<KeyValuePair<string, string>>();

        public string? RutaGdbOrigen { get; set; }
        public string? GlobalId { get; set; }

        public ObservableCollection<AdjuntoFotoInfo> Adjuntos { get; } = new();

        [ObservableProperty]
        private bool isCargandoAdjuntos;

        [ObservableProperty]
        private bool hasAdjuntos;

        public string TotalAdjuntosTexto => $"{Adjuntos.Count} {(Adjuntos.Count == 1 ? "fotografía adjunta" : "fotografías adjuntas")}";

        public void NotificarAdjuntosCambiados()
        {
            HasAdjuntos = Adjuntos.Count > 0;
            OnPropertyChanged(nameof(TotalAdjuntosTexto));
        }
    }
}

