using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Geometry;

namespace Geomatica.Desktop.Views
{
    public partial class TablaAtributosView : Window
    {
        private DataTable? _dataTable;
        private Dictionary<DataRow, Geometry?>? _geometrias;
        private Action<Geometry>? _onCentrarCallback;
        private string? _rutaGdb;
        private Geomatica.Desktop.Services.IFileGdbImporterService? _gdbImporter;
        private string? _columnaGlobalId;

        public TablaAtributosView()
        {
            InitializeComponent();
        }

        public void CargarDatos(
            string nombreCapa,
            string icono,
            DataTable dataTable,
            Dictionary<DataRow, Geometry?> geometrias,
            Action<Geometry>? onCentrarCallback,
            string? rutaGdb = null,
            Geomatica.Desktop.Services.IFileGdbImporterService? gdbImporter = null)
        {
            _dataTable = dataTable;
            _geometrias = geometrias;
            _onCentrarCallback = onCentrarCallback;
            _rutaGdb = rutaGdb;
            _gdbImporter = gdbImporter;

            // Detectar columna de GlobalId si existe
            _columnaGlobalId = null;
            if (_dataTable != null)
            {
                foreach (DataColumn c in _dataTable.Columns)
                {
                    if (c.ColumnName.Equals("GlobalId", StringComparison.OrdinalIgnoreCase) ||
                        c.ColumnName.Equals("Global_ID", StringComparison.OrdinalIgnoreCase) ||
                        c.ColumnName.Equals("GlobalID", StringComparison.OrdinalIgnoreCase))
                    {
                        _columnaGlobalId = c.ColumnName;
                        break;
                    }
                }
            }

            TxtIcono.Text = string.IsNullOrWhiteSpace(icono) ? "📊" : icono;
            TxtTitulo.Text = $"Tabla de Atributos — {nombreCapa}";
            TxtSubtitulo.Text = $"{dataTable.Rows.Count:N0} registros cargados";

            GridAtributos.ItemsSource = _dataTable?.DefaultView;
            ActualizarEstadoSeleccion();
        }

        private void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_dataTable == null) return;

            string filtro = TxtBuscar.Text.Trim().Replace("'", "''");
            if (string.IsNullOrWhiteSpace(filtro))
            {
                _dataTable.DefaultView.RowFilter = string.Empty;
                TxtSubtitulo.Text = $"{_dataTable.Rows.Count:N0} registros";
                return;
            }

            var condiciones = new List<string>();
            foreach (DataColumn col in _dataTable.Columns)
            {
                if (col.DataType == typeof(string))
                {
                    condiciones.Add($"[{col.ColumnName}] LIKE '%{filtro}%'");
                }
                else if (col.DataType == typeof(int) || col.DataType == typeof(long) || col.DataType == typeof(short))
                {
                    if (long.TryParse(filtro, out _))
                    {
                        condiciones.Add($"Convert([{col.ColumnName}], 'System.String') LIKE '%{filtro}%'");
                    }
                }
            }

