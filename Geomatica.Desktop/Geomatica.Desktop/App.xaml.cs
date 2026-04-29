using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Windows;
using Esri.ArcGISRuntime;
using Geomatica.Data.Repositories;
using Geomatica.Desktop.ViewModels;
using Npgsql;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load configuration from environment variables, settings JSON and user secrets
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddUserSecrets<App>()
                .Build();

            // Configure DI
            var services = new ServiceCollection();

            // Repositories: prefer a full connection string from config, otherwise build one
            var cs = config["GEOMATICA_CONNECTION"] ?? string.Empty;

            // Asegurar resiliencia en connection strings proporcionadas externamente
            try
            {
                var parsed = new NpgsqlConnectionStringBuilder(cs);
                if (parsed.KeepAlive == 0) parsed.KeepAlive = 30;
                if (parsed.Timeout == 15) parsed.Timeout = 10;
                if (parsed.ConnectionIdleLifetime == 300) parsed.ConnectionIdleLifetime = 60;
                if (parsed.ConnectionPruningInterval == 10) parsed.ConnectionPruningInterval = 15;
                cs = parsed.ConnectionString;
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"[App] Error al parsear la cadena de conexión: {ex.Message}");
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
                try
                {
                    var builderCheck = new NpgsqlConnectionStringBuilder(cs);
                    var msg = $"No se pudo conectar a la base de datos.\n\nTarget: Host={builderCheck.Host}:{builderCheck.Port}, Database={builderCheck.Database} (revisa user secrets o variables GEOMATICA_CONNECTION / GEOMATICA_DB_* )\n\nError: {ex.Message}\n\nLa aplicación continuará, pero algunas funcionalidades podrán fallar.";
                    MessageBox.Show(msg, "Error conexión Postgres", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch
                {
                    MessageBox.Show($"No se pudo conectar a la base de datos.\n\nError: {ex.Message}", "Error conexión Postgres", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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

                    if (missing.Count > 0)
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
                        var count = cntObj == null || cntObj == DBNull.Value ? 0L : Convert.ToInt64(cntObj);
                        Debug.WriteLine($"[App] Proyectos en geovisor.proyecto: {count}");
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
            services.AddSingleton<Geomatica.Desktop.Services.ProyectoArchivosService>();

            // ViewModels
            services.AddSingleton<FiltrosViewModel>();
            // Keep a single MapaViewModel so its Map and layers are reused and not re-created on each view switch
            services.AddSingleton<MapaViewModel>(sp => new MapaViewModel(
                sp.GetRequiredService<IProyectoRepository>(),
                sp.GetRequiredService<IMunicipioRepository>(),
                sp.GetRequiredService<FiltrosViewModel>(),
                sp.GetRequiredService<ArchivosViewModel>()));
            services.AddTransient<ArchivosViewModel>(sp => new ArchivosViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                sp.GetRequiredService<Geomatica.Desktop.Services.ProyectoArchivosService>()));

            // Factory for CrearProyectoViewModel with a navigation callback
            services.AddSingleton<Func<Action, Action?, CrearProyectoViewModel>>(sp => (navigateBack, onCreado) =>
                new CrearProyectoViewModel(
                    sp.GetRequiredService<IProyectoRepository>(),
                    sp.GetRequiredService<IMunicipioRepository>(),
                    sp.GetRequiredService<Geomatica.Desktop.Services.ProyectoArchivosService>(),
                    navigateBack,
                    onCreado));

            // Factory for EditarProyectoViewModel
            services.AddSingleton<Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel>>(sp => (proyecto, navigateBack, onEditado) =>
                new EditarProyectoViewModel(
                    sp.GetRequiredService<IProyectoRepository>(),
                    sp.GetRequiredService<IMunicipioRepository>(),
                    proyecto,
                    navigateBack,
                    onEditado));

            services.AddSingleton<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                () => sp.GetRequiredService<MapaViewModel>(),
                () => sp.GetRequiredService<ArchivosViewModel>(),
                sp.GetRequiredService<Func<Action, Action?, CrearProyectoViewModel>>(),
                sp.GetRequiredService<Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel>>()
                ));

            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
            Application.Current.Properties["ServiceProvider"] = _serviceProvider;

            // Precarga de cachés: departamentos y municipios se cargan en segundo plano
            // para que estén disponibles instantáneamente cuando la UI los necesite.
            var muniRepo = _serviceProvider.GetRequiredService<IMunicipioRepository>();
            _ = Task.Run(async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    await Task.WhenAll(
                        muniRepo.ListarDepartamentosAsync(),
                        muniRepo.ListarTodosMunicipiosAsync()
                    );
                    sw.Stop();
                    Debug.WriteLine($"[App] Precarga de departamentos y municipios completada en {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error en precarga de departamentos/municipios: {ex}");
                }
            });

            var main = _serviceProvider.GetRequiredService<MainWindow>();
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            main.DataContext = mainVm;

            // HACK: Autenticar el portal luego de que la ventana de la App está cargada, 
            // sino Webview2 no tendrá un owner visual y podría ocultarse en background
            main.Loaded += async (s, args) =>
            {
                // Autenticación por Usuario Nombrado - Configurar Oauth 2.0 y obtener licencia
                var clientId = config["ArcGIS:ClientId"];

                // Si tienes un portal on-premise, puedes configurarlo, de lo contrario arcgis.com
                string portalUrl = "https://www.arcgis.com/sharing/rest";

                try
                {
                    // Configuramos SIEMPRE el handler visual para interceptar challenges, haya client id o no
                    Esri.ArcGISRuntime.Security.AuthenticationManager.Current.ChallengeHandler = new Esri.ArcGISRuntime.Security.DefaultChallengeHandler();
                    Esri.ArcGISRuntime.Security.AuthenticationManager.Current.OAuthAuthorizeHandler = new Geomatica.Desktop.OAuthAuthorizeHandler();

                    if (!string.IsNullOrEmpty(clientId))
                    {
                        var redirectUrl = config["ArcGIS:RedirectUri"] ?? "my-geomatica-app://auth";

                        var userConfig = new Esri.ArcGISRuntime.Security.OAuthUserConfiguration(new Uri(portalUrl), clientId, new Uri(redirectUrl));
                        Esri.ArcGISRuntime.Security.AuthenticationManager.Current.OAuthUserConfigurations.Add(userConfig);

                        // Esri previene hacer el login directamente en OnStartup o constructores sin UI visible en versiones modernas de WPF
                        // 1. Realizar explícitamente el Challenge/Autenticación de OAuth para el usuario actual
                        var cred = await Esri.ArcGISRuntime.Security.OAuthUserCredential.CreateAsync(userConfig);

                        Esri.ArcGISRuntime.Security.AuthenticationManager.Current.AddCredential(cred);

                        var portal = await Esri.ArcGISRuntime.Portal.ArcGISPortal.CreateAsync(new Uri("https://www.arcgis.com/"), true);

                        // 3. Obtener la licencia vinculada a ese usuario y aplicarla a la app
                        var licenseInfo = await portal.GetLicenseInfoAsync();
                        Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.SetLicense(licenseInfo);
                    }
                    else
                    {
                        // Flujo directo sin OAuth Client ID, forzar ventana de login estilo "TokenSecuredChallenge" en recursos que no sean OAuth
                        // Nota: el mapa lanzará un Challenge por el mapa default topográfico, así entra el handler automáticamente
                        Debug.WriteLine("[App] Aviso: ArcGIS:ClientId no está configurado. La autenticación dependerá del mapa cargando para lanzar la ventana gráfica si requiere token.");
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[App] El usuario canceló la autenticación. Se empleará la versión Developer de ArcGIS.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Error de autenticación ArcGIS: {ex.Message}");
                }

                // Show the initial map view only AFTER authentication has been attempted/resolved, 
                // to prevent load errors where maps ask for authentication while handlers are still not ready.
                try
                {
                    mainVm.ShowMapaCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error inicializando vista de mapa: {ex}");
                }
            };

            main.Show();
        }
    }
}