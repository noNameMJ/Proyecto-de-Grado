using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Geomatica.Desktop.Models;
using Geomatica.Desktop.ViewModels;

namespace Geomatica.Desktop.Views
{
    public partial class ArchivosView : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragging;
        private ListViewItem? _currentHoverItem;
        private Brush? _originalHoverBackground;
        private static readonly Brush DropTargetHighlight = new SolidColorBrush(Color.FromArgb(100, 0x43, 0xA0, 0x47)); // Soft UIS green

        public ArchivosView() 
        { 
            InitializeComponent();
            this.DataContextChanged += ArchivosView_DataContextChanged;
            this.Loaded += ArchivosView_Loaded;
            this.Unloaded += ArchivosView_Unloaded;
        }

        private void ArchivosView_Loaded(object? sender, RoutedEventArgs e)
        {
            AttachToFiltros(DataContext as ArchivosViewModel);
        }

        private void ArchivosView_Unloaded(object? sender, RoutedEventArgs e)
        {
            DetachFromFiltros(DataContext as ArchivosViewModel);
        }

        private void ArchivosView_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is ArchivosViewModel oldVm)
            {
                DetachFromFiltros(oldVm);
            }

            if (e.NewValue is ArchivosViewModel newVm)
            {
                AttachToFiltros(newVm);
            }
        }

        private void AttachToFiltros(ArchivosViewModel? vm)
        {
            // Omitted to avoid redundant handling since ViewModel manages it
        }

        private void DetachFromFiltros(ArchivosViewModel? vm)
        {
        }

        private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                item.IsSelected = true;
                item.Focus();
                if (DataContext is ArchivosViewModel vm && item.DataContext is NodoArchivoVirtual nodo)
                {
                    vm.Seleccionado = nodo;
                    vm.SelectedEntry = nodo;
                }
            }
        }

        private void ListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DataContext is ArchivosViewModel vm)
            {
                DependencyObject? dep = e.OriginalSource as DependencyObject;
                while (dep != null && dep != listViewArchivos && dep is not ListViewItem)
                {
                    dep = VisualTreeHelper.GetParent(dep);
                }

                if (dep is not ListViewItem)
                {
                    listViewArchivos.SelectedItem = null;
                    vm.SelectedEntry = null;
                    vm.Seleccionado = null;
                }

                vm.ActualizarCanPaste();
            }
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Solo responder a doble clic IZQUIERDO. El clic derecho nunca debe abrir el archivo.
            if (e.ChangedButton != MouseButton.Left) return;

            if (DataContext is ArchivosViewModel vm && ((FrameworkElement)e.OriginalSource).DataContext != null)
            {
                var item = ((FrameworkElement)e.OriginalSource).DataContext;
                if (item is NodoArchivoVirtual nodo)
                {
                    vm.Seleccionado = nodo;
                    vm.SelectedEntry = nodo;

                    if (nodo is CarpetaVirtual)
                    {
                        vm.AbrirCommand.Execute(null);
                    }
                    else if (ArchivosViewModel.EsFormatoSoportadoMapa(nodo))
                    {
                        vm.AbrirEnMapaCommand.Execute(null);
                    }
                    else
                    {
                        vm.AbrirCommand.Execute(null);
                    }
                    e.Handled = true;
                }
            }
        }

        #region Drag and Drop Handling

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(listViewArchivos);
            _isDragging = false;
        }

        private void ListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;

            Point currentPos = e.GetPosition(listViewArchivos);
            Vector diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                ListViewItem? item = GetListViewItemAt(_dragStartPoint);
                if (item?.DataContext is NodoArchivoVirtual nodo)
                {
                    _isDragging = true;
                    var data = new DataObject();
                    data.SetData("Geomatica.NodoVirtual", nodo);

                    if (DataContext is ArchivosViewModel vm)
                    {
                        try
                        {
                            string physicalPath = vm.ObtenerRutaFisica(nodo);
                            if (File.Exists(physicalPath) || Directory.Exists(physicalPath))
                            {
                                var files = new System.Collections.Specialized.StringCollection { physicalPath };
                                data.SetFileDropList(files);
                            }
                        }
                        catch { }
                    }

                    try
                    {
                        DragDrop.DoDragDrop(item, data, DragDropEffects.Move | DragDropEffects.Copy);
                    }
                    finally
                    {
                        _isDragging = false;
                        ClearDropHighlight();
                    }
                }
            }
        }

        private ListViewItem? GetListViewItemAt(Point point)
        {
            HitTestResult hitResult = VisualTreeHelper.HitTest(listViewArchivos, point);
            DependencyObject? obj = hitResult?.VisualHit;
            while (obj != null && obj != listViewArchivos)
            {
                if (obj is ListViewItem item)
                    return item;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private void SetHoverItem(ListViewItem? item)
        {
            if (_currentHoverItem == item) return;

            ClearDropHighlight();

            if (item != null)
            {
                _currentHoverItem = item;
                _originalHoverBackground = item.Background;
                item.Background = DropTargetHighlight;
            }
        }

        private void ClearDropHighlight()
        {
            if (_currentHoverItem != null)
            {
                _currentHoverItem.Background = _originalHoverBackground;
                _currentHoverItem = null;
                _originalHoverBackground = null;
            }
        }

        private void ListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            bool isVirtual = e.Data.GetDataPresent("Geomatica.NodoVirtual");
            bool isFileDrop = e.Data.GetDataPresent(DataFormats.FileDrop);

            if (!isVirtual && !isFileDrop)
            {
                ClearDropHighlight();
                e.Handled = true;
                return;
            }

            Point pt = e.GetPosition(listViewArchivos);
            ListViewItem? item = GetListViewItemAt(pt);

            if (item?.DataContext is CarpetaVirtual carpetaDestino)
            {
                if (isVirtual && e.Data.GetData("Geomatica.NodoVirtual") is NodoArchivoVirtual draggedNodo)
                {
                    if (draggedNodo == carpetaDestino)
                    {
                        ClearDropHighlight();
                        e.Handled = true;
                        return;
                    }
                }

                e.Effects = isVirtual ? DragDropEffects.Move : DragDropEffects.Copy;
                SetHoverItem(item);
            }
            else
            {
                ClearDropHighlight();
                if (isFileDrop && !isVirtual)
                {
                    e.Effects = DragDropEffects.Copy;
                }
            }

            e.Handled = true;
        }

        private void ListView_DragLeave(object sender, DragEventArgs e)
        {
            Point pt = e.GetPosition(listViewArchivos);
            if (pt.X < 0 || pt.Y < 0 || pt.X > listViewArchivos.ActualWidth || pt.Y > listViewArchivos.ActualHeight)
            {
                ClearDropHighlight();
            }
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            try
            {
                Point pt = e.GetPosition(listViewArchivos);
                ListViewItem? item = GetListViewItemAt(pt);
                CarpetaVirtual? targetFolder = item?.DataContext as CarpetaVirtual;

                if (DataContext is not ArchivosViewModel vm) return;

                if (e.Data.GetDataPresent("Geomatica.NodoVirtual"))
                {
                    if (e.Data.GetData("Geomatica.NodoVirtual") is NodoArchivoVirtual draggedNodo)
                    {
                        if (targetFolder != null && targetFolder != draggedNodo)
                        {
                            vm.MoverElementoAFolder(draggedNodo, targetFolder.RutaRelativaVirtual);
                        }
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        string? destFolder = targetFolder?.RutaRelativaVirtual;
                        vm.ProcesarArchivosDroppeados(files, destFolder);
                    }
                }
            }
            finally
            {
                ClearDropHighlight();
                e.Handled = true;
            }
        }

        private void BtnArriba_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            if (DataContext is ArchivosViewModel vm && !string.IsNullOrEmpty(vm.RutaActual))
            {
                if (e.Data.GetDataPresent("Geomatica.NodoVirtual") || e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = e.Data.GetDataPresent("Geomatica.NodoVirtual") ? DragDropEffects.Move : DragDropEffects.Copy;
                    btnArriba.Background = DropTargetHighlight;
                }
            }
            e.Handled = true;
        }

        private void BtnArriba_DragLeave(object sender, DragEventArgs e)
        {
            btnArriba.ClearValue(Button.BackgroundProperty);
        }

        private void BtnArriba_Drop(object sender, DragEventArgs e)
        {
            btnArriba.ClearValue(Button.BackgroundProperty);
            if (DataContext is not ArchivosViewModel vm) return;

            if (e.Data.GetDataPresent("Geomatica.NodoVirtual"))
            {
                if (e.Data.GetData("Geomatica.NodoVirtual") is NodoArchivoVirtual draggedNodo)
                {
                    vm.MoverHaciaArriba(draggedNodo);
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    if (!string.IsNullOrEmpty(vm.RutaActual))
                    {
                        var parts = vm.RutaActual.TrimEnd('/', '\\').Split(new[] { '/', '\\' });
                        string rutaPadre = parts.Length <= 1 ? "" : string.Join("/", parts.Take(parts.Length - 1));
                        vm.ProcesarArchivosDroppeados(files, rutaPadre);
                    }
                }
            }
            e.Handled = true;
        }

        #endregion
    }
}

