using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using Geomatica.Data.Repositories;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using System.Windows;
using Geomatica.Desktop.Services;
using Geomatica.Desktop.Models;

namespace Geomatica.Desktop.ViewModels
{
    public partial class ArchivosViewModel : ObservableObject
    {
        private FiltrosViewModel? _filtros;
        private readonly ProyectoArchivosService _archivosService;
        private readonly IProyectoRepository _proyectoRepository;
        private readonly INotificationService? _notifications;
        
        public FiltrosViewModel? Filtros => _filtros;

        // La ruta real física donde reside el proyecto completo
        private string _rutaRaizProyecto = "";

        // En la UI mostramos la ruta relativa virtual. Vacio ("") es la raíz del proyecto.
        [ObservableProperty] private string rutaActual = "";
        
        [ObservableProperty] private string busquedaTexto = "";

        public ObservableCollection<object> Items { get; } = new();

        [ObservableProperty] private NodoArchivoVirtual? seleccionado;
        [ObservableProperty] private object? selectedEntry;
        [ObservableProperty] private string estado = "";

        [ObservableProperty] private FichaProyectoViewModel? proyectoDetalle;
        [ObservableProperty] private bool isExtendido;

        public string ExtenderBotonTexto => IsExtendido ? "🗗 Reducir" : "⛶ Extender";
        public string ExtenderBotonTooltip => IsExtendido ? "Restaurar tamaño normal del panel de archivos" : "Extender panel de archivos a pantalla completa";

        partial void OnIsExtendidoChanged(bool value)
        {
            OnPropertyChanged(nameof(ExtenderBotonTexto));
            OnPropertyChanged(nameof(ExtenderBotonTooltip));
        }

        [RelayCommand]
        private void ToggleExtender()
        {
            IsExtendido = !IsExtendido;
        }

        public bool HasProyectoDetalle => ProyectoDetalle != null;
        public bool ShowEmptyState => Items.Count == 0 && string.IsNullOrWhiteSpace(_rutaRaizProyecto);

        partial void OnProyectoDetalleChanged(FichaProyectoViewModel? value)
        {
            OnPropertyChanged(nameof(HasProyectoDetalle));
            if (value != null && !string.IsNullOrWhiteSpace(value.RutaArchivos))
            {
                _rutaRaizProyecto = value.RutaArchivos;
                if (RutaActual == "") RefrescarSegunFiltros();
                else RutaActual = ""; // raíz virtual
            }
        }

        public event EventHandler<string>? AbrirEnMapaSolicitado;

        public ArchivosViewModel(
            FiltrosViewModel filtros, 
            ProyectoArchivosService archivosService, 
            IProyectoRepository proyectoRepository,
            INotificationService? notifications = null)
        {
            _filtros = filtros;
            _archivosService = archivosService;
            _proyectoRepository = proyectoRepository;
            _notifications = notifications;

            _filtros.BuscarSolicitado += async (_, __) => { RefrescarSegunFiltros(); await LoadProyectosIntoFiltrosAsync(); };
            _filtros.PropertyChanged += Filtros_PropertyChanged;

            RefrescarSegunFiltros();

            this.PropertyChanged += ArchivosViewModel_PropertyChanged;
        }

