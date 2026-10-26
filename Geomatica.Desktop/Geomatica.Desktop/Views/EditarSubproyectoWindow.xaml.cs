using Geomatica.Desktop.ViewModels;
using System.Windows;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Ventana emergente modal para la creación y edición individual de subproyectos y frentes de trabajo.
    /// </summary>
    public partial class EditarSubproyectoWindow : Window
    {
        public EditarSubproyectoViewModel ViewModel { get; }

        public EditarSubproyectoWindow(EditarSubproyectoViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;

            viewModel.RequestClose += (resultado) =>
            {
                DialogResult = resultado;
                Close();
            };
        }
    }
}
