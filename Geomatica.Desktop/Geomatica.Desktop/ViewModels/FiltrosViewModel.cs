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
        [ObservableProperty] private bool isBuscando;

        // Manejo Resiliente de Desconexión / Reconexión a PostgreSQL
        [ObservableProperty] private bool isErrorConexionDb;
        [ObservableProperty] private string mensajeErrorConexion = "Sin conexión a la base de datos PostgreSQL. Verifique su red/VPN.";
        [ObservableProperty] private bool isReintentandoConexion;

        public bool NoHayResultados => !IsBuscando && !IsErrorConexionDb && ResultadosLista.Count == 0;
        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<object> Areas { get; } = new();

        public ObservableCollection<object> ResultadosResumen { get; } = new();
        public ObservableCollection<object> ResultadosLista { get; } = new();

        public IRelayCommand BuscarCommand { get; }
        public IRelayCommand DescargarCommand { get; }
        public IRelayCommand LimpiarFiltrosCommand { get; }
        public IRelayCommand LimpiarDesdeCommand { get; }
        public IRelayCommand LimpiarHastaCommand { get; }
        public IAsyncRelayCommand ReintentarConexionCommand { get; }

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
            ReintentarConexionCommand = new AsyncRelayCommand(ReintentarConexionAsync);

            if (_municipioRepository != null)
                _ = CargarDepartamentosAsync();
        }

        private void LimpiarFiltros()
        {
            PalabraClave = null;
            Desde = null;
            Hasta = null;
            // No deseleccionamos el proyecto actual para no cerrar la ficha de detalle
            // SelectedProyecto = null;

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

            IsBuscando = true;
            OnPropertyChanged(nameof(NoHayResultados));

            _ = Task.Delay(DebounceMs).ContinueWith(t =>
            {
                if (!token.IsCancellationRequested)
                    BuscarSolicitado?.Invoke(this, EventArgs.Empty);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void NotificarResultadosCargados()
        {
            IsBuscando = false;
            OnPropertyChanged(nameof(NoHayResultados));
        }

        public void NotificarErrorConexion(string mensaje)
        {
            IsBuscando = false;
            IsErrorConexionDb = true;
            MensajeErrorConexion = mensaje;
            OnPropertyChanged(nameof(NoHayResultados));
        }

        public async Task ReintentarConexionAsync()
        {
            if (_municipioRepository == null) return;

            IsReintentandoConexion = true;
            try
            {
                // Intentar probar conectividad cargando departamentos
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                
                // Conexión exitosa
                IsErrorConexionDb = false;
                Departamentos.Clear();
                Departamentos.Add(DepartamentoItem.Todos);
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }
                SelectedDepartamento = DepartamentoItem.Todos;

                // Re-disparar búsqueda para repoblar proyectos
                BuscarSolicitado?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                IsErrorConexionDb = true;
                MensajeErrorConexion = $"Fallo al reconectar con PostgreSQL: {ex.Message}";
            }
            finally
            {
                IsReintentandoConexion = false;
                OnPropertyChanged(nameof(NoHayResultados));
            }
        }

        private async Task CargarDepartamentosAsync()
        {
            if (_municipioRepository == null) return;
            try 
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                IsErrorConexionDb = false;
                Departamentos.Clear();
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
                IsErrorConexionDb = true;
                MensajeErrorConexion = $"No se pudo conectar a PostgreSQL: {ex.Message}";
                OnPropertyChanged(nameof(NoHayResultados));
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
                IsErrorConexionDb = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading municipios: {ex}");
                AreaInteres = null;
                IsErrorConexionDb = true;
                MensajeErrorConexion = $"Error de base de datos al listar municipios: {ex.Message}";
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
