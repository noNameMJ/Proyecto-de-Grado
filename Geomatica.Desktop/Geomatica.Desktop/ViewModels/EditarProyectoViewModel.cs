using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using System.Collections.ObjectModel;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public partial class EditarProyectoViewModel : ObservableObject
    {
        private readonly IProyectoRepository _proyectoRepository;
        private readonly IMunicipioRepository _municipioRepository;
        private readonly Action _navigateBack;
        private readonly Action? _onProyectoEditado;

        public int IdProyecto { get; }

        [ObservableProperty] private string titulo = string.Empty;
        [ObservableProperty] private string? descripcion;
        [ObservableProperty] private DateTime fechaInicio = DateTime.Today;
        [ObservableProperty] private string? palabraClave;
        [ObservableProperty] private string? ruta;
        [ObservableProperty] private string? latStr;
        [ObservableProperty] private string? lonStr;

        [ObservableProperty] private DepartamentoItem? selectedDepartamento;
        [ObservableProperty] private MunicipioItem? selectedMunicipio;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();

        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }

        public EditarProyectoViewModel(
            IProyectoRepository proyectoRepository,
            IMunicipioRepository municipioRepository,
            ProyectoDetalleDto proyecto,
            Action navigateBack,
            Action? onProyectoEditado = null)
        {
            _proyectoRepository = proyectoRepository;
            _municipioRepository = municipioRepository;
            _navigateBack = navigateBack;
            _onProyectoEditado = onProyectoEditado;

            IdProyecto = proyecto.Id;
            Titulo = proyecto.Titulo;
            Descripcion = proyecto.Descripcion;
            FechaInicio = proyecto.Fecha ?? DateTime.Today;
            PalabraClave = proyecto.PalabraClave;
            Ruta = proyecto.RutaArchivos;
            if (proyecto.Lat != 0 || proyecto.Lon != 0)
            {
                LatStr = proyecto.Lat.ToString();
                LonStr = proyecto.Lon.ToString();
            }

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);

            _ = CargarDepartamentosYSeleccionarAsync(proyecto.MunicipioCodigo);
        }

        private async Task CargarDepartamentosYSeleccionarAsync(string? municipioCodigo)
        {
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                foreach (var d in deps)
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));

                if (!string.IsNullOrEmpty(municipioCodigo) && municipioCodigo.Length >= 2)
                {
                    var dptoCodigo = municipioCodigo.Substring(0, 2);
                    var dpto = Departamentos.FirstOrDefault(d => d.Codigo == dptoCodigo);
                    if (dpto != null)
                    {
                        SelectedDepartamento = dpto;
                        // Wait for municipalities to load from OnSelectedDepartamentoChanged
                        await Task.Delay(500);
                        // Try to select on UI thread
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var muni = Municipios.FirstOrDefault(m => m.Codigo == municipioCodigo);
                            if (muni != null)
                                SelectedMunicipio = muni;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error cargando departamentos: {ex.Message}");
            }
        }

        partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            Municipios.Clear();
            SelectedMunicipio = null;
            if (value == null) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var m in muns)
                            Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error cargando municipios: {ex.Message}"));
                }
            });
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

            if (!string.IsNullOrWhiteSpace(LatStr) && double.TryParse(LatStr, out var l)) lat = l;
            if (!string.IsNullOrWhiteSpace(LonStr) && double.TryParse(LonStr, out var o)) lon = o;

            string? geom = null;
            if (lon.HasValue && lat.HasValue)
            {
                geom = $"POINT({lon.Value} {lat.Value})";
            }

            try
            {
                await _proyectoRepository.ActualizarAsync(
                    IdProyecto,
                    Titulo,
                    Descripcion,
                    FechaInicio,
                    PalabraClave,
                    Ruta,
                    geom,
                    SelectedMunicipio.Codigo
                );

                MessageBox.Show("Proyecto actualizado exitosamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
                _onProyectoEditado?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error actualizando proyecto: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
