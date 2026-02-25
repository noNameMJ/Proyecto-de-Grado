using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using System.Diagnostics;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FichaProyectoViewModel : ObservableObject
    {
        public ProyectoDetalleDto Proyecto { get; }

        public string Titulo => Proyecto.Titulo;
        public string? Descripcion => Proyecto.Descripcion;
        public string FechaTexto => Proyecto.Fecha?.ToString("dd/MM/yyyy") ?? "Sin fecha";
        public string? PalabraClave => Proyecto.PalabraClave;
        public string? RutaArchivos => Proyecto.RutaArchivos;
        public string Coordenadas => $"{Proyecto.Lat:F6}°N, {Proyecto.Lon:F6}°W";
        public string? MunicipioNombre => Proyecto.MunicipioNombre;

        public IRelayCommand VolverCommand { get; }
        public IRelayCommand EditarCommand { get; }
        public IRelayCommand AbrirCarpetaCommand { get; }

        public event EventHandler<ProyectoDetalleDto>? EditarSolicitado;

        public FichaProyectoViewModel(ProyectoDetalleDto proyecto, Action volverAction)
        {
            Proyecto = proyecto;
            VolverCommand = new RelayCommand(volverAction);
            EditarCommand = new RelayCommand(() => EditarSolicitado?.Invoke(this, Proyecto));
            AbrirCarpetaCommand = new RelayCommand(AbrirCarpeta, () => !string.IsNullOrWhiteSpace(RutaArchivos));
        }

        private void AbrirCarpeta()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = RutaArchivos,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error abriendo carpeta: {ex}");
            }
        }
    }
}
