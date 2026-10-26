using Geomatica.Desktop.ViewModels;
using System.Windows;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Ventana de revisión y confirmación de importación de subproyectos desde archivo CSV/TXT.
    /// </summary>
    public partial class ImportarSubproyectosCsvWindow : Window
    {
        public ImportarSubproyectosCsvViewModel ViewModel { get; }

        public ImportarSubproyectosCsvWindow(ImportarSubproyectosCsvViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.PuedeAceptar)
            {
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

