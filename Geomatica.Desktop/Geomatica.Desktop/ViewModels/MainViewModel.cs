using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;

public class MainViewModel
{
    private readonly BuscarProyectosUseCase _buscar;
    public string? Query { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public ObservableCollection<ProyectoGeomatico> Resultados { get; } = new();

    public ICommand BuscarCommand { get; }

    public MainViewModel(BuscarProyectosUseCase buscar)
    {
        _buscar = buscar;
        BuscarCommand = new AsyncRelayCommand(EjecutarBusquedaAsync);
    }

    private async Task EjecutarBusquedaAsync()
    {
        var lista = await _buscar.EjecutarAsync(Query, Desde, Hasta, null, null, null, null);
        Resultados.Clear();
        foreach (var p in lista) Resultados.Add(p);
    }
}