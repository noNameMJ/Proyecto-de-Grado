using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using System.Collections.ObjectModel;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FiltrosViewModel : ObservableObject
    {
        private readonly IMunicipioRepository? _municipioRepository;
        private CancellationTokenSource? _debounceCts;
        private const int DebounceMs = 300;

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
        public IRelayCommand LimpiarFiltrosCommand { get; }
        public IRelayCommand LimpiarDesdeCommand { get; }
        public IRelayCommand LimpiarHastaCommand { get; }

        public event EventHandler? BuscarSolicitado;
        public event EventHandler<ProyectoItem>? ProyectoSeleccionadoEnMapa;

        public FiltrosViewModel(IMunicipioRepository? municipioRepository)
        {
            _municipioRepository = municipioRepository;
            BuscarCommand = new RelayCommand(() => BuscarSolicitado?.Invoke(this, EventArgs.Empty));
            DescargarCommand = new RelayCommand(() => { /* placeholder */ });
            LimpiarFiltrosCommand = new RelayCommand(LimpiarFiltros);
            LimpiarDesdeCommand = new RelayCommand(() => Desde = null);
            LimpiarHastaCommand = new RelayCommand(() => Hasta = null);

            if (_municipioRepository != null)
                _ = CargarDepartamentosAsync();
        }

        private void LimpiarFiltros()
        {
            PalabraClave = null;
            Desde = null;
            Hasta = null;
            SelectedProyecto = null;

            if (SelectedDepartamento == DepartamentoItem.Todos)
            {
                // Ya es Todos → OnSelectedDepartamentoChanged no se dispara.
                // Forzar que municipio vuelva a "— Todos —".
                AreaInteres = MunicipioItem.Todos;
                DebounceBuscar();
            }
            else
            {
                // Cambio real → dispara OnSelectedDepartamentoChanged que recarga municipios
                SelectedDepartamento = DepartamentoItem.Todos;
            }
        }

        private void DebounceBuscar()
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Delay(DebounceMs).ContinueWith(t =>
            {
                if (!token.IsCancellationRequested)
                    BuscarSolicitado?.Invoke(this, EventArgs.Empty);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async Task CargarDepartamentosAsync()
        {
            if (_municipioRepository == null) return;
            try 
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Add(DepartamentoItem.Todos);
                foreach(var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }
                // Seleccionar "— Todos —" por defecto
                SelectedDepartamento = DepartamentoItem.Todos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading departments: {ex}");
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            Areas.Clear();

            if (_municipioRepository == null)
            {
                AreaInteres = null;
                DebounceBuscar();
                return;
            }

            try
            {
                IReadOnlyList<MunicipioDto> munis;

                if (value == null || string.IsNullOrEmpty(value.Codigo))
                {
                    munis = await _municipioRepository.ListarTodosMunicipiosAsync();
                }
                else
                {
                    munis = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                }

                Areas.Add(MunicipioItem.Todos);
                foreach (var m in munis)
                {
                    Areas.Add(new MunicipioItem(m.Codigo, m.Nombre));
                }

                // Seleccionar "— Todos —" por defecto en municipios
                AreaInteres = MunicipioItem.Todos;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading municipios: {ex}");
                AreaInteres = null;
            }

            DebounceBuscar();
        }

        partial void OnPalabraClaveChanged(string? value) => DebounceBuscar();
        partial void OnDesdeChanged(DateTime? value) => DebounceBuscar();
        partial void OnHastaChanged(DateTime? value) => DebounceBuscar();
        partial void OnAreaInteresChanged(object? value) => DebounceBuscar();

        partial void OnSelectedProyectoChanged(ProyectoItem? value)
        {
            if (value != null && !(value.Lon == 0 && value.Lat == 0))
                ProyectoSeleccionadoEnMapa?.Invoke(this, value);
        }

        public record DepartamentoItem(string Codigo, string Nombre)
        {
            public static readonly DepartamentoItem Todos = new("", "— Todos —");
            public override string ToString() => Nombre;
        }

        public record MunicipioItem(string Codigo, string Nombre)
        {
            public static readonly MunicipioItem Todos = new("", "— Todos —");
            public override string ToString() => Nombre;
        }

        public record ProyectoItem(int Id, string Titulo, double Lon, double Lat, string? Ruta)
        {
            public override string ToString() => Titulo;
        }
    }
}
