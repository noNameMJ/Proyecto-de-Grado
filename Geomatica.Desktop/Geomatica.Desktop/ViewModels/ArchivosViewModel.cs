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

        [ObservableProperty] private bool canPaste;

        partial void OnSeleccionadoChanged(NodoArchivoVirtual? value)
        {
            OnPropertyChanged(nameof(IsElementoSeleccionado));
            OnPropertyChanged(nameof(IsArchivoSeleccionado));
            OnPropertyChanged(nameof(IsCarpetaSeleccionada));
            OnPropertyChanged(nameof(IsFormatoMapaSeleccionado));
            OnPropertyChanged(nameof(NombreSeleccionado));
        }

        public bool IsElementoSeleccionado => Seleccionado != null;
        public bool IsArchivoSeleccionado => Seleccionado is ArchivoVirtual;
        public bool IsCarpetaSeleccionada => Seleccionado is CarpetaVirtual;
        public bool IsFormatoMapaSeleccionado => EsFormatoSoportadoMapa(Seleccionado);
        public string NombreSeleccionado => Seleccionado?.Nombre ?? "";

        private enum ClipboardOp { None, Copy, Cut }
        private NodoArchivoVirtual? _clipboardNodo;
        private string _clipboardRutaFisica = "";
        private ClipboardOp _clipboardOp = ClipboardOp.None;

        public void ActualizarCanPaste()
        {
            try
            {
                CanPaste = (_clipboardOp != ClipboardOp.None && !string.IsNullOrEmpty(_clipboardRutaFisica))
                           || Clipboard.ContainsFileDropList();
            }
            catch
            {
                CanPaste = _clipboardOp != ClipboardOp.None;
            }
        }

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

        public void ProcesarArchivosDroppeados(string[] files, string? subcarpetaDestino = null)
        {
            if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                _notifications?.ShowWarning("Debe seleccionar un proyecto válido primero.", "Archivos");
                return;
            }

            string rel = string.IsNullOrWhiteSpace(subcarpetaDestino) ? RutaActual : subcarpetaDestino;
            string rutaFisica = Path.Combine(_rutaRaizProyecto, rel.TrimStart('/', '\\'));
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
                        var res = MessageBox.Show($"El archivo '{Path.GetFileName(file)}' ya existe en el destino. ¿Desea sobrescribirlo?", "Confirmar sobrescritura", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                string nombreDestino = string.IsNullOrWhiteSpace(subcarpetaDestino) || subcarpetaDestino == "/" || subcarpetaDestino == "\\"
                    ? "la carpeta actual"
                    : $"'{Path.GetFileName(subcarpetaDestino.TrimEnd('/', '\\'))}'";

                Estado = $"Se subieron {count} archivo(s)";
                _notifications?.ShowSuccess($"Se agregaron {count} archivo(s) a {nombreDestino} correctamente.", "Archivos");
                RefrescarSegunFiltros();
            }
        }

        public bool MoverElementoAFolder(NodoArchivoVirtual nodoAMover, string rutaRelativaDestino)
        {
            if (nodoAMover == null || string.IsNullOrWhiteSpace(_rutaRaizProyecto))
                return false;

            try
            {
                string rutaFisicaOrigen = ObtenerRutaFisica(nodoAMover);
                if (!File.Exists(rutaFisicaOrigen) && !Directory.Exists(rutaFisicaOrigen))
                {
                    _notifications?.ShowError("El elemento seleccionado ya no existe en el disco.", "Mover");
                    return false;
                }

                string rutaFisicaDestinoDir = Path.Combine(_rutaRaizProyecto, rutaRelativaDestino.TrimStart('/', '\\'));
                if (!Directory.Exists(rutaFisicaDestinoDir))
                {
                    _notifications?.ShowError("La carpeta de destino no existe.", "Mover");
                    return false;
                }

                // Validar que el origen y el destino no sean el mismo elemento
                string rutaFisicaDestinoItem = Path.Combine(rutaFisicaDestinoDir, nodoAMover.Nombre);
                if (string.Equals(rutaFisicaOrigen, rutaFisicaDestinoItem, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Mover archivo
                if (File.Exists(rutaFisicaOrigen))
                {
                    if (File.Exists(rutaFisicaDestinoItem))
                    {
                        var res = MessageBox.Show(
                            $"El archivo '{nodoAMover.Nombre}' ya existe en la carpeta destino. ¿Desea sobrescribirlo?",
                            "Confirmar sobrescritura",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (res != MessageBoxResult.Yes) return false;
                        File.Delete(rutaFisicaDestinoItem);
                    }

                    File.Move(rutaFisicaOrigen, rutaFisicaDestinoItem);
                    string nombreCarpeta = string.IsNullOrWhiteSpace(rutaRelativaDestino) || rutaRelativaDestino == "/" || rutaRelativaDestino == "\\"
                        ? "la raíz"
                        : $"'{Path.GetFileName(rutaRelativaDestino.TrimEnd('/', '\\'))}'";

                    _notifications?.ShowSuccess($"Archivo '{nodoAMover.Nombre}' movido a {nombreCarpeta}.", "Mover Archivo");
                    Estado = $"Movido: {nodoAMover.Nombre}";
                    RefrescarSegunFiltros();
                    return true;
                }
                // Mover carpeta
                else if (Directory.Exists(rutaFisicaOrigen))
                {
                    if (rutaFisicaDestinoDir.StartsWith(rutaFisicaOrigen, StringComparison.OrdinalIgnoreCase))
                    {
                        _notifications?.ShowWarning("No se puede mover una carpeta dentro de sí misma.", "Mover");
                        return false;
                    }

                    var carpetasBase = new[] { "Datos_Espaciales", "Documentos", "Entregables", "Otros" };
                    if (carpetasBase.Contains(nodoAMover.Nombre, StringComparer.OrdinalIgnoreCase) && string.IsNullOrEmpty(RutaActual))
                    {
                        _notifications?.ShowWarning("No se pueden mover las carpetas base del proyecto.", "Mover");
                        return false;
                    }

                    if (Directory.Exists(rutaFisicaDestinoItem))
                    {
                        _notifications?.ShowWarning($"Ya existe una carpeta con el nombre '{nodoAMover.Nombre}' en el destino.", "Mover");
                        return false;
                    }

                    Directory.Move(rutaFisicaOrigen, rutaFisicaDestinoItem);
                    string nombreCarpeta = string.IsNullOrWhiteSpace(rutaRelativaDestino) || rutaRelativaDestino == "/" || rutaRelativaDestino == "\\"
                        ? "la raíz"
                        : $"'{Path.GetFileName(rutaRelativaDestino.TrimEnd('/', '\\'))}'";

                    _notifications?.ShowSuccess($"Carpeta '{nodoAMover.Nombre}' movida a {nombreCarpeta}.", "Mover Carpeta");
                    Estado = $"Movida: {nodoAMover.Nombre}";
                    RefrescarSegunFiltros();
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                _notifications?.ShowError("No tiene permisos para mover este elemento en el sistema.", "Acceso Denegado");
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al mover: {ex.Message}", "Error");
            }

            return false;
        }

        public bool MoverHaciaArriba(NodoArchivoVirtual nodoAMover)
        {
            if (string.IsNullOrEmpty(RutaActual) || RutaActual == "/" || RutaActual == "\\")
            {
                _notifications?.ShowInfo("El elemento ya se encuentra en la raíz del proyecto.", "Mover");
                return false;
            }

            var parts = RutaActual.TrimEnd('/', '\\').Split(new[] { '/', '\\' });
            string rutaPadre = parts.Length <= 1 ? "" : string.Join("/", parts.Take(parts.Length - 1));
            return MoverElementoAFolder(nodoAMover, rutaPadre);
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

        public string ObtenerRutaFisica(NodoArchivoVirtual nodo)
        {
            return Path.Combine(_rutaRaizProyecto, nodo.RutaRelativaVirtual.TrimStart('/', '\\'));
        }

        [RelayCommand]
        private void Copiar()
        {
            if (Seleccionado == null || string.IsNullOrWhiteSpace(_rutaRaizProyecto)) return;
            string rutaFisica = ObtenerRutaFisica(Seleccionado);
            if (!File.Exists(rutaFisica) && !Directory.Exists(rutaFisica)) return;

            _clipboardNodo = Seleccionado;
            _clipboardRutaFisica = rutaFisica;
            _clipboardOp = ClipboardOp.Copy;
            CanPaste = true;

            try
            {
                var files = new System.Collections.Specialized.StringCollection { rutaFisica };
                Clipboard.SetFileDropList(files);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Clipboard] Error setting drop list: {ex.Message}");
            }

            Estado = $"Copiado: {Seleccionado.Nombre}";
            _notifications?.ShowInfo($"'{Seleccionado.Nombre}' copiado al portapapeles.", "Copiar");
        }

        [RelayCommand]
        private void Cortar()
        {
            if (Seleccionado == null || string.IsNullOrWhiteSpace(_rutaRaizProyecto)) return;
            string rutaFisica = ObtenerRutaFisica(Seleccionado);
            if (!File.Exists(rutaFisica) && !Directory.Exists(rutaFisica)) return;

            _clipboardNodo = Seleccionado;
            _clipboardRutaFisica = rutaFisica;
            _clipboardOp = ClipboardOp.Cut;
            CanPaste = true;

            Estado = $"Cortado: {Seleccionado.Nombre}";
            _notifications?.ShowInfo($"'{Seleccionado.Nombre}' cortado. Vaya a la carpeta de destino y elija Pegar.", "Cortar");
        }

        [RelayCommand]
        private void Pegar()
        {
            if (string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                _notifications?.ShowWarning("Debe seleccionar un proyecto válido primero.", "Archivos");
                return;
            }

            string targetRelativa = RutaActual;
            string rutaFisicaDestino = Path.Combine(_rutaRaizProyecto, targetRelativa.TrimStart('/', '\\'));
            if (!Directory.Exists(rutaFisicaDestino))
            {
                _notifications?.ShowError("La carpeta destino no existe o no tiene acceso.", "Pegar");
                return;
            }

            // 1. Verificar portapapeles interno de la aplicación
            if (_clipboardOp != ClipboardOp.None && !string.IsNullOrEmpty(_clipboardRutaFisica))
            {
                try
                {
                    if (File.Exists(_clipboardRutaFisica))
                    {
                        string fileName = Path.GetFileName(_clipboardRutaFisica);
                        string destFile = Path.Combine(rutaFisicaDestino, fileName);

                        if (_clipboardOp == ClipboardOp.Copy)
                        {
                            if (string.Equals(_clipboardRutaFisica, destFile, StringComparison.OrdinalIgnoreCase))
                            {
                                string ext = Path.GetExtension(fileName);
                                string baseName = Path.GetFileNameWithoutExtension(fileName);
                                destFile = Path.Combine(rutaFisicaDestino, $"{baseName} - Copia{ext}");
                                int i = 2;
                                while (File.Exists(destFile))
                                {
                                    destFile = Path.Combine(rutaFisicaDestino, $"{baseName} - Copia ({i}){ext}");
                                    i++;
                                }
                            }
                            else if (File.Exists(destFile))
                            {
                                var res = MessageBox.Show($"El archivo '{fileName}' ya existe en el destino. ¿Desea sobrescribirlo?", "Confirmar sobrescritura", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (res != MessageBoxResult.Yes) return;
                            }

                            File.Copy(_clipboardRutaFisica, destFile, true);
                            _notifications?.ShowSuccess($"Archivo '{Path.GetFileName(destFile)}' pegado con éxito.", "Pegar");
                            Estado = $"Pegado: {Path.GetFileName(destFile)}";
                        }
                        else if (_clipboardOp == ClipboardOp.Cut)
                        {
                            if (string.Equals(_clipboardRutaFisica, destFile, StringComparison.OrdinalIgnoreCase))
                                return;

                            if (File.Exists(destFile))
                            {
                                var res = MessageBox.Show($"El archivo '{fileName}' ya existe en el destino. ¿Desea sobrescribirlo?", "Confirmar sobrescritura", MessageBoxButton.YesNo, MessageBoxImage.Question);
                                if (res != MessageBoxResult.Yes) return;
                                File.Delete(destFile);
                            }

                            File.Move(_clipboardRutaFisica, destFile);
                            _notifications?.ShowSuccess($"Archivo '{fileName}' movido con éxito.", "Mover");
                            Estado = $"Movido: {fileName}";
                            _clipboardOp = ClipboardOp.None;
                            _clipboardNodo = null;
                            _clipboardRutaFisica = "";
                            CanPaste = false;
                        }
                        RefrescarSegunFiltros();
                        return;
                    }
                    else if (Directory.Exists(_clipboardRutaFisica))
                    {
                        string dirName = Path.GetFileName(_clipboardRutaFisica);
                        string destDir = Path.Combine(rutaFisicaDestino, dirName);

                        if (_clipboardOp == ClipboardOp.Copy)
                        {
                            if (string.Equals(_clipboardRutaFisica, destDir, StringComparison.OrdinalIgnoreCase))
                            {
                                destDir = Path.Combine(rutaFisicaDestino, $"{dirName} - Copia");
                                int i = 2;
                                while (Directory.Exists(destDir))
                                {
                                    destDir = Path.Combine(rutaFisicaDestino, $"{dirName} - Copia ({i})");
                                    i++;
                                }
                            }
                            CopiarDirectorioRecursivo(_clipboardRutaFisica, destDir);
                            _notifications?.ShowSuccess($"Carpeta '{Path.GetFileName(destDir)}' pegada con éxito.", "Pegar");
                            Estado = $"Pegada: {Path.GetFileName(destDir)}";
                        }
                        else if (_clipboardOp == ClipboardOp.Cut)
                        {
                            if (string.Equals(_clipboardRutaFisica, destDir, StringComparison.OrdinalIgnoreCase))
                                return;

                            if (Directory.Exists(destDir))
                            {
                                _notifications?.ShowWarning($"Ya existe una carpeta con el nombre '{dirName}' en el destino.", "Mover");
                                return;
                            }

                            Directory.Move(_clipboardRutaFisica, destDir);
                            _notifications?.ShowSuccess($"Carpeta '{dirName}' movida con éxito.", "Mover");
                            Estado = $"Movida: {dirName}";
                            _clipboardOp = ClipboardOp.None;
                            _clipboardNodo = null;
                            _clipboardRutaFisica = "";
                            CanPaste = false;
                        }
                        RefrescarSegunFiltros();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError($"Error al pegar: {ex.Message}", "Error");
                    return;
                }
            }

            // 2. Si no hay nada interno, verificar si el portapapeles del sistema tiene archivos
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var fileList = Clipboard.GetFileDropList();
                    if (fileList.Count > 0)
                    {
                        string[] files = new string[fileList.Count];
                        fileList.CopyTo(files, 0);
                        ProcesarArchivosDroppeados(files);
                    }
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error al pegar archivos del portapapeles: {ex.Message}", "Error");
            }
        }

        [RelayCommand]
        private void Renombrar()
        {
            if (Seleccionado == null || string.IsNullOrWhiteSpace(_rutaRaizProyecto)) return;
            string rutaFisica = ObtenerRutaFisica(Seleccionado);
            if (!File.Exists(rutaFisica) && !Directory.Exists(rutaFisica))
            {
                _notifications?.ShowWarning("El elemento seleccionado no existe.", "Renombrar");
                return;
            }

            if (Seleccionado is CarpetaVirtual && string.IsNullOrEmpty(RutaActual))
            {
                var carpetasBase = new[] { "Datos_Espaciales", "Documentos", "Entregables", "Otros" };
                if (carpetasBase.Contains(Seleccionado.Nombre))
                {
                    _notifications?.ShowWarning("No se pueden renombrar las carpetas base del proyecto.", "Advertencia");
                    return;
                }
            }

            var dialog = new Window
            {
                Title = "Renombrar",
                Width = 380,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = SystemColors.ControlBrush
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = $"Nuevo nombre para '{Seleccionado.Nombre}':", 
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold
            });

            var txtNombre = new System.Windows.Controls.TextBox 
            { 
                Text = Seleccionado.Nombre, 
                Margin = new Thickness(0, 0, 0, 15), 
                Padding = new Thickness(3) 
            };
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

            txtNombre.Focus();
            int dotIndex = Seleccionado.Nombre.LastIndexOf('.');
            if (dotIndex > 0 && Seleccionado is ArchivoVirtual)
                txtNombre.Select(0, dotIndex);
            else
                txtNombre.SelectAll();

            if (dialog.ShowDialog() == true)
            {
                string nuevoNombre = txtNombre.Text.Trim();
                if (string.IsNullOrWhiteSpace(nuevoNombre) || nuevoNombre == Seleccionado.Nombre) return;

                var invalidChars = Path.GetInvalidFileNameChars();
                if (nuevoNombre.Any(c => invalidChars.Contains(c)))
                {
                    _notifications?.ShowError("El nombre contiene caracteres no válidos.", "Nombre Inválido");
                    return;
                }

                string parentDir = Path.GetDirectoryName(rutaFisica)!;
                string nuevaRutaFisica = Path.Combine(parentDir, nuevoNombre);

                if (File.Exists(nuevaRutaFisica) || Directory.Exists(nuevaRutaFisica))
                {
                    _notifications?.ShowWarning("Ya existe un archivo o carpeta con ese nombre.", "Aviso");
                    return;
                }

                try
                {
                    if (Seleccionado is CarpetaVirtual)
                    {
                        Directory.Move(rutaFisica, nuevaRutaFisica);
                    }
                    else
                    {
                        File.Move(rutaFisica, nuevaRutaFisica);
                    }
                    _notifications?.ShowSuccess($"Renombrado a '{nuevoNombre}' exitosamente.", "Renombrar");
                    Estado = $"Renombrado: {nuevoNombre}";
                    RefrescarSegunFiltros();
                }
                catch (Exception ex)
                {
                    _notifications?.ShowError($"Error al renombrar: {ex.Message}", "Error");
                }
            }
        }

        [RelayCommand]
        private void CopiarRutaCompleta()
        {
            if (Seleccionado == null || string.IsNullOrWhiteSpace(_rutaRaizProyecto)) return;
            string rutaFisica = ObtenerRutaFisica(Seleccionado);
            try
            {
                Clipboard.SetText(rutaFisica);
                _notifications?.ShowSuccess("Ruta física copiada al portapapeles.", "Copiado");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Clipboard] {ex.Message}");
            }
        }

        [RelayCommand]
        private void MostrarEnExplorador()
        {
            string rutaTarget = "";
            if (Seleccionado != null && !string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                rutaTarget = ObtenerRutaFisica(Seleccionado);
            }
            else if (!string.IsNullOrWhiteSpace(_rutaRaizProyecto))
            {
                rutaTarget = Path.Combine(_rutaRaizProyecto, RutaActual.TrimStart('/', '\\'));
            }

            if (string.IsNullOrWhiteSpace(rutaTarget)) return;

            try
            {
                if (File.Exists(rutaTarget))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{rutaTarget}\"",
                        UseShellExecute = true
                    });
                }
                else if (Directory.Exists(rutaTarget))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{rutaTarget}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                _notifications?.ShowError($"Error abriendo explorador: {ex.Message}", "Error");
            }
        }


        private static void CopiarDirectorioRecursivo(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                CopiarDirectorioRecursivo(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            }
        }
    }
}