using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Geomatica.Data.Repositories;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Desktop.Services;

namespace Geomatica.Desktop.ViewModels
{
    public partial class CrearProyectoViewModel : ObservableObject
    {
        private readonly IProyectoRepository _proyectoRepository;
        private readonly IMunicipioRepository _municipioRepository;
        private readonly ProyectoArchivosService _proyectoArchivosService;
        private readonly INotificationService? _notifications;
        private readonly Action _navigateBack;
        private readonly Action? _onProyectoCreado;
        private bool _isUpdatingProgrammatically;

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
        public event Action<double?, double?>? CoordenadasPinChanged;

        public ObservableCollection<DepartamentoItem> Departamentos { get; } = new();
        public ObservableCollection<MunicipioItem> Municipios { get; } = new();

        public IAsyncRelayCommand GuardarCommand { get; }
        public IRelayCommand CancelarCommand { get; }
        public IRelayCommand SeleccionarCarpetaCommand { get; }

        public CrearProyectoViewModel(
            IProyectoRepository proyectoRepository, 
            IMunicipioRepository municipioRepository, 
            ProyectoArchivosService proyectoArchivosService, 
            Action navigateBack, 
            Action? onProyectoCreado = null,
            INotificationService? notifications = null)
        {
            _proyectoRepository = proyectoRepository;
            _municipioRepository = municipioRepository;
            _proyectoArchivosService = proyectoArchivosService;
            _navigateBack = navigateBack;
            _onProyectoCreado = onProyectoCreado;
            _notifications = notifications;

            GuardarCommand = new AsyncRelayCommand(GuardarAsync);
            CancelarCommand = new RelayCommand(_navigateBack);
            SeleccionarCarpetaCommand = new RelayCommand(SeleccionarCarpeta);

            _ = CargarDepartamentosAsync();
        }

