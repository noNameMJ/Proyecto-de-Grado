using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FiltrosViewModel : ObservableObject
    {
        [ObservableProperty] private string? palabraClave;
        [ObservableProperty] private DateTime? desde;
        [ObservableProperty] private DateTime? hasta;
        [ObservableProperty] private object? areaInteres;
        [ObservableProperty] private ProyectoItem? selectedProyecto;

        public ObservableCollection<object> Areas { get; } = new();

        public ObservableCollection<object> ResultadosResumen { get; } = new();
        public ObservableCollection<object> ResultadosLista { get; } = new();

        public IRelayCommand BuscarCommand { get; }
        public IRelayCommand DescargarCommand { get; }

        public event EventHandler? BuscarSolicitado;

        public FiltrosViewModel()
        {
            BuscarCommand = new RelayCommand(() => BuscarSolicitado?.Invoke(this, EventArgs.Empty));
            DescargarCommand = new RelayCommand(() => { /* placeholder */ });
        }

        public record DepartamentoItem(string Codigo, string Nombre)
        {
            public override string ToString() => Nombre;
        }

        public record ProyectoItem(int Id, string Titulo, double Lon, double Lat, string? Ruta)
        {
            public override string ToString() => Titulo;
        }
    }
}
