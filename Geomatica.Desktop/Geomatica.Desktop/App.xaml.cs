using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Leer la clave API desde el archivo API_KEY.txt
            string apiKey = File.ReadAllText("API_KEY.txt").Trim();
            Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;

            // Call a function to set up the AuthenticationManager for OAuth.
            UserAuth.ArcGISLoginPrompt.SetChallengeHandler();
        }
    }
}