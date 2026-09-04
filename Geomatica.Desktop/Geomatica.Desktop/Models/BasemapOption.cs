using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Mapping;

namespace Geomatica.Desktop.Models
{
    public partial class BasemapOption : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Icono { get; set; } = "🗺️";
        public string Descripcion { get; set; } = "";
        public BasemapStyle? Style { get; set; }

        [ObservableProperty]
        private bool isDefaultOffline;

        [ObservableProperty]
        private bool isOfflineAvailable;

        [ObservableProperty]
        private string? offlinePackagePath;

        [ObservableProperty]
        private bool isSeleccionado;

        [ObservableProperty]
        private bool isHabilitado = true;

        [ObservableProperty]
        private string estadoTexto = "En línea";
    }
}
