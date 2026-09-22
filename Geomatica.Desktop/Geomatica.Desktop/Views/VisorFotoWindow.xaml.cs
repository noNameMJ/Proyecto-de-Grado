using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Geomatica.Desktop.Models;
using Microsoft.Win32;

namespace Geomatica.Desktop.Views
{
    public partial class VisorFotoWindow : Window
    {
        private readonly IReadOnlyList<AdjuntoFotoInfo> _adjuntos;
        private int _indiceActual;

        public VisorFotoWindow(IReadOnlyList<AdjuntoFotoInfo> adjuntos, string titulo, string subtitulo, int indiceInicial = 0)
        {
            InitializeComponent();
            _adjuntos = adjuntos ?? new List<AdjuntoFotoInfo>();

            TxtTitulo.Text = !string.IsNullOrWhiteSpace(titulo) ? titulo : "Fotografías del Árbol";
            TxtSubtitulo.Text = !string.IsNullOrWhiteSpace(subtitulo) ? subtitulo : "";

            ListMiniaturas.ItemsSource = _adjuntos;

            if (_adjuntos.Count > 0)
            {
                int idx = Math.Clamp(indiceInicial, 0, _adjuntos.Count - 1);
                MostrarFoto(idx);
            }
            else
            {
                TxtSinImagen.Visibility = Visibility.Visible;
                BtnAnterior.IsEnabled = false;
                BtnSiguiente.IsEnabled = false;
                TxtContador.Text = "0 fotografías";
                TxtInfoFoto.Text = "No hay fotografías adjuntas disponibles.";
            }
        }

        private void MostrarFoto(int indice)
        {
            if (indice < 0 || indice >= _adjuntos.Count) return;

            _indiceActual = indice;
            var adjunto = _adjuntos[indice];

            try
            {
                if (File.Exists(adjunto.RutaArchivoLocal))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(adjunto.RutaArchivoLocal, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();

                    ImgPrincipal.Source = bmp;
                    TxtSinImagen.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ImgPrincipal.Source = null;
                    TxtSinImagen.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VisorFotoWindow] Error cargando imagen '{adjunto.RutaArchivoLocal}': {ex.Message}");
                ImgPrincipal.Source = null;
                TxtSinImagen.Visibility = Visibility.Visible;
            }

            TxtContador.Text = $"Fotografía {indice + 1} de {_adjuntos.Count}";
            TxtInfoFoto.Text = $"Archivo: {adjunto.Nombre} • Tamaño: {adjunto.TamanoLegible}";

            BtnAnterior.IsEnabled = indice > 0;
            BtnSiguiente.IsEnabled = indice < _adjuntos.Count - 1;

            if (ListMiniaturas.SelectedIndex != indice)
            {
                ListMiniaturas.SelectedIndex = indice;
                ListMiniaturas.ScrollIntoView(adjunto);
            }
        }

        private void BtnAnterior_Click(object sender, RoutedEventArgs e)
        {
            if (_indiceActual > 0)
            {
                MostrarFoto(_indiceActual - 1);
            }
        }

        private void BtnSiguiente_Click(object sender, RoutedEventArgs e)
        {
            if (_indiceActual < _adjuntos.Count - 1)
            {
                MostrarFoto(_indiceActual + 1);
            }
        }

        private void ListMiniaturas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListMiniaturas.SelectedIndex >= 0 && ListMiniaturas.SelectedIndex != _indiceActual)
            {
                MostrarFoto(ListMiniaturas.SelectedIndex);
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (_indiceActual < 0 || _indiceActual >= _adjuntos.Count) return;

            var adjunto = _adjuntos[_indiceActual];
            if (!File.Exists(adjunto.RutaArchivoLocal)) return;

            string ext = Path.GetExtension(adjunto.Nombre);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";

            var sfd = new SaveFileDialog
            {
                Title = "Guardar fotografía del árbol",
                FileName = adjunto.Nombre,
                Filter = $"Imagen (*{ext})|*{ext}|Todos los archivos (*.*)|*.*"
            };

            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    File.Copy(adjunto.RutaArchivoLocal, sfd.FileName, true);
                    MessageBox.Show(this, "Fotografía guardada con éxito.", "Guardar", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error al guardar el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnVisorExterno_Click(object sender, RoutedEventArgs e)
        {
            if (_indiceActual < 0 || _indiceActual >= _adjuntos.Count) return;

            var adjunto = _adjuntos[_indiceActual];
            if (!File.Exists(adjunto.RutaArchivoLocal)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = adjunto.RutaArchivoLocal,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"No se pudo abrir el visor externo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left)
            {
                BtnAnterior_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                BtnSiguiente_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}

