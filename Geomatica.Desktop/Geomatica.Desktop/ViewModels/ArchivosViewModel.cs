using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Geomatica.Data.Repositories;
using System;
using System.Windows;
using System.Collections.Generic;

namespace Geomatica.Desktop.ViewModels
{
    public class CarpetaNode
    {
        public string Ruta { get; set; } = "";
        public string Nombre => Path.GetFileName(Ruta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) switch
        {
            "" => Ruta, // root
            var n => n
        };
        public ObservableCollection<CarpetaNode> Hijas { get; } = new();
    }

    public class ArchivoItem
    {
        public string Nombre { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Tamano { get; set; } = "";
        public DateTime Fecha { get; set; }
        public string RutaCompleta { get; set; } = "";
    }

    public partial class ArchivosViewModel : ObservableObject
    {
        private FiltrosViewModel? _filtros;
        public FiltrosViewModel? Filtros => _filtros;

        // Cambiado: no seleccionar ninguna carpeta por defecto
        [ObservableProperty] private string rutaActual = "";
        public ObservableCollection<CarpetaNode> Carpetas { get; } = new();
        public ObservableCollection<ArchivoItem> Archivos { get; } = new();

        // New combined items collection for Explorer-like view
        public ObservableCollection<object> Items { get; } = new();

        [ObservableProperty] private ArchivoItem? seleccionado;
        [ObservableProperty] private object? selectedEntry;
        [ObservableProperty] private string estado = "";

        // NUEVO: ctor con filtros compartidos (opción B)
        public ArchivosViewModel(FiltrosViewModel filtros)
        {
            _filtros = filtros;
            // When user presses Buscar in the shared filtros view, refresh files and also load projects into the shared resultados
            _filtros.BuscarSolicitado += async (_, __) => { RefrescarSegunFiltros(); await LoadProyectosIntoFiltrosAsync(); };
            // react to project selection in the shared filtros viewmodel
            _filtros.PropertyChanged += Filtros_PropertyChanged;

            ConstruirArbolRaiz();
            RefrescarSegunFiltros();

            // subscribe to property changes to react to SelectedEntry
            this.PropertyChanged += ArchivosViewModel_PropertyChanged;
        }

        // EXISTENTE: compatibilidad
        public ArchivosViewModel() : this(new FiltrosViewModel()) { }

