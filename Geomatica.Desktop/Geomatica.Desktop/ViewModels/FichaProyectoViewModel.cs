using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FichaProyectoViewModel : ObservableObject
    {
        private readonly INotificationService? _notifications;

        public ProyectoDetalleDto Proyecto { get; }

        public int Id => Proyecto.Id;
        public string CodigoId => $"#PROY-{Proyecto.Id:D4}";
        public string Titulo => Proyecto.Titulo;
        public string? Descripcion => Proyecto.Descripcion;
        public string FechaTexto => Proyecto.Fecha?.ToString("dd/MM/yyyy") ?? "Sin fecha";
        public string? PalabraClave => Proyecto.PalabraClave;
        public string? RutaArchivos => Proyecto.RutaArchivos;
        public string Coordenadas => $"{Proyecto.Lat:F6}°N, {Proyecto.Lon:F6}°W";
        public string CoordenadasFormato => $"{Proyecto.Lat:F6}, {Proyecto.Lon:F6}";
        public string LatitudTexto => $"{Math.Abs(Proyecto.Lat):F6}° {(Proyecto.Lat >= 0 ? "N" : "S")}";
        public string LongitudTexto => $"{Math.Abs(Proyecto.Lon):F6}° {(Proyecto.Lon >= 0 ? "E" : "W")}";
        public string? MunicipioNombre => Proyecto.MunicipioNombre;
        public string? MunicipioCodigo => Proyecto.MunicipioCodigo;

        public bool HasDescripcion => !string.IsNullOrWhiteSpace(Proyecto.Descripcion);
        public bool HasMunicipio => !string.IsNullOrWhiteSpace(Proyecto.MunicipioNombre);
        public bool HasRutaArchivos => !string.IsNullOrWhiteSpace(Proyecto.RutaArchivos);
        public bool RutaExiste => HasRutaArchivos && Directory.Exists(Proyecto.RutaArchivos);
        public string EstadoRutaTexto => RutaExiste ? "Carpeta vinculada" : (HasRutaArchivos ? "Ruta inaccesible" : "Sin carpeta");

        public IReadOnlyList<string> PalabrasClaveLista { get; }
        public bool HasPalabrasClave => PalabrasClaveLista.Count > 0;

        public IRelayCommand VolverCommand { get; }
        public IRelayCommand EditarCommand { get; }
        public IRelayCommand AbrirCarpetaCommand { get; }
        public IRelayCommand CopiarCoordenadasCommand { get; }
        public IRelayCommand CopiarRutaCommand { get; }

        public event EventHandler<ProyectoDetalleDto>? EditarSolicitado;

        public FichaProyectoViewModel(ProyectoDetalleDto proyecto, Action volverAction, INotificationService? notifications = null)
        {
            Proyecto = proyecto;
            _notifications = notifications;

            VolverCommand = new RelayCommand(volverAction);
            EditarCommand = new RelayCommand(() => EditarSolicitado?.Invoke(this, Proyecto));
            AbrirCarpetaCommand = new RelayCommand(AbrirCarpeta, () => !string.IsNullOrWhiteSpace(RutaArchivos));
            CopiarCoordenadasCommand = new RelayCommand(CopiarCoordenadas);
            CopiarRutaCommand = new RelayCommand(CopiarRuta, () => !string.IsNullOrWhiteSpace(RutaArchivos));

            if (!string.IsNullOrWhiteSpace(proyecto.PalabraClave))
            {
                PalabrasClaveLista = proyecto.PalabraClave
                    .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                PalabrasClaveLista = Array.Empty<string>();
            }
        }

        private void CopiarCoordenadas()
        {
            try
            {
                Clipboard.SetText(CoordenadasFormato);
                _notifications?.ShowSuccess($"Coordenadas copiadas: {CoordenadasFormato}", "Copiado");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error copiando coordenadas: {ex}");
            }
        }

        private void CopiarRuta()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos)) return;
            try
            {
                Clipboard.SetText(RutaArchivos);
                _notifications?.ShowSuccess("Ruta de carpeta copiada al portapapeles.", "Copiado");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error copiando ruta: {ex}");
            }
        }

        private void AbrirCarpeta()
        {
            if (string.IsNullOrWhiteSpace(RutaArchivos)) return;
            try
            {
                if (!Directory.Exists(RutaArchivos))
                {
                    _notifications?.ShowWarning("La carpeta física configurada no existe o no es accesible.", "Carpeta no encontrada");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = RutaArchivos,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FichaProyecto] Error abriendo carpeta: {ex}");
                _notifications?.ShowError($"No se pudo abrir la carpeta: {ex.Message}", "Error");
            }
        }
    }
}
