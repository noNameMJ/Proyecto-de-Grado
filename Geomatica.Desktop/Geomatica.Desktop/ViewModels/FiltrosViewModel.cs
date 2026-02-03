using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using System.Collections.ObjectModel;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FiltrosViewModel : ObservableObject
    {
        private readonly IMunicipioRepository? _municipioRepository;

        [ObservableProperty] private string? palabraClave;
        [ObservableProperty] private DateTime? desde;
        [ObservableProperty] private DateTime? hasta;
        [ObservableProperty] private object? areaInteres;
        [ObservableProperty] private ProyectoItem? selectedProyecto;

        [ObservableProperty] private DepartamentoItem? selectedDepartamento;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<object> Areas { get; } = new();

        public ObservableCollection<object> ResultadosResumen { get; } = new();
        public ObservableCollection<object> ResultadosLista { get; } = new();

        public IRelayCommand BuscarCommand { get; }
        public IRelayCommand DescargarCommand { get; }

        public event EventHandler? BuscarSolicitado;

        public FiltrosViewModel(IMunicipioRepository? municipioRepository)
        {
            _municipioRepository = municipioRepository;
            BuscarCommand = new RelayCommand(() => BuscarSolicitado?.Invoke(this, EventArgs.Empty));
            DescargarCommand = new RelayCommand(() => { /* placeholder */ });
            
            if (_municipioRepository != null)
                _ = CargarDepartamentosAsync();
        }

        private async Task CargarDepartamentosAsync()
        {
            if (_municipioRepository == null) return;
            try 
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                foreach(var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading departments: {ex}");
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            Areas.Clear();
            AreaInteres = null;
            
            // Trigger search whenever department changes
            BuscarSolicitado?.Invoke(this, EventArgs.Empty);

            if (value == null || _municipioRepository == null) return;

            try
            {
                var munis = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                foreach (var m in munis)
                {
                    Areas.Add(new MunicipioItem(m.Codigo, m.Nombre));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading municipios: {ex}");
            }
        }

        partial void OnPalabraClaveChanged(string? value) => BuscarSolicitado?.Invoke(this, EventArgs.Empty);
        partial void OnDesdeChanged(DateTime? value) => BuscarSolicitado?.Invoke(this, EventArgs.Empty);
        partial void OnHastaChanged(DateTime? value) => BuscarSolicitado?.Invoke(this, EventArgs.Empty);
        partial void OnAreaInteresChanged(object? value) => BuscarSolicitado?.Invoke(this, EventArgs.Empty);

        public record DepartamentoItem(string Codigo, string Nombre)
        {
            public override string ToString() => Nombre;
        }

        public record MunicipioItem(string Codigo, string Nombre)
        {
            public override string ToString() => Nombre;
        }

        public record ProyectoItem(int Id, string Titulo, double Lon, double Lat, string? Ruta)
        {
            public override string ToString() => Titulo;
        }
    }
}