            if (condiciones.Count > 0)
            {
                try
                {
                    _dataTable.DefaultView.RowFilter = string.Join(" OR ", condiciones);
                    TxtSubtitulo.Text = $"{_dataTable.DefaultView.Count:N0} de {_dataTable.Rows.Count:N0} registros";
                }
                catch
                {
                    _dataTable.DefaultView.RowFilter = string.Empty;
                }
            }
        }

        private void GridAtributos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ActualizarEstadoSeleccion();
        }

        private void GridAtributos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CentrarSeleccionado();
        }

        private void BtnCentrar_Click(object sender, RoutedEventArgs e)
        {
            CentrarSeleccionado();
        }

        private void ActualizarEstadoSeleccion()
        {
            if (GridAtributos.SelectedItem is DataRowView drv)
            {
                bool tieneGlobalId = _columnaGlobalId != null && drv.Row[_columnaGlobalId] != null && !string.IsNullOrWhiteSpace(drv.Row[_columnaGlobalId].ToString());
                if (tieneGlobalId && _gdbImporter != null && !string.IsNullOrWhiteSpace(_rutaGdb))
                {
                    BtnVerFotos.Visibility = Visibility.Visible;
                    BtnVerFotos.IsEnabled = true;
                }
                else
                {
                    BtnVerFotos.Visibility = Visibility.Collapsed;
                }

                if (_geometrias != null && _geometrias.TryGetValue(drv.Row, out var geom) && geom != null)
                {
                    BtnCentrar.IsEnabled = true;
                    TxtInfoSeleccion.Text = "Elemento espacial seleccionado. Listo para centrar en el mapa o ver sus fotos.";
                }
                else
                {
                    BtnCentrar.IsEnabled = false;
                    TxtInfoSeleccion.Text = "Fila seleccionada.";
                }
            }
            else
            {
                BtnVerFotos.Visibility = Visibility.Collapsed;
                BtnCentrar.IsEnabled = false;
                TxtInfoSeleccion.Text = "Seleccione una fila para interactuar con el mapa o ver sus fotos.";
            }
        }

        private async void BtnVerFotos_Click(object sender, RoutedEventArgs e)
        {
            if (GridAtributos.SelectedItem is not DataRowView drv || _columnaGlobalId == null || _gdbImporter == null || string.IsNullOrWhiteSpace(_rutaGdb))
                return;

            string gid = drv.Row[_columnaGlobalId]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(gid)) return;

            try
            {
                BtnVerFotos.IsEnabled = false;
                BtnVerFotos.Content = "⏳ Cargando fotos...";

                var adjuntos = await _gdbImporter.ObtenerAdjuntosElementoAsync(_rutaGdb, gid);
                if (adjuntos != null && adjuntos.Count > 0)
                {
                    string titulo = "Fotografías del Árbol";
                    string subtitulo = "";
                    if (_dataTable != null)
                    {
                        var colNombre = _dataTable.Columns["Nombre_com_n"] ?? _dataTable.Columns["Nombre"] ?? _dataTable.Columns["NombreComun"];
                        var colCod = _dataTable.Columns["C_digo_del__rbol"] ?? _dataTable.Columns["Codigo"] ?? _dataTable.Columns["OBJECTID"];
                        if (colCod != null && colNombre != null)
                            subtitulo = $"Código: {drv.Row[colCod]} • {drv.Row[colNombre]}";
                        else if (colCod != null)
                            subtitulo = $"Elemento #{drv.Row[colCod]}";
                    }

                    var visor = new VisorFotoWindow(adjuntos, titulo, subtitulo)
                    {
                        Owner = this
                    };
                    visor.ShowDialog();
                }
                else
                {
                    MessageBox.Show(this, "Este elemento no contiene fotografías adjuntas en la Geodatabase.", "Sin fotografías", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error al obtener fotografías: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                BtnVerFotos.IsEnabled = true;
                BtnVerFotos.Content = "📷 Ver fotografías...";
            }
        }

        private void CentrarSeleccionado()
        {
            if (GridAtributos.SelectedItem is DataRowView drv && _geometrias != null && _geometrias.TryGetValue(drv.Row, out var geom) && geom != null)
            {
                _onCentrarCallback?.Invoke(geom);
            }
        }

        private void BtnExportarCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_dataTable == null) return;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar tabla a archivo CSV",
                Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos|*.*",
                FileName = "tabla_atributos.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    var columnNames = _dataTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName.Replace("\"", "\"\"")}\"");
                    sb.AppendLine(string.Join(",", columnNames));

                    foreach (DataRowView rowView in _dataTable.DefaultView)
                    {
                        var fields = rowView.Row.ItemArray.Select(field =>
                            field == null || field == DBNull.Value ? "" : $"\"{field.ToString()?.Replace("\"", "\"\"")}\"");
                        sb.AppendLine(string.Join(",", fields));
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("Tabla exportada exitosamente a CSV.", "Exportación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al exportar tabla: {ex.Message}", "Error de Exportación", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

