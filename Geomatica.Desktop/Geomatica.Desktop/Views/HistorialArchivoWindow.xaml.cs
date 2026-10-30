using System.Windows;
using Geomatica.Desktop.ViewModels;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Ventana emergente modal para la visualización del historial de cambios (últimos 5) de un archivo o archivos eliminados.
    /// </summary>
    public partial class HistorialArchivoWindow : Window
    {
        public HistorialArchivoViewModel ViewModel { get; }

        public HistorialArchivoWindow(HistorialArchivoViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;

            viewModel.RequestClose += () => Close();

            Loaded += async (s, e) =>
            {
                await ViewModel.CargarHistorialAsync();
            };
        }

        private void Cerrar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

