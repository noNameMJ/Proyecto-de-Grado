using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using Geomatica.Domain.Entities;
using Geomatica.Infrastructure.Gis.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Geomatica.Desktop.ViewModels
{
    public partial class FiltrosViewModel : ObservableObject
    {
        private readonly IMunicipioRepository? _municipioRepository;
        private CancellationTokenSource? _debounceCts;
        private const int DebounceMs = 300;
        private bool _estaInicializando = false;

        public bool EstaInicializando
        {
            get => _estaInicializando;
            set => _estaInicializando = value;
        }

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
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReintentarConexion))]
        private bool isReintentandoConexion;

        public bool CanReintentarConexion => !IsReintentandoConexion;
        public bool HasResultados => ResultadosLista.Count > 0;
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
        public IRelayCommand<SubproyectoDto> UbicarSubproyectoCommand { get; }
        public IRelayCommand<SubproyectoDto> AbrirArchivosSubproyectoCommand { get; }

        public event EventHandler? BuscarSolicitado;
        public event EventHandler<ProyectoItem>? ProyectoSeleccionadoEnMapa;
        public event EventHandler<SubproyectoDto>? SubproyectoSeleccionadoEnMapa;
        public event EventHandler<SubproyectoDto>? NavegacionSubcarpetaSolicitada;

        public FiltrosViewModel(IMunicipioRepository? municipioRepository)
        {
            _municipioRepository = municipioRepository;
            BuscarCommand = new RelayCommand(() => BuscarSolicitado?.Invoke(this, EventArgs.Empty));
            DescargarCommand = new RelayCommand(() => { /* placeholder */ });
            LimpiarFiltrosCommand = new RelayCommand(LimpiarFiltros);
            LimpiarDesdeCommand = new RelayCommand(() => Desde = null);
            LimpiarHastaCommand = new RelayCommand(() => Hasta = null);
            ReintentarConexionCommand = new AsyncRelayCommand(ReintentarConexionAsync);
            UbicarSubproyectoCommand = new RelayCommand<SubproyectoDto>(sub =>
            {
                if (sub != null && (sub.Lon != 0 || sub.Lat != 0))
                    SubproyectoSeleccionadoEnMapa?.Invoke(this, sub);
            });
            AbrirArchivosSubproyectoCommand = new RelayCommand<SubproyectoDto>(sub =>
            {
                if (sub != null)
                    NavegacionSubcarpetaSolicitada?.Invoke(this, sub);
            });

            if (_municipioRepository != null)
                _ = InicializarFiltrosAsync();
        }

        private void LimpiarFiltros()
        {
            PalabraClave = null;
            Desde = null;
            Hasta = null;

            if (SelectedDepartamento == DepartamentoItem.Todos)
            {
                AreaInteres = MunicipioItem.Todos;
                DebounceBuscar();
            }
            else
            {
                SelectedDepartamento = DepartamentoItem.Todos;
            }
        }

        private void DebounceBuscar()
        {
            if (_estaInicializando) return;
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
            OnPropertyChanged(nameof(HasResultados));
            OnPropertyChanged(nameof(NoHayResultados));
        }

        public void NotificarErrorConexion(string mensaje)
        {
            IsBuscando = false;
            IsErrorConexionDb = true;
            MensajeErrorConexion = mensaje;
            OnPropertyChanged(nameof(HasResultados));
            OnPropertyChanged(nameof(NoHayResultados));
        }

        public async Task ReintentarConexionAsync()
        {
            if (_municipioRepository == null) return;
            if (IsReintentandoConexion) return;

            IsReintentandoConexion = true;
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();

                IsErrorConexionDb = false;
                Departamentos.Clear();
                Departamentos.Add(DepartamentoItem.Todos);
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }
                SelectedDepartamento = DepartamentoItem.Todos;
                MensajeErrorConexion = string.Empty;
                BuscarCommand.Execute(null);
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

        public async Task InicializarFiltrosAsync()
        {
            _estaInicializando = true;
            try
            {
                await CargarDepartamentosAsync();
                if (_municipioRepository != null)
                {
                    var munis = await _municipioRepository.ListarTodosMunicipiosAsync();
                    Areas.Clear();
                    Areas.Add(MunicipioItem.Todos);
                    foreach (var m in munis)
                    {
                        Areas.Add(new MunicipioItem(m.Codigo, m.Nombre));
                    }
                    AreaInteres = MunicipioItem.Todos;
                }
            }
            catch (Exception ex)
            {
                NotificarErrorConexion($"Fallo al inicializar filtros territoriales: {ex.Message}");
            }
            finally
            {
                _estaInicializando = false;
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

        public class ProyectoItem : ObservableObject
        {
            public int Id { get; }
            public string Titulo { get; }
            public double Lon { get; }
            public double Lat { get; }
            public string? Ruta { get; }
            public DateTime? Fecha { get; }
            public string? PalabrasClave { get; }
            public string? Responsable { get; }
            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }
            public string? TipoRecurso { get; }

            public ObservableCollection<SubproyectoDto> Subproyectos { get; } = new();

            private bool _estaExpandido;
            public bool EstaExpandido
            {
                get => _estaExpandido;
                set => SetProperty(ref _estaExpandido, value);
            }

            public bool HasSubproyectos => Subproyectos.Count > 0;
            public int TotalSubproyectos => Subproyectos.Count;

            public string SubproyectosBadgeTexto => TotalSubproyectos == 1
                ? "1 subproyecto / frente"
                : $"{TotalSubproyectos} subproyectos / frentes";

            public string CodigoId => $"#PROY-{Id:D4}";

            public string FechaTexto => Fecha.HasValue && Fecha.Value != DateTime.MinValue
                ? Fecha.Value.ToString("dd/MM/yyyy")
                : "Sin fecha";

            public string CoordenadasTexto => (Lon != 0 || Lat != 0)
                ? $"{Lat.ToString("F4", CultureInfo.InvariantCulture)}°N, {Lon.ToString("F4", CultureInfo.InvariantCulture)}°W"
                : "Sin coordenadas";

            public string PalabrasClaveTexto => !string.IsNullOrWhiteSpace(PalabrasClave)
                ? PalabrasClave.Trim()
                : "";

            public bool HasPalabrasClave => !string.IsNullOrWhiteSpace(PalabrasClave);

            public bool TieneExtentValido => (MinX != 0 || MaxX != 0 || MinY != 0 || MaxY != 0) &&
                                             !(Math.Abs(MinX - MaxX) < 1e-7 && Math.Abs(MinY - MaxY) < 1e-7);

            public ProyectoItem(
                int id,
                string titulo,
                double lon,
                double lat,
                string? ruta,
                DateTime? fecha = null,
                string? palabrasClave = null,
                string? responsable = null,
                double minX = 0,
                double minY = 0,
                double maxX = 0,
                double maxY = 0,
                string? tipoRecurso = null,
                IEnumerable<SubproyectoDto>? subproyectos = null)
            {
                Id = id;
                Titulo = titulo;
                Lon = lon;
                Lat = lat;
                Ruta = ruta;
                Fecha = fecha;
                PalabrasClave = palabrasClave;
                Responsable = responsable;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
                TipoRecurso = tipoRecurso;

                if (subproyectos != null)
                {
                    foreach (var s in subproyectos)
                        Subproyectos.Add(s);
                }
            }

            public void ActualizarSubproyectos(IEnumerable<SubproyectoDto>? subproyectos)
            {
                Subproyectos.Clear();
                if (subproyectos != null)
                {
                    foreach (var s in subproyectos)
                        Subproyectos.Add(s);
                }
                OnPropertyChanged(nameof(HasSubproyectos));
                OnPropertyChanged(nameof(TotalSubproyectos));
                OnPropertyChanged(nameof(SubproyectosBadgeTexto));
            }

            public override string ToString() => Titulo;
        }
    }
}