        private void Filtros_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FiltrosViewModel.SelectedProyecto))
            {
                try
                {
                    if (_filtros?.SelectedProyecto is FiltrosViewModel.ProyectoItem p && !string.IsNullOrWhiteSpace(p.Ruta))
                    {
                        // Navigate to project's files
                        RutaActual = p.Ruta!;
                        RefrescarSegunFiltros();
                    }
                    else if (_filtros?.SelectedProyecto == null)
                    {
                        // when selection cleared, clear current view
                        RutaActual = "";
                        ConstruirArbolRaiz();
                        RefrescarSegunFiltros();
                    }
                }
                catch { }
            }
        }

        private void ArchivosViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedEntry")
            {
                if (SelectedEntry is ArchivoItem ai)
                {
                    Seleccionado = ai;
                }
                else
                {
                    Seleccionado = null;
                }
            }
        }

        // Rebuild tree when RutaActual changes
        partial void OnRutaActualChanged(string value)
        {
            ConstruirArbolRaiz();
            RefrescarSegunFiltros();
        }

        private void ConstruirArbolRaiz()
        {
            Carpetas.Clear();
            try
            {
                // If no rutaActual is set, do not populate drives or folders
                if (string.IsNullOrWhiteSpace(RutaActual))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(RutaActual) && Directory.Exists(RutaActual))
                {
                    var nodo = new CarpetaNode { Ruta = RutaActual };
                    CargarHijas(nodo, nivel:1);
                    Carpetas.Add(nodo);
                }
                else
                {
                    // If provided RutaActual does not exist, keep Carpetas empty
                }
            }
            catch { }
        }

        private void CargarHijas(CarpetaNode nodo, int nivel)
        {
            if (nivel >3) return; // limit depth
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(nodo.Ruta))
                {
                    var child = new CarpetaNode { Ruta = dir };
                    nodo.Hijas.Add(child);
                    CargarHijas(child, nivel +1);
                }
            }
            catch { }
        }

        // Mantén Refrescar para uso manual (botones/teclas)
        [RelayCommand]
        private void Refrescar() => RefrescarSegunFiltros();

        // Filtrado según PalabraClave/Desde/Hasta
        private void RefrescarSegunFiltros()
        {
            Archivos.Clear();
            Items.Clear();
            try
            {
                // If no rutaActual is set, do not list drives or files
                if (string.IsNullOrWhiteSpace(RutaActual))
                {
                    Estado = ""; // keep UI empty
                    return;
                }

                if (!string.IsNullOrWhiteSpace(RutaActual) && Directory.Exists(RutaActual))
                {
                    // add subfolders first
                    IEnumerable<string> dirs = Directory.EnumerateDirectories(RutaActual);
                    foreach (var d in dirs)
                    {
                        var node = new CarpetaNode { Ruta = d };
                        Items.Add(node);
                    }

                    // then files
                    IEnumerable<string> files = Directory.EnumerateFiles(RutaActual);

                    if (!string.IsNullOrWhiteSpace(_filtros?.PalabraClave))
                        files = files.Where(f => Path.GetFileName(f).Contains(_filtros.PalabraClave!, StringComparison.OrdinalIgnoreCase));

                    foreach (var f in files)
                    {
                        var fi = new FileInfo(f);
                        if (_filtros?.Desde is DateTime d1 && fi.LastWriteTime < d1) continue;
                        if (_filtros?.Hasta is DateTime d2 && fi.LastWriteTime > d2) continue;

                        var item = new ArchivoItem
                        {
                            Nombre = fi.Name,
                            Tipo = fi.Extension,
                            Tamano = $"{fi.Length /1024.0:0.0} KB",
                            Fecha = fi.LastWriteTime,
                            RutaCompleta = fi.FullName
                        };
                        Archivos.Add(item);
                        Items.Add(item);
                    }
                    Estado = $"{Items.Count} elementos";
                }
                else
                {
                    // RutaActual provided but doesn't exist: keep lists empty
                    Estado = "";
                }
            }
            catch (Exception ex) { Estado = ex.Message; }
        }

        private async Task LoadProyectosIntoFiltrosAsync()
        {
            try
            {
                if (_filtros == null) return;

                IServiceProvider? provider = null;
                if (Application.Current.Properties.Contains("ServiceProvider"))
                    provider = Application.Current.Properties["ServiceProvider"] as IServiceProvider;

                if (provider == null)
                {
                    return;
                }

                var repo = provider.GetService<IProyectoRepository>();
                if (repo == null) return;

                IEnumerable<ProyectoDto> items;

                if (_filtros.AreaInteres is FiltrosViewModel.DepartamentoItem dept)
                {
                    items = await repo.ListarPorDepartamentoAsync(dept.Codigo, _filtros.Desde, _filtros.Hasta, _filtros.PalabraClave);
                }
                else
                {
                    items = await repo.ListarAsync(_filtros.Desde, _filtros.Hasta, _filtros.PalabraClave, null);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _filtros.ResultadosLista.Clear();
                    _filtros.ResultadosResumen.Clear();

                    foreach (var p in items)
                    {
                        _filtros.ResultadosLista.Add(new FiltrosViewModel.ProyectoItem(p.Id, p.Titulo, p.Lon, p.Lat, p.RutaArchivos));
                    }

                    _filtros.ResultadosResumen.Add($"{items.Count()} proyectos");
                    foreach (var p in items.Take(5)) _filtros.ResultadosResumen.Add(p.Titulo);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArchivosViewModel] Error cargando proyectos: {ex}");
            }
        }

        [RelayCommand]
        private void Ir()
        {
            if (Directory.Exists(RutaActual)) RefrescarSegunFiltros();
        }

        [RelayCommand]
        private void Arriba()
        {
            var dir = Directory.GetParent(RutaActual);
            if (dir != null) { RutaActual = dir.FullName; RefrescarSegunFiltros(); }
        }

        [RelayCommand]
        private void Abrir()
        {
            if (Seleccionado == null) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Seleccionado.RutaCompleta) { UseShellExecute = true }); }
            catch (Exception ex) { Estado = ex.Message; }
        }

        [RelayCommand]
        private void Eliminar()
        {
            if (Seleccionado == null) return;
            try { File.Delete(Seleccionado.RutaCompleta); RefrescarSegunFiltros(); }
            catch (Exception ex) { Estado = ex.Message; }
        }

        public void NavegarANodo(CarpetaNode nodo)
        {
            RutaActual = nodo.Ruta;
            RefrescarSegunFiltros();
        }
    }
}
