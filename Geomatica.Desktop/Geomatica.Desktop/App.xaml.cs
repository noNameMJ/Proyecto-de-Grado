using Esri.ArcGISRuntime;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.ViewModels;
using Geomatica.Domain.Repositories;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System;
using System.Windows;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        public static ServiceProvider Services { get; private set; } = null!;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Services = Bootstrap.BuildServices();

            var apiKey = Environment.GetEnvironmentVariable("ArcGIS_ApiKey");
            Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;

            var main = new MainWindow();
            main.DataContext = Services.GetRequiredService<MainViewModel>();
            main.Show();
        }
    }
}