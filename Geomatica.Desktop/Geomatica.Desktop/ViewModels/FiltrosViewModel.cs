using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FiltrosViewModel : INotifyPropertyChanged
    {
        private string? _areaWkt;
        private DateTime? _desde;
        private DateTime? _hasta;
        private string? _palabraClave;

        public string? AreaWkt
        {
            get => _areaWkt;
            set { _areaWkt = value; OnPropertyChanged(); }
        }

        public DateTime? Desde
        {
            get => _desde;
            set { _desde = value; OnPropertyChanged(); }
        }

        public DateTime? Hasta
        {
            get => _hasta;
            set { _hasta = value; OnPropertyChanged(); }
        }

        public string? PalabraClave
        {
            get => _palabraClave;
            set { _palabraClave = value; OnPropertyChanged(); }
        }

        public event EventHandler? BuscarSolicitado;
        public void DispararBuscar() => BuscarSolicitado?.Invoke(this, EventArgs.Empty);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
