using System;
using System.IO;
using System.Windows;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Leer la clave API desde el archivo API_KEY.txt si existe (opcional para funcionalidades online).
            string apiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "API_KEY.txt");
            if (File.Exists(apiPath))
            {
                string apiKey = File.ReadAllText(apiPath).Trim();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;
                }
            }

            // Si quieres modo offline completo, no inicialices el handler OAuth.
            // UserAuth.ArcGISLoginPrompt.SetChallengeHandler();
        }
    }
}