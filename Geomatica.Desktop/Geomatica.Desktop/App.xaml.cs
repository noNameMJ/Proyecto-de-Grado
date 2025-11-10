using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Windows;
using Esri.ArcGISRuntime;
using Microsoft.Extensions.Configuration;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var apiKey = Environment.GetEnvironmentVariable("ArcGIS_ApiKey");
            Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;
        }
    }
}