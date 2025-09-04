using System.Windows;
using Esri.ArcGISRuntime;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Si tienes API key de ArcGIS, descomenta:
            // ArcGISRuntimeEnvironment.ApiKey = "TU_API_KEY";
            base.OnStartup(e);
        }
    }
}