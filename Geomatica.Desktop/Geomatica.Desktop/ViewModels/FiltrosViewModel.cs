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
    }
}
