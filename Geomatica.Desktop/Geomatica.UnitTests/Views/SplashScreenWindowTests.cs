using System;
using System.Threading;
using FluentAssertions;
using Geomatica.Desktop.Views;
using Xunit;

namespace Geomatica.UnitTests.Views;

public class SplashScreenWindowTests
{
    private static void EjecutarEnSta(Action accion)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                accion();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));

        if (ex != null)
        {
            throw new AggregateException(ex);
        }
    }

    [Fact]
    public void SplashScreenWindow_InicializaYActualizaEstadoCorrectamente()
    {
        EjecutarEnSta(() =>
        {
            var splash = new SplashScreenWindow();
            try
            {
                splash.ActualizarEstado("Cargando base de datos...", "Conectando al puerto 5433...", 45.0);

                splash.EstadoActual.Should().Be("Cargando base de datos...");
                splash.DetalleActual.Should().Be("Conectando al puerto 5433...");
                splash.EsIndeterminado.Should().BeFalse();
                splash.ProgresoActual.Should().Be(45.0);
                splash.PorcentajeTexto.Should().Be("45%");

                // Probar modo indeterminado
                splash.ActualizarEstado("Procesando...", null, null);
                splash.EstadoActual.Should().Be("Procesando...");
                splash.EsIndeterminado.Should().BeTrue();
                splash.PorcentajeTexto.Should().BeEmpty();
            }
            finally
            {
                splash.Close();
            }
        });
    }
}

