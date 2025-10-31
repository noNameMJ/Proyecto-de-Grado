using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Geomatica.Desktop.ViewModels;

public sealed class ArchivoItem
{
    public string Nombre { get; set; } = "";
    public DateTime? Fecha { get; set; }
    public double? Lon { get; set; }
    public double? Lat { get; set; }
    public string? CodMpio { get; set; }
    public string? CodDpto { get; set; }
}

public partial class ArchivosViewModel : ObservableObject
{
    public ObservableCollection<ArchivoItem> Items { get; } = new();
    [ObservableProperty] private string estado = "Sin resultados";

    // Recibe el MapaViewModel para suscribirse a los resultados
    public ArchivosViewModel(MapaViewModel mapaVm)
    {
        mapaVm.ProyectosCargados += (_, lista) =>
        {
            Items.Clear();
            foreach (var p in lista)
            {
                Items.Add(new ArchivoItem
                {
                    Nombre = p.Titulo,
                    Fecha = p.Fecha,
                    Lon = p.Lon,
                    Lat = p.Lat,
                    CodMpio = p.CodMpio,
                    CodDpto = p.CodDpto
                });
            }
            Estado = $"{Items.Count} proyectos";
        };
    }
}