        private async Task CargarDepartamentosAsync()
        {
            try
            {
                var deps = await _municipioRepository.ListarDepartamentosAsync();
                Departamentos.Clear();
                foreach (var d in deps)
                {
                    Departamentos.Add(new DepartamentoItem(d.Codigo, d.Nombre));
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error cargando departamentos: {ex.Message}", "Departamentos");
            }
        }

        async partial void OnSelectedDepartamentoChanged(DepartamentoItem? value)
        {
            if (_isUpdatingProgrammatically) return;

            Municipios.Clear();
            SelectedMunicipio = null;
            MunicipioGeoJsonChanged?.Invoke(null);
            LatStr = null;
            LonStr = null;
            CoordenadasPinChanged?.Invoke(null, null);

            if (value == null || string.IsNullOrEmpty(value.Codigo)) return;

            try
            {
                var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(value.Codigo);
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
            if (_isUpdatingProgrammatically) return;

            if (value == null || string.IsNullOrEmpty(value.Codigo))
            {
                MunicipioGeoJsonChanged?.Invoke(null);
                return;
            }

            try
            {
                var geoDtos = await _municipioRepository.PorCodigosGeoJsonAsync(new[] { value.Codigo });
                var geo = geoDtos.Count > 0 ? geoDtos[0].GeoJson : null;
                MunicipioGeoJsonChanged?.Invoke(geo);

                // Si hay coordenadas actuales, verificar si están dentro del nuevo municipio
                if (ObtenerCoordenadasActuales(out var lat, out var lon))
                {
                    var adentro = await _municipioRepository.PuntoEstaEnMunicipioAsync(value.Codigo, lon, lat);
                    if (!adentro)
                    {
                        LatStr = null;
                        LonStr = null;
                        CoordenadasPinChanged?.Invoke(null, null);
                        _notifications?.ShowWarning($"El punto marcado previamente no pertenece a {value.Nombre} y ha sido removido.", "Ubicación");
                    }
                }
            }
            catch
            {
                MunicipioGeoJsonChanged?.Invoke(null);
            }
        }

        public bool ObtenerCoordenadasActuales(out double lat, out double lon)
        {
            lat = 0;
            lon = 0;
            var latNorm = LatStr?.Replace(',', '.');
            var lonNorm = LonStr?.Replace(',', '.');

            if (!string.IsNullOrWhiteSpace(latNorm) && !string.IsNullOrWhiteSpace(lonNorm)
                && double.TryParse(latNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                && double.TryParse(lonNorm, NumberStyles.Float, CultureInfo.InvariantCulture, out var o))
            {
                lat = l;
                lon = o;
                return true;
            }
            return false;
        }

        public async Task<bool> ProcesarClickMapaAsync(double lat, double lon)
        {
            try
            {
                var ubicacion = await _municipioRepository.ObtenerPorPuntoAsync(lon, lat);
                if (ubicacion == null)
                {
                    _notifications?.ShowWarning("El punto seleccionado no se encuentra dentro del territorio de ningún municipio registrado.", "Ubicación Inválida");
                    return false;
                }

                if (SelectedMunicipio == null)
                {
                    await AsignarUbicacionDetectadaAsync(ubicacion);
                    SetCoordenadas(lat, lon);
                    _notifications?.ShowInfo($"Municipio detectado: {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}).", "Ubicación Asignada");
                    return true;
                }

                if (SelectedMunicipio.Codigo == ubicacion.MunicipioCodigo)
                {
                    SetCoordenadas(lat, lon);
                    return true;
                }

                var msg = $"El punto seleccionado pertenece a {ubicacion.MunicipioNombre} ({ubicacion.DepartamentoNombre}), pero actualmente tiene seleccionado {SelectedMunicipio.Nombre}.\n\n¿Desea cambiar el municipio del proyecto a {ubicacion.MunicipioNombre}?";
                var res = MessageBox.Show(msg, "Punto fuera del municipio", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    await AsignarUbicacionDetectadaAsync(ubicacion);
                    SetCoordenadas(lat, lon);
                    return true;
                }
                else
                {
                    _notifications?.ShowWarning($"Se mantuvo el municipio actual ({SelectedMunicipio.Nombre}). El punto fuera del límite no fue asignado.", "Punto Rechazado");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al verificar la ubicación geográfica: {ex.Message}", "Error de Ubicación");
                return false;
            }
        }

        private async Task AsignarUbicacionDetectadaAsync(MunicipioUbicacionDto ubicacion)
        {
            _isUpdatingProgrammatically = true;
            try
            {
                if (SelectedDepartamento?.Codigo != ubicacion.DepartamentoCodigo)
                {
                    var dep = Departamentos.FirstOrDefault(d => d.Codigo == ubicacion.DepartamentoCodigo);
                    if (dep == null && Departamentos.Count == 0)
                    {
                        await CargarDepartamentosAsync();
                        dep = Departamentos.FirstOrDefault(d => d.Codigo == ubicacion.DepartamentoCodigo);
                    }

                    if (dep != null)
                    {
                        SelectedDepartamento = dep;
                        var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(dep.Codigo);
                        Municipios.Clear();
                        foreach (var m in muns)
                        {
                            Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                        }
                    }
                }
                else if (Municipios.Count == 0)
                {
                    var muns = await _municipioRepository.ListarMunicipiosPorDepartamentoAsync(ubicacion.DepartamentoCodigo);
                    Municipios.Clear();
                    foreach (var m in muns)
                    {
                        Municipios.Add(new MunicipioItem(m.Codigo, m.Nombre));
                    }
                }

                var mun = Municipios.FirstOrDefault(m => m.Codigo == ubicacion.MunicipioCodigo);
                if (mun == null)
                {
                    mun = new MunicipioItem(ubicacion.MunicipioCodigo, ubicacion.MunicipioNombre);
                    Municipios.Add(mun);
                }
                SelectedMunicipio = mun;

                MunicipioGeoJsonChanged?.Invoke(ubicacion.GeoJson);
            }
            finally
            {
                _isUpdatingProgrammatically = false;
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

            if ((lat.HasValue && !lon.HasValue) || (!lat.HasValue && lon.HasValue))
            {
                _notifications?.ShowWarning("Debe especificar tanto latitud como longitud, o dejar ambas vacías.", "Validación");
                return;
            }

            string? geom = null;
            if (lon.HasValue && lat.HasValue)
            {
                var estaEnMunicipio = await _municipioRepository.PuntoEstaEnMunicipioAsync(SelectedMunicipio.Codigo, lon.Value, lat.Value);
                if (!estaEnMunicipio)
                {
                    _notifications?.ShowError($"Las coordenadas indicadas ({lat.Value:F6}, {lon.Value:F6}) no pertenecen al municipio seleccionado ({SelectedMunicipio.Nombre}). Corrija la ubicación antes de guardar.", "Error de Validación Espacial");
                    return;
                }

                geom = string.Format(CultureInfo.InvariantCulture, "POINT({0} {1})", lon.Value, lat.Value);
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(Ruta))
                {
                    _proyectoArchivosService.CrearEstructuraProyecto(Ruta);
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError(ex.Message, "Error creando carpetas");
                return;
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

                _notifications?.ShowSuccess("Proyecto creado exitosamente.", "Proyecto Creado");
                _onProyectoCreado?.Invoke();
                _navigateBack();
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error guardando proyecto: {ex.Message}", "Error al Guardar");
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
            CoordenadasPinChanged?.Invoke(lat, lon);
        }

        public record DepartamentoItem(string Codigo, string Nombre);
        public record MunicipioItem(string Codigo, string Nombre);
    }
}
