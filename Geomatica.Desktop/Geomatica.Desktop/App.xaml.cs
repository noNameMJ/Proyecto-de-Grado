using System;
using System.Windows;
using Esri.ArcGISRuntime;
using Microsoft.Extensions.Configuration;

public partial class App : Application
{
    private IConfiguration? _configuration;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Combina User Secrets (local) y variables de entorno
        _configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets<App>()
            .Build();

        // Lee primero de User Secrets (clave "ArcGIS:ApiKey"), si no está, usa variable de entorno ARCGIS_API_KEY
        var apiKey = _configuration["ArcGIS:ApiKey"] ?? Environment.GetEnvironmentVariable("ARCGIS_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("API key de ArcGIS no encontrada. Configurela con __Manage User Secrets__ o como variable de entorno 'ARCGIS_API_KEY'.", "Falta API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            ArcGISRuntimeEnvironment.ApiKey = apiKey;

            // Mensaje visible en UI (evita mostrar la clave)
            System.Diagnostics.Trace.WriteLine($"ArcGIS API key presente: {!string.IsNullOrWhiteSpace(ArcGISRuntimeEnvironment.ApiKey)}");
        }

        base.OnStartup(e);
    }
}
