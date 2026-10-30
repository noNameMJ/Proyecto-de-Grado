using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;

namespace Geomatica.Desktop.ViewModels;

public partial class HistorialArchivoViewModel : ObservableObject
{
    private readonly IProyectoRepository? _proyectoRepository;
    private readonly int _idProyecto;

    [ObservableProperty]
    private string _nombreArchivo = string.Empty;

    [ObservableProperty]
    private string _rutaRelativa = string.Empty;

    [ObservableProperty]
    private string _tituloProyecto = string.Empty;

    [ObservableProperty]
    private bool _soloEliminados;

    public bool EsModoProyecto => !SoloEliminados && string.IsNullOrWhiteSpace(RutaRelativa);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoHistorial))]
    private bool _cargando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoHistorial))]
    private bool _hasHistorial;

    public bool HasNoHistorial => !Cargando && !HasHistorial;

    [ObservableProperty]
    private string _tituloVentana = "Historial de Cambios";

    [ObservableProperty]
    private string _subtituloVentana = string.Empty;

    [ObservableProperty]
    private string _contadorTexto = string.Empty;

    public ObservableCollection<AuditoriaArchivoDto> Historial { get; } = new();

    public event Action? RequestClose;

    public IRelayCommand CopiarRutaCommand { get; }

    public HistorialArchivoViewModel(
        int idProyecto,
        string rutaRelativa,
        string nombreArchivo,
        string tituloProyecto,
        IProyectoRepository? proyectoRepository,
        bool soloEliminados = false,
        Action? cerrarAction = null)
    {
        _idProyecto = idProyecto;
        _rutaRelativa = rutaRelativa;
        _nombreArchivo = nombreArchivo;
        _tituloProyecto = tituloProyecto;
        _proyectoRepository = proyectoRepository;
        _soloEliminados = soloEliminados;

        if (cerrarAction != null)
        {
            RequestClose += cerrarAction;
        }

        if (_soloEliminados)
        {
            TituloVentana = "Registro de Archivos Eliminados";
            SubtituloVentana = $"Trazabilidad de eliminaciones en el proyecto: {tituloProyecto}";
        }
        else if (string.IsNullOrWhiteSpace(rutaRelativa))
        {
            TituloVentana = "Actividad de Archivos del Proyecto";
            SubtituloVentana = $"Historial completo de movimientos de archivos • {tituloProyecto}";
        }
        else
        {
            TituloVentana = $"Historial: {nombreArchivo}";
            SubtituloVentana = $"Movimientos recientes • {tituloProyecto}";
        }

        CopiarRutaCommand = new RelayCommand(CopiarRuta);
    }

    [RelayCommand]
    private void Cerrar()
    {
        RequestClose?.Invoke();
    }

    private void CopiarRuta()
    {
        if (string.IsNullOrWhiteSpace(RutaRelativa)) return;
        try
        {
            Clipboard.SetText(RutaRelativa);
        }
        catch
        {
            // Evitar crash si el portapapeles está bloqueado por otro proceso
        }
    }

    public async Task CargarHistorialAsync(CancellationToken ct = default)
    {
        if (_proyectoRepository == null || _idProyecto <= 0)
        {
            HasHistorial = false;
            return;
        }

        try
        {
            Cargando = true;
            IReadOnlyList<AuditoriaArchivoDto> resultados;

            if (SoloEliminados)
            {
                resultados = await _proyectoRepository.ObtenerArchivosEliminadosProyectoAsync(_idProyecto, limite: 50, ct);
            }
            else if (EsModoProyecto)
            {
                resultados = await _proyectoRepository.ObtenerHistorialArchivosProyectoAsync(_idProyecto, limite: 100, ct);
            }
            else
            {
                resultados = await _proyectoRepository.ObtenerHistorialArchivoAsync(_idProyecto, RutaRelativa, limite: 50, ct);
            }

            Historial.Clear();
            foreach (var item in resultados)
            {
                Historial.Add(item);
            }

            HasHistorial = Historial.Count > 0;
            ContadorTexto = SoloEliminados
                ? $"{Historial.Count} archivo(s) eliminado(s)"
                : (EsModoProyecto
                    ? $"{Historial.Count} movimiento(s)"
                    : $"{Historial.Count} movimiento(s) registrado(s)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HistorialArchivoViewModel] Error al cargar historial: {ex.Message}");
            HasHistorial = false;
        }
        finally
        {
            Cargando = false;
        }
    }
}
