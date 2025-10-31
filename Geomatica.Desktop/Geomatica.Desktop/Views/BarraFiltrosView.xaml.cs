using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para BarraFiltrosView.xaml
    /// </summary>
    public partial class BarraFiltrosView : UserControl
    {
        public string? PalabraClave { get => (string?)GetValue(PalabraClaveProperty); set => SetValue(PalabraClaveProperty, value); }
        public static readonly DependencyProperty PalabraClaveProperty =
            DependencyProperty.Register(nameof(PalabraClave), typeof(string), typeof(BarraFiltrosView));

        public DateTime? Desde { get => (DateTime?)GetValue(DesdeProperty); set => SetValue(DesdeProperty, value); }
        public static readonly DependencyProperty DesdeProperty =
            DependencyProperty.Register(nameof(Desde), typeof(DateTime?), typeof(BarraFiltrosView));

        public DateTime? Hasta { get => (DateTime?)GetValue(HastaProperty); set => SetValue(HastaProperty, value); }
        public static readonly DependencyProperty HastaProperty =
            DependencyProperty.Register(nameof(Hasta), typeof(DateTime?), typeof(BarraFiltrosView));

        public object? AreaInteres { get => GetValue(AreaInteresProperty); set => SetValue(AreaInteresProperty, value); }
        public static readonly DependencyProperty AreaInteresProperty =
            DependencyProperty.Register(nameof(AreaInteres), typeof(object), typeof(BarraFiltrosView));

        public IEnumerable<object>? ResultadosResumen { get => (IEnumerable<object>?)GetValue(ResultadosResumenProperty); set => SetValue(ResultadosResumenProperty, value); }
        public static readonly DependencyProperty ResultadosResumenProperty =
            DependencyProperty.Register(nameof(ResultadosResumen), typeof(IEnumerable<object>), typeof(BarraFiltrosView));

        public IEnumerable<object>? ResultadosLista { get => (IEnumerable<object>?)GetValue(ResultadosListaProperty); set => SetValue(ResultadosListaProperty, value); }
        public static readonly DependencyProperty ResultadosListaProperty =
            DependencyProperty.Register(nameof(ResultadosLista), typeof(IEnumerable<object>), typeof(BarraFiltrosView));

        public ICommand? BuscarCommand { get => (ICommand?)GetValue(BuscarCommandProperty); set => SetValue(BuscarCommandProperty, value); }
        public static readonly DependencyProperty BuscarCommandProperty =
            DependencyProperty.Register(nameof(BuscarCommand), typeof(ICommand), typeof(BarraFiltrosView));

        public ICommand? DescargarCommand { get => (ICommand?)GetValue(DescargarCommandProperty); set => SetValue(DescargarCommandProperty, value); }
        public static readonly DependencyProperty DescargarCommandProperty =
            DependencyProperty.Register(nameof(DescargarCommand), typeof(ICommand), typeof(BarraFiltrosView));

        public BarraFiltrosView() => InitializeComponent();
    }

}
