using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Esri.ArcGISRuntime;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.ViewModels;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var apiKey = Environment.GetEnvironmentVariable("ArcGIS_ApiKey");
            Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;

            // Configure DI
            var services = new ServiceCollection();

            // Repositories: provide connection string from environment or default
            var cs = Environment.GetEnvironmentVariable("GEOMATICA_CONNECTION") ?? "";
            services.AddSingleton<IProyectoRepository>(sp => new ProyectoRepository(cs));
            services.AddSingleton<IMunicipioRepository>(sp => new MunicipioRepository(cs));

            // ViewModels
            services.AddSingleton<FiltrosViewModel>();
            services.AddTransient<MapaViewModel>(sp => new MapaViewModel(
                sp.GetRequiredService<IProyectoRepository>(),
                sp.GetRequiredService<IMunicipioRepository>(),
                sp.GetRequiredService<FiltrosViewModel>()));
            services.AddTransient<ArchivosViewModel>(sp => new ArchivosViewModel(sp.GetRequiredService<FiltrosViewModel>()));

            services.AddSingleton<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                () => sp.GetRequiredService<MapaViewModel>(),
                () => sp.GetRequiredService<ArchivosViewModel>()));

            services.AddSingleton<MainWindow>();

            var provider = services.BuildServiceProvider();

            var main = provider.GetRequiredService<MainWindow>();
            var mainVm = provider.GetRequiredService<MainViewModel>();
            main.DataContext = mainVm;

            // Show the initial map view at startup by executing the generated command
            // (this will create the MapaViewModel via the factory and set CurrentView)
            try
            {
                mainVm.ShowMapaCommand.Execute(null);
            }
            catch (System.Exception ex)
            {
                // Si hay error en la inicialización, loguear o mostrar estado mínimo
                System.Diagnostics.Debug.WriteLine($"Error inicializando vista de mapa: {ex}");
            }

            main.Show();
        }
    }
}