        private void Filtros_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FiltrosViewModel.SelectedProyecto))
            {
                try
                {
                    if (_filtros?.SelectedProyecto is FiltrosViewModel.ProyectoItem p && !string.IsNullOrWhiteSpace(p.Ruta))
                    {
                        _rutaRaizProyecto = p.Ruta;
                        if (RutaActual == "") RefrescarSegunFiltros();
                        else RutaActual = "";
                    }
                    else if (_filtros?.SelectedProyecto == null)
                    {
                        _rutaRaizProyecto = "";
                        ProyectoDetalle = null;
                        if (RutaActual == "") RefrescarSegunFiltros();
                        else RutaActual = "";
                    }
                }
                catch { }
            }
        }

        private void ArchivosViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectedEntry))
            {
                if (SelectedEntry is NodoArchivoVirtual ai)
                {
                    Seleccionado = ai;
                }
                else
                {
                    Seleccionado = null;
                }
            }
            else if (e.PropertyName == nameof(RutaActual))
            {
                RefrescarSegunFiltros();
            }
            else if (e.PropertyName == nameof(BusquedaTexto))
            {
                RefrescarSegunFiltros();
            }
        }

        [RelayCommand]
        private void Refrescar() => RefrescarSegunFiltros();

        private void RefrescarSegunFiltros()
        {
            Items.Clear();
            try
            {
                if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
                {
                    Estado = "";
                    return;
                }

                var nodos = _archivosService.ListarContenidoVirtual(_rutaRaizProyecto, RutaActual);

                // Aplicar filtros locales si existen
                if (!string.IsNullOrWhiteSpace(_filtros?.PalabraClave))
                {
                    nodos = nodos.Where(n => n.EsCarpeta || n.Nombre.Contains(_filtros.PalabraClave, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Aplicar búsqueda específica en archivos
                if (!string.IsNullOrWhiteSpace(BusquedaTexto))
                {
                    nodos = nodos.Where(n => n.Nombre.Contains(BusquedaTexto, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                foreach (var nodo in nodos)
                {
                    Items.Add(nodo);
                }

                Estado = $"{Items.Count} elementos";
                OnPropertyChanged(nameof(ShowEmptyState));
            }
            catch (Exception ex) 
            { 
                Estado = ex.Message; 
            }
        }

        private async Task LoadProyectosIntoFiltrosAsync()
        {
            try
            {
                if (_filtros == null) return;
                var repo = _proyectoRepository;

                IEnumerable<ProyectoDto> items;
                if (_filtros.AreaInteres is FiltrosViewModel.MunicipioItem muni && !string.IsNullOrEmpty(muni.Codigo))
                    items = await repo.ListarPorMunicipioAsync(muni.Codigo, _filtros.Desde, _filtros.Hasta, _filtros.PalabraClave);
                else if (_filtros.SelectedDepartamento is FiltrosViewModel.DepartamentoItem dept && !string.IsNullOrEmpty(dept.Codigo))
                    items = await repo.ListarPorDepartamentoAsync(dept.Codigo, _filtros.Desde, _filtros.Hasta, _filtros.PalabraClave);
                else
                    items = await repo.ListarAsync(_filtros.Desde, _filtros.Hasta, _filtros.PalabraClave, null);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _filtros.ResultadosLista.Clear();
                    _filtros.ResultadosResumen.Clear();
                    foreach (var p in items) _filtros.ResultadosLista.Add(new FiltrosViewModel.ProyectoItem(p.Id, p.Titulo, p.Lon, p.Lat, p.RutaArchivos));
                    int total = items.Count();
                    string textoConteo = total == 1 ? "1 proyecto encontrado" : $"{total} proyectos encontrados";
                    _filtros.ResultadosResumen.Add(textoConteo);
                    _filtros.NotificarResultadosCargados();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArchivosViewModel] Error cargando proyectos: {ex}");
                await Application.Current.Dispatcher.InvokeAsync(() => _filtros?.NotificarResultadosCargados());
            }
        }

        public void ProcesarArchivosDroppeados(string[] files)
        {
            if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                _notifications?.ShowWarning("Debe seleccionar un proyecto válido primero.", "Archivos");
                return;
            }

            string rutaFisica = Path.Combine(_rutaRaizProyecto, RutaActual.TrimStart('/', '\\'));
            if (!Directory.Exists(rutaFisica))
            {
                _notifications?.ShowError("La carpeta de destino no existe o no tiene acceso.", "Error de Acceso");
                return;
            }

            int count = 0;
            foreach (var file in files)
            {
                try
                {
                    if (Directory.Exists(file)) continue;

                    var dest = Path.Combine(rutaFisica, Path.GetFileName(file));
                    if (File.Exists(dest))
                    {
                        var res = MessageBox.Show($"El archivo '{Path.GetFileName(file)}' ya existe. ¿Desea sobrescribirlo?", "Confirmar sobrescritura", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (res != MessageBoxResult.Yes) continue;
                    }
                    File.Copy(file, dest, true);
                    count++;
                }
                catch (UnauthorizedAccessException)
                {
                    _notifications?.ShowError("No tiene permisos para subir archivos en esta carpeta.", "Acceso Denegado");
                    break;
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError($"Error subiendo '{Path.GetFileName(file)}': {ex.Message}", "Error al Subir");
                }
            }

            if (count > 0)
            {
                Estado = $"Se subieron {count} archivo(s)";
                _notifications?.ShowSuccess($"Se subieron {count} archivo(s) correctamente.", "Archivos");
                RefrescarSegunFiltros();
            }
        }

        [RelayCommand]
        private void Ir()
        {
            RefrescarSegunFiltros();
        }

        [RelayCommand]
        private void Arriba()
        {
            if (string.IsNullOrEmpty(RutaActual) || RutaActual == "/" || RutaActual == "\\") return;
            var parts = RutaActual.TrimEnd('/', '\\').Split(new[] { '/', '\\' });
            if (parts.Length <= 1)
            {
                RutaActual = "";
            }
            else
            {
                RutaActual = string.Join("/", parts.Take(parts.Length - 1));
            }
        }

        [RelayCommand]
        private void Subir()
        {
            if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                _notifications?.ShowWarning("Debe seleccionar un proyecto válido primero.", "Archivos");
                return;
            }

            string rutaFisica = Path.Combine(_rutaRaizProyecto, RutaActual.TrimStart('/', '\\'));
            if (!Directory.Exists(rutaFisica))
            {
                _notifications?.ShowError("La carpeta de destino no existe o no tiene acceso.", "Error de Acceso");
                return;
            }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar archivos para subir",
                Multiselect = true,
                Filter = "Todos los archivos (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                int count = 0;
                foreach (var file in ofd.FileNames)
                {
                    try
                    {
                        var dest = Path.Combine(rutaFisica, Path.GetFileName(file));
                        if (File.Exists(dest))
                        {
                            var res = MessageBox.Show($"El archivo '{Path.GetFileName(file)}' ya existe. ¿Desea sobrescribirlo?", "Confirmar sobrescritura", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (res != MessageBoxResult.Yes) continue;
                        }
                        File.Copy(file, dest, true);
                        count++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _notifications?.ShowError("No tiene permisos para subir archivos en esta carpeta.", "Acceso Denegado");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _notifications?.ShowError($"Error subiendo '{Path.GetFileName(file)}': {ex.Message}", "Error al Subir");
                    }
                }

                if (count > 0)
                {
                    Estado = $"Se subieron {count} archivo(s)";
                    _notifications?.ShowSuccess($"Se subieron {count} archivo(s) correctamente.", "Archivos");
                    RefrescarSegunFiltros();
                }
            }
        }

        [RelayCommand]
        private void NuevaCarpeta()
        {
            if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                _notifications?.ShowWarning("Debe seleccionar un proyecto válido primero.", "Archivos");
                return;
            }

            string rutaFisica = Path.Combine(_rutaRaizProyecto, RutaActual.TrimStart('/', '\\'));
            if (!Directory.Exists(rutaFisica))
            {
                _notifications?.ShowError("La carpeta de destino no existe o no tiene acceso.", "Error de Acceso");
                return;
            }

            var dialog = new Window
            {
                Title = "Nueva Carpeta",
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = SystemColors.ControlBrush
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "Nombre de la nueva carpeta:", Margin = new Thickness(0, 0, 0, 10) });

            var txtNombre = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 15), Padding = new Thickness(3) };
            if (Application.Current != null && Application.Current.MainWindow != null)
                txtNombre.FontFamily = Application.Current.MainWindow.FontFamily;
            stack.Children.Add(txtNombre);

            var panelBotones = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnAceptar = new System.Windows.Controls.Button { Content = "Aceptar", Width = 80, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(3), IsDefault = true };
            var btnCancelar = new System.Windows.Controls.Button { Content = "Cancelar", Width = 80, Padding = new Thickness(3), IsCancel = true };

            btnAceptar.Click += (s, e) => { dialog.DialogResult = true; };
            panelBotones.Children.Add(btnAceptar);
            panelBotones.Children.Add(btnCancelar);

            stack.Children.Add(panelBotones);
            dialog.Content = stack;

            if (dialog.ShowDialog() == true)
            {
                var nombre = txtNombre.Text.Trim();
                if (string.IsNullOrWhiteSpace(nombre)) return;

                var invalidChars = Path.GetInvalidFileNameChars();
                if (nombre.Any(c => invalidChars.Contains(c)))
                {
                    _notifications?.ShowError("El nombre de la carpeta contiene caracteres no válidos.", "Nombre Inválido");
                    return;
                }

                var nuevaRuta = Path.Combine(rutaFisica, nombre);
                if (Directory.Exists(nuevaRuta) || File.Exists(nuevaRuta))
                {
                    _notifications?.ShowWarning("Ya existe un archivo o carpeta con ese nombre.", "Aviso");
                    return;
                }

                try
                {
                    Directory.CreateDirectory(nuevaRuta);
                    Estado = $"Carpeta '{nombre}' creada";
                    _notifications?.ShowSuccess($"Carpeta '{nombre}' creada exitosamente.", "Carpeta Creada");
                    RefrescarSegunFiltros();
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError($"Error al crear la carpeta: {ex.Message}", "Error");
                }
            }
        }

        [RelayCommand]
        private void Descargar()
        {
            if (Seleccionado is not ArchivoVirtual archivo)
            {
                _notifications?.ShowWarning("Seleccione un archivo para descargar.", "Atención");
                return;
            }

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar archivo como...",
                FileName = archivo.Nombre,
                Filter = string.IsNullOrEmpty(archivo.Extension) ? "Todos los archivos|*.*" : $"Archivo ({archivo.Extension})|*{archivo.Extension}|Todos los archivos|*.*"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string rutaFisica = Path.Combine(_rutaRaizProyecto, archivo.RutaRelativaVirtual.TrimStart('/', '\\'));
                    File.Copy(rutaFisica, sfd.FileName, true);
                    Estado = $"Descargado: {archivo.Nombre}";
                    _notifications?.ShowSuccess($"Archivo '{archivo.Nombre}' descargado exitosamente.", "Descarga Completa");
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError($"Error descargando archivo: {ex.Message}", "Error de Descarga");
                }
            }
        }

        private static readonly HashSet<string> FormatosSoportadosMapa = new(StringComparer.OrdinalIgnoreCase)
        {
            ".shp", ".kml", ".kmz", ".gdb", ".geodatabase", ".slpk", ".las", ".laz", ".zlas", ".tif", ".tiff"
        };

        public static bool EsFormatoSoportadoMapa(NodoArchivoVirtual? nodo)
        {
            if (nodo == null) return false;
            if (nodo is CarpetaVirtual carpeta)
            {
                return carpeta.Nombre.EndsWith(".gdb", StringComparison.OrdinalIgnoreCase);
            }
            if (nodo is ArchivoVirtual archivo)
            {
                var ext = archivo.Extension;
                if (string.IsNullOrWhiteSpace(ext)) return false;
                if (!ext.StartsWith(".")) ext = "." + ext;
                return FormatosSoportadosMapa.Contains(ext);
            }
            return false;
        }

        [RelayCommand]
        private void AbrirEnMapa()
        {
            if (Seleccionado != null)
            {
                string rutaFisica = Path.Combine(_rutaRaizProyecto, Seleccionado.RutaRelativaVirtual.TrimStart('/', '\\'));
                if (File.Exists(rutaFisica) || Directory.Exists(rutaFisica))
                {
                    AbrirEnMapaSolicitado?.Invoke(this, rutaFisica);
                }
            }
        }

        [RelayCommand]
        private void Abrir()
        {
            if (Seleccionado == null) return;
            try 
            {
                if (Seleccionado is CarpetaVirtual carpeta)
                {
                    RutaActual = carpeta.RutaRelativaVirtual;
                }
                else if (Seleccionado is ArchivoVirtual archivo)
                {
                    string rutaFisica = Path.Combine(_rutaRaizProyecto, archivo.RutaRelativaVirtual.TrimStart('/', '\\'));
                    // Copiar a temp para proteger el original y evitar bloqueos en red
                    string dest = Path.Combine(Path.GetTempPath(), archivo.Nombre);
                    File.Copy(rutaFisica, dest, true);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true }); 
                }
            }
            catch (Exception ex) { Estado = ex.Message; }
        }

        [RelayCommand]
        private void Eliminar()
        {
            if (Seleccionado == null) return;
            
            var tipo = Seleccionado is CarpetaVirtual ? "la carpeta" : "el archivo";
            var result = MessageBox.Show(
                $"¿Está seguro de que desea eliminar {tipo} '{Seleccionado.Nombre}'?\n\nEsta acción no se puede deshacer.",
                "Confirmar eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            
            try 
            { 
                string rutaFisica = Path.Combine(_rutaRaizProyecto, Seleccionado.RutaRelativaVirtual.TrimStart('/', '\\'));
                if (Seleccionado is CarpetaVirtual)
                {
                    // No permitir eliminar las carpetas base del proyecto
                    if (string.IsNullOrEmpty(RutaActual))
                    {
                        var carpetasBase = new[] { "Datos_Espaciales", "Documentos", "Entregables", "Otros" };
                        if (carpetasBase.Contains(Seleccionado.Nombre))
                        {
                            MessageBox.Show("No se pueden eliminar las carpetas base del proyecto.", "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    Directory.Delete(rutaFisica, true);
                }
                else
                {
                    File.Delete(rutaFisica);
                }
                RefrescarSegunFiltros(); 
            }
            catch (Exception ex) { Estado = ex.Message; }
        }
    }
}