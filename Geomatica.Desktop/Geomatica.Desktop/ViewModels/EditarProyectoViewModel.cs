using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Data.Repositories;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Desktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Geomatica.Desktop.ViewModels
{
    public partial class EditarProyectoViewModel : ObservableObject
    {
        private readonly IProyectoRepository _proyectoRepository;
        private readonly IMunicipioRepository _municipioRepository;
        private readonly INotificationService? _notifications;
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

        [ObservableProperty] private bool tituloInvalido;

        [ObservableProperty] private DepartamentoItem? selectedDepartamento;
        [ObservableProperty] private MunicipioItem? selectedMunicipio;

        partial void OnTituloChanged(string value)
        {
            if (TituloInvalido && !string.IsNullOrWhiteSpace(value))
                TituloInvalido = false;
        }

        public event Action<string?>? MunicipioGeoJsonChanged;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();

        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }
        public IRelayCommand SeleccionarCarpetaCommand { get; }

        public EditarProyectoViewModel(
            IProyectoRepository proyectoRepository,
            IMunicipioRepository municipioRepository,
            ProyectoDetalleDto proyecto,
            Action navigateBack,
            Action? onProyectoEditado = null,
            INotificationService? notifications = null)
        {
            _proyectoRepository = proyectoRepository;
            _municipioRepository = municipioRepository;
            _notifications = notifications;
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
                LatStr = proyecto.Lat.ToString("F6", CultureInfo.InvariantCulture);
                LonStr = proyecto.Lon.ToString("F6", CultureInfo.InvariantCulture);
            }

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);
            SeleccionarCarpetaCommand = new RelayCommand(SeleccionarCarpeta);

            _ = CargarDatosInicialesAsync(proyecto.MunicipioCodigo);
        }

        private async Task CargarDatosInicialesAsync(string? municipioCodigo)
        {
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }

                if (!string.IsNullOrWhiteSpace(municipioCodigo))
                {
                    var dptoCodigo = municipioCodigo.Length >= 2 ? municipioCodigo.Substring(0, 2) : "";
                    var dep = Departamentos.FirstOrDefault(d => d.Codigo == dptoCodigo);
                    if (dep != null)
                    {
                        SelectedDepartamento = dep;
                        var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(dep.Codigo);
                        Municipios.Clear();
                        foreach (var m in muns)
                        {
                            var item = new MunicipioItem(m.Codigo, m.Nombre);
                            Municipios.Add(item);
                            if (m.Codigo == municipioCodigo)
                            {
                                SelectedMunicipio = item;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error cargando departamentos: {ex.Message}", "Departamentos");
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            if (value == null || string.IsNullOrEmpty(value.Codigo)) return;

            try
            {
                var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
                Municipios.Clear();
                foreach (var m in muns)
                {
                    Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error cargando municipios: {ex.Message}", "Municipios");
            }
        }

        async partial void OnSelectedMunicipioChanged(MunicipioItem? value)
        {
            if (value == null || string.IsNullOrEmpty(value.Codigo))
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                return;
            }

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
            TituloInvalido = string.IsNullOrWhiteSpace(Titulo);
            if (TituloInvalido)
            {
                _notifications?.ShowWarning("El título es obligatorio.", "Validación");
                return;
            }
            if (SelectedMunicipio == null)
            {
                _notifications?.ShowWarning("Debe seleccionar un municipio.", "Validación");
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

                _notifications?.ShowSuccess("Proyecto actualizado exitosamente.", "Proyecto Guardado");
                _onProyectoEditado?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error actualizando proyecto: {ex.Message}", "Error al Actualizar");
            }
        }

        /// <summary>
        /// Abre el diálogo para seleccionar una carpeta física del sistema.
        /// </summary>
        private void SeleccionarCarpeta()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para el proyecto",
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(Ruta) && Directory.Exists(Ruta))
                {
                    dialog.InitialDirectory = Ruta;
                }

                if (dialog.ShowDialog() == true)
                {
                    Ruta = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al abrir el selector de carpetas: {ex.Message}", "Selector de Carpetas");
            }
        }

        /// <summary>
        /// Establece las coordenadas seleccionadas en el mapa interactivo.
        /// </summary>
        public void SetCoordenadas(double lat, double lon)
        {
            LatStr = lat.ToString("F6", CultureInfo.InvariantCulture);
            LonStr = lon.ToString("F6", CultureInfo.InvariantCulture);
        }

        public record DepartamentoItem(string Codigo, string Nombre);
        public record MunicipioItem(string Codigo, string Nombre);
    }
}
