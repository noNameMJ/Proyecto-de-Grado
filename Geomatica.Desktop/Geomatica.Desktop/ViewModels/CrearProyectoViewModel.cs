using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public partial class CrearProyectoViewModel : ObservableObject
    {
        private readonly IProyectoRepository _proyectoRepository;
        private readonly IMunicipioRepository _municipioRepository;

        // Navigation back
        private readonly Action _navigateBack;
        private readonly Action? _onProyectoCreado;

        [ObservableProperty] private string titulo = string.Empty;
        [ObservableProperty] private string? descripcion;
        [ObservableProperty] private DateTime fechaInicio = DateTime.Today;
        [ObservableProperty] private string? palabraClave;
        [ObservableProperty] private string? ruta;
        
        [ObservableProperty] private string? latStr;
        [ObservableProperty] private string? lonStr;

        [ObservableProperty] private DepartamentoItem? selectedDepartamento;
        [ObservableProperty] private MunicipioItem? selectedMunicipio;

        public event Action<string?>? MunicipioGeoJsonChanged;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();

        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }

        public CrearProyectoViewModel(IProyectoRepository proyectoRepository, IMunicipioRepository municipioRepository, Action navigateBack, Action? onProyectoCreado = null)
        {
            _proyectoRepository = proyectoRepository;
            _municipioRepository = municipioRepository;
            _navigateBack = navigateBack;
            _onProyectoCreado = onProyectoCreado;

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);

            _ = CargarDepartamentosAsync();
        }

        private async Task CargarDepartamentosAsync()
        {
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                foreach (var d in deps)
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando departamentos: {ex.Message}");
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            Municipios.Clear();
            SelectedMunicipio = null;
            if (value == null) return;

            try
            {
                var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                foreach (var m in muns)
                    Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando municipios: {ex.Message}");
            }
        }

        async partial void OnSelectedMunicipioChanged(MunicipioItem? value)
        {
            if (value == null) { MunicipioGeoJsonChanged?.Invoke(null); return; }
            try
            {
                var results = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { value.Codigo });
                MunicipioGeoJsonChanged?.Invoke(results.FirstOrDefault()?.GeoJson);
            }
            catch
            {
                MunicipioGeoJsonChanged?.Invoke(null);
            }
        }

        private async Task GuardarAsync()
        {
            if (string.IsNullOrWhiteSpace(Titulo))
            {
                MessageBox.Show("El título es obligatorio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SelectedMunicipio == null)
            {
                MessageBox.Show("Debe seleccionar un municipio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double? lat = null;
            double? lon = null;

            var latNorm = LatStr?.Replace(',', '.');
            var lonNorm = LonStr?.Replace(',', '.');

            if (!string.IsNullOrWhiteSpace(latNorm) && double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)) lat = l;
            if (!string.IsNullOrWhiteSpace(lonNorm) && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var o)) lon = o;

            string? geom = null;
            if (lon.HasValue && lat.HasValue)
            {
                geom = string.Format(CultureInfo.InvariantCulture, "POINT({0} {1})", lon.Value, lat.Value);
            }

            try
            {
                await _proyectoRepository.InsertarAsync(
                    Titulo, 
                    Descripcion, 
                    FechaInicio, 
                    PalabraClave, 
                    Ruta, 
                    geom, 
                    SelectedMunicipio.Codigo
                );

                MessageBox.Show("Proyecto creado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                _onProyectoCreado?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error guardando proyecto: {ex.Message}\n\nVerifique que la columna 'descripcion' exista en la DB si falla.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Establece las coordenadas desde un tap en el mapa.
        /// </summary>
        public void SetCoordenadas(double lat, double lon)
        {
            LatStr = lat.ToString("F6", CultureInfo.InvariantCulture);
            LonStr = lon.ToString("F6", CultureInfo.InvariantCulture);
        }

        public record DepartamentoItem(string Codigo, string Nombre)
        {
            public override string ToString() => Nombre;
        }

        public record MunicipioItem(string Codigo, string Nombre)
        {
            public override string ToString() => Nombre;
        }
    }
}
