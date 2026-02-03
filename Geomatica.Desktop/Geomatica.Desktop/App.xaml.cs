using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Windows;
using Esri.ArcGISRuntime;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.ViewModels;
using Npgsql;
using System.Diagnostics;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load configuration from environment variables and user secrets
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddUserSecrets<App>()
                .Build();

            // Initialize ArcGIS Runtime with API Key from configuration
            var apiKey = config["ArcGIS:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;
            }

            // Configure DI
            var services = new ServiceCollection();

            // Repositories: prefer a full connection string from config, otherwise build one
            var cs = config["GEOMATICA_CONNECTION"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cs))
            {
                var host = config["GEOMATICA_DB_HOST"];
                var portParsed = int.TryParse(config["GEOMATICA_DB_PORT"], out var p);
                var port = p;
                var db = config["GEOMATICA_DB_NAME"];
                var user = config["GEOMATICA_DB_USER"];
                var pass = config["GEOMATICA_DB_PASS"];

                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Database = db,
                    Username = user,
                    Password = pass,
                    Pooling = true
                };
                cs = builder.ConnectionString;
            }

            // Test DB connection early to provide clear feedback
            var dbOk = false;
            try
            {
                using var testCon = new NpgsqlConnection(cs);
                testCon.Open();
                testCon.Close();
                Debug.WriteLine("[App] Conexión a DB OK.");
                dbOk = true;
            }
            catch (Exception ex)
            {
                var builderCheck = new NpgsqlConnectionStringBuilder(cs);
                var msg = $"No se pudo conectar a la base de datos.\n\nTarget: Host={builderCheck.Host}:{builderCheck.Port}, Database={builderCheck.Database} (revisa user secrets o variables GEOMATICA_CONNECTION / GEOMATICA_DB_* )\n\nError: {ex.Message}\n\nLa aplicación continuará, pero algunas funcionalidades podrán fallar.";
                MessageBox.Show(msg, "Error conexión Postgres", MessageBoxButton.OK, MessageBoxImage.Warning);
                Debug.WriteLine($"[App] Error conexión Postgres: {ex}");
            }

            // If DB connected, verify required tables exist in schema 'geovisor'
            if (dbOk)
            {
                try
                {
                    using var con = new NpgsqlConnection(cs);
                    con.Open();

                    var requiredTables = new[] { "proyecto", "municipio" };
                    var missing = new List<string>();

                    foreach (var tbl in requiredTables)
                    {
                        using var cmd = new NpgsqlCommand(
                            "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = 'geovisor' AND table_name = @t);",
                            con);
                        cmd.Parameters.AddWithValue("@t", tbl);
                        var exists = (cmd.ExecuteScalar() as bool?) == true;
                        if (!exists) missing.Add(tbl);
                    }

                    if (missing.Count >0)
                    {
                        var msg = $"La base de datos existe pero faltan tablas en el esquema 'geovisor': {string.Join(", ", missing)}.\n\nVerifica que la migración/creación de tablas se haya ejecutado.";
                        MessageBox.Show(msg, "Tablas faltantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Debug.WriteLine($"[App] Tablas faltantes en geovisor: {string.Join(",", missing)}");
                    }

                    // New: count projects and show quick verification
                    try
                    {
                        using var cntCmd = new NpgsqlCommand("SELECT COUNT(*) FROM geovisor.proyecto;", con);
                        var cntObj = cntCmd.ExecuteScalar();
                        var count = cntObj == null || cntObj == DBNull.Value ?0L : Convert.ToInt64(cntObj);
                        Debug.WriteLine($"[App] Proyectos en geovisor.proyecto: {count}");
                        MessageBox.Show($"Proyectos en geovisor.proyecto: {count}", "Proyectos encontrados", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[App] Error contando proyectos: {ex}");
                    }

                    con.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error verificando tablas: {ex}");
                }
            }

            services.AddSingleton<IProyectoRepository>(sp => new ProyectoRepository(cs));
            services.AddSingleton<IMunicipioRepository>(sp => new MunicipioRepository(cs));

            // ViewModels
            services.AddSingleton<FiltrosViewModel>();
            // Keep a single MapaViewModel so its Map and layers are reused and not re-created on each view switch
            services.AddSingleton<MapaViewModel>(sp => new MapaViewModel(
                sp.GetRequiredService<IProyectoRepository>(),
                sp.GetRequiredService<IMunicipioRepository>(),
                sp.GetRequiredService<FiltrosViewModel>()));
            services.AddTransient<ArchivosViewModel>(sp => new ArchivosViewModel(sp.GetRequiredService<FiltrosViewModel>()));

            services.AddSingleton<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                () => sp.GetRequiredService<MapaViewModel>(),
                () => sp.GetRequiredService<ArchivosViewModel>()));

            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
            Application.Current.Properties["ServiceProvider"] = _serviceProvider;

            var main = _serviceProvider.GetRequiredService<MainWindow>();
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            main.DataContext = mainVm;

            // Show the initial map view at startup by executing the generated command
            try
            {
                mainVm.ShowMapaCommand.Execute(null);
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error inicializando vista de mapa: {ex}");
            }

            main.Show();
        }
    }
}