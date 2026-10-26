using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace Geomatica.Desktop.Views
{
    /// <summary>
    /// Pantalla de carga (Splash Screen) institucional para Geomática UIS.
    /// Proporciona retroalimentación visual inmediata durante el inicio, comprobación de BD y ArcGIS Runtime.
    /// </summary>
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Texto del estado principal actual (solo lectura).
        /// </summary>
        public string EstadoActual => TxtEstado.Text;

        /// <summary>
        /// Texto del detalle secundario actual (solo lectura).
        /// </summary>
        public string DetalleActual => TxtDetalle.Text;

        /// <summary>
        /// Valor numérico del progreso actual (0 a 100).
        /// </summary>
        public double ProgresoActual => ProgresoBar.Value;

        /// <summary>
        /// Indica si la barra de progreso está en modo indeterminado.
        /// </summary>
        public bool EsIndeterminado => ProgresoBar.IsIndeterminate;

        /// <summary>
        /// Texto de porcentaje visualizado.
        /// </summary>
        public string PorcentajeTexto => TxtPorcentaje.Text;

        /// <summary>
        /// Actualiza de forma segura para subprocesos el estado y progreso de la pantalla de carga.
        /// </summary>
        public void ActualizarEstado(string estado, string? detalle = null, double? porcentaje = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ActualizarEstado(estado, detalle, porcentaje));
                return;
            }

            TxtEstado.Text = estado;
            if (detalle != null)
            {
                TxtDetalle.Text = detalle;
            }

            if (porcentaje.HasValue)
            {
                ProgresoBar.IsIndeterminate = false;
                ProgresoBar.Value = Math.Clamp(porcentaje.Value, 0, 100);
                TxtPorcentaje.Text = $"{Math.Round(porcentaje.Value)}%";
            }
            else
            {
                ProgresoBar.IsIndeterminate = true;
                TxtPorcentaje.Text = string.Empty;
            }
        }

        /// <summary>
        /// Cierra la pantalla de carga con una animación suave de desvanecimiento (fade out).
        /// </summary>
        public async Task CerrarConFadeOutAsync()
        {
            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(CerrarConFadeOutAsync);
                return;
            }

            try
            {
                var anim = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(200)));
                var tcs = new TaskCompletionSource<bool>();
                anim.Completed += (s, e) =>
                {
                    try
                    {
                        Close();
                    }
                    catch { }
                    tcs.TrySetResult(true);
                };
                BeginAnimation(OpacityProperty, anim);
                await tcs.Task;
            }
            catch
            {
                Close();
            }
        }
    }
}
