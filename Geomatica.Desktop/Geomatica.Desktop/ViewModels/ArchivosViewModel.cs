using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;

namespace Geomatica.Desktop.ViewModels
{
    public class CarpetaNode
    {
        public string Ruta { get; set; } = "";
        public string Nombre => Path.GetFileName(Ruta);
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

        [ObservableProperty] private string rutaActual = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public ObservableCollection<CarpetaNode> Carpetas { get; } = new();
        public ObservableCollection<ArchivoItem> Archivos { get; } = new();

        [ObservableProperty] private ArchivoItem? seleccionado;
        [ObservableProperty] private string estado = "";

        // NUEVO: ctor con filtros compartidos (opción B)
        public ArchivosViewModel(FiltrosViewModel filtros)
        {
            _filtros = filtros;
            _filtros.BuscarSolicitado += (_, __) => RefrescarSegunFiltros();
            ConstruirArbolRaiz();
            RefrescarSegunFiltros();
        }

        // EXISTENTE: compatibilidad
        public ArchivosViewModel() : this(new FiltrosViewModel()) { }

        private void ConstruirArbolRaiz()
        {
            Carpetas.Clear();
            foreach (var unidad in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var nodo = new CarpetaNode { Ruta = unidad.RootDirectory.FullName };
                CargarHijas(nodo, nivel: 1);
                Carpetas.Add(nodo);
            }
        }

        private void CargarHijas(CarpetaNode nodo, int nivel)
        {
            if (nivel > 2) return;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(nodo.Ruta))
                {
                    var child = new CarpetaNode { Ruta = dir };
                    nodo.Hijas.Add(child);
                    CargarHijas(child, nivel + 1);
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
            try
            {
                IEnumerable<string> files = Directory.Exists(RutaActual)
                    ? Directory.EnumerateFiles(RutaActual)
                    : Enumerable.Empty<string>();

                if (!string.IsNullOrWhiteSpace(_filtros?.PalabraClave))
                    files = files.Where(f => Path.GetFileName(f)
                               .Contains(_filtros.PalabraClave!, StringComparison.OrdinalIgnoreCase));

                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    if (_filtros?.Desde is DateTime d1 && fi.LastWriteTime < d1) continue;
                    if (_filtros?.Hasta is DateTime d2 && fi.LastWriteTime > d2) continue;

                    Archivos.Add(new ArchivoItem
                    {
                        Nombre = fi.Name,
                        Tipo = fi.Extension,
                        Tamano = $"{fi.Length / 1024.0:0.0} KB",
                        Fecha = fi.LastWriteTime,
                        RutaCompleta = fi.FullName
                    });
                }
                Estado = $"{Archivos.Count} elementos";
            }
            catch (Exception ex) { Estado = ex.Message; }
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
