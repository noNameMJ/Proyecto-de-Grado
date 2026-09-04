using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Tasks.Offline;
using Geomatica.Data.Repositories;
using Geomatica.AppCore.UseCases;
using Geomatica.Domain.Entities;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.Desktop.ViewModels;
using Geomatica.Desktop.Services;
using Npgsql;

namespace Geomatica.Desktop
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppLogger.Info("Inicializando componentes y configuración de la aplicación...");

            // Manejadores globales de excepciones para logging estructurado y resiliencia
            DispatcherUnhandledException += (s, args) =>
            {
                AppLogger.Error("Excepción no controlada en hilo de UI (Dispatcher)", args.Exception);
                args.Handled = true;
                try
                {
                    var notifications = _serviceProvider?.GetService<INotificationService>();
                    notifications?.ShowError($"Ocurrió un error en la aplicación: {args.Exception.Message}", "Error Inesperado");
                }
                catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLogger.Error("Excepción fatal no controlada en AppDomain", ex);
                }
                else
                {
                    AppLogger.Error($"Excepción no controlada en AppDomain: {args.ExceptionObject}");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                AppLogger.Error("Excepción no observada en tarea en segundo plano (TaskScheduler)", args.Exception);
                args.SetObserved();
            };

            // Load configuration from environment variables, settings JSON and user secrets
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .AddUserSecrets<App>()
                .Build();

            // Configurar API Key de ArcGIS tempranamente
            var apiKey = config["ArcGIS:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;
                AppLogger.Info("API Key de ArcGIS configurada.");
            }

            // Permite pre-generar o actualizar el paquete oficial Esri Topográfico para Colombia
            if (e.Args.Length > 0 && e.Args[0] == "--export-topographic")
            {
                try
                {
                    AppLogger.Info("Iniciando tarea de exportación oficial Esri Topográfico...");
                    var portal = ArcGISPortal.CreateAsync().GetAwaiter().GetResult();
                    var item = PortalItem.CreateAsync(portal, "df541726b3df4c0caf99255bb1be4c86").GetAwaiter().GetResult();
                    var task = ExportVectorTilesTask.CreateAsync(item).GetAwaiter().GetResult();
                    var wgs84Extent = new Envelope(-88.0, -8.0, -60.0, 17.0, SpatialReferences.Wgs84);
                    var aoi = (Envelope)GeometryEngine.Project(wgs84Extent, SpatialReferences.WebMercator);
                    var parameters = task.CreateDefaultExportVectorTilesParametersAsync(aoi, 577790).GetAwaiter().GetResult();

                    var appDir = AppContext.BaseDirectory;
                    var repoAssetsDir = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "Assets", "Basemaps"));
                    if (!Directory.Exists(repoAssetsDir)) Directory.CreateDirectory(repoAssetsDir);

                    var vtpkPath = Path.Combine(repoAssetsDir, "topographic_offline.vtpk");
                    var stylePath = Path.Combine(repoAssetsDir, "topographic_style");
                    var hillshadePath = Path.Combine(repoAssetsDir, "topographic_hillshade.tpkx");

                    if (!File.Exists(vtpkPath) || new FileInfo(vtpkPath).Length < 80_000_000)
                    {
                        if (Directory.Exists(stylePath)) Directory.Delete(stylePath, true);
                        if (File.Exists(vtpkPath)) File.Delete(vtpkPath);

                        AppLogger.Info($"Exportando paquete vectorial Esri a: {vtpkPath}");
                        var job = task.ExportVectorTiles(parameters, vtpkPath, stylePath);
                        job.ProgressChanged += (s, ev) =>
                        {
                            AppLogger.Info($"Progreso exportación vectorial: {job.Progress}%");
                        };
                        job.Start();
                        var res = job.GetResultAsync().GetAwaiter().GetResult();
                        AppLogger.Info($"Exportación vectorial completada exitosamente en: {vtpkPath}");
                    }
                    else
                    {
                        AppLogger.Info($"Paquete vectorial regional ya existe ({new FileInfo(vtpkPath).Length / (1024.0 * 1024.0):F1} MB), omitiendo re-exportación vectorial.");
                    }

                    // Exportar también la capa de sombreado de relieve (Hillshade) oficial de Esri
                    try
                    {
                        AppLogger.Info("Iniciando exportación de capa de sombreado de relieve (Hillshade) desde PortalItem babedc22ebd64a428b77f7119c2591c3...");
                        var hillItem = PortalItem.CreateAsync(portal, "babedc22ebd64a428b77f7119c2591c3").GetAwaiter().GetResult();
                        AppLogger.Info($"PortalItem World Hillshade cargado. URL: {hillItem.Url}");
                        var hillTask = ExportTileCacheTask.CreateAsync(hillItem.Url!).GetAwaiter().GetResult();
                        var hillParams = hillTask.CreateDefaultExportTileCacheParametersAsync(aoi, 0, 577790).GetAwaiter().GetResult();
                        if (File.Exists(hillshadePath)) File.Delete(hillshadePath);

                        var hillJob = hillTask.ExportTileCache(hillParams, hillshadePath);
                        hillJob.ProgressChanged += (s, ev) =>
                        {
                            AppLogger.Info($"Progreso exportación Hillshade: {hillJob.Progress}%");
                        };
                        hillJob.Start();
                        var hillRes = hillJob.GetResultAsync().GetAwaiter().GetResult();
                        AppLogger.Info($"Exportación Hillshade completada exitosamente en: {hillshadePath}");
                    }
                    catch (Exception exHill)
                    {
                        AppLogger.Warn($"No se pudo exportar Hillshade complementario: {exHill}");
                    }

                    // Copiar también al directorio bin Assets y AppData
                    var binAssets = Path.Combine(appDir, "Assets", "Basemaps");
                    if (Directory.Exists(binAssets))
                    {
                        if (File.Exists(vtpkPath)) File.Copy(vtpkPath, Path.Combine(binAssets, "topographic_offline.vtpk"), true);
                        if (File.Exists(hillshadePath)) File.Copy(hillshadePath, Path.Combine(binAssets, "topographic_hillshade.tpkx"), true);
                    }

                    var appDataStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Geomatica", "Basemaps");
                    if (Directory.Exists(appDataStorage))
                    {
                        if (File.Exists(hillshadePath)) File.Copy(hillshadePath, Path.Combine(appDataStorage, "topographic_hillshade.tpkx"), true);
                    }

                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error en exportación oficial Esri", ex);
                    Environment.Exit(1);
                }
                return;
            }

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
                AppLogger.Warn($"Error al parsear la cadena de conexión: {ex.Message}");
            }

            // Test DB connection early to provide clear feedback
            var dbOk = false;
            try
            {
                using var testCon = new NpgsqlConnection(cs);
                testCon.Open();
                testCon.Close();
                AppLogger.Info("Conexión a Base de Datos PostgreSQL exitosa.");
                dbOk = true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("No se pudo conectar a la base de datos PostgreSQL", ex);
                try
                {
                    var builderCheck = new NpgsqlConnectionStringBuilder(cs);
                    var msg = $"No se pudo conectar a la base de datos.\n\nTarget: Host={builderCheck.Host}:{builderCheck.Port}, Database={builderCheck.Database}\n\nError: {ex.Message}\n\nLa aplicación continuará, pero algunas funcionalidades podrán fallar.";
                    MessageBox.Show(msg, "Error conexión Postgres", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch
                {
                    MessageBox.Show($"No se pudo conectar a la base de datos.\n\nError: {ex.Message}", "Error conexión Postgres", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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
                        AppLogger.Warn($"Tablas faltantes en geovisor: {string.Join(", ", missing)}");
                        MessageBox.Show(msg, "Tablas faltantes", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    try
                    {
                        using var cntCmd = new NpgsqlCommand("SELECT COUNT(*) FROM geovisor.proyecto;", con);
                        var cntObj = cntCmd.ExecuteScalar();
                        var count = cntObj == null || cntObj == DBNull.Value ? 0L : Convert.ToInt64(cntObj);
                        AppLogger.Info($"Proyectos registrados en geovisor.proyecto: {count}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Error contando proyectos: {ex.Message}");
                    }

                    con.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error verificando tablas", ex);
                }
            }

            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IProyectoRepository>(sp => new ProyectoRepository(cs));
            services.AddSingleton<IMunicipioRepository>(sp => new MunicipioRepository(cs));
            services.AddSingleton<BuscarProyectosUseCase>();
            services.AddSingleton<Geomatica.Desktop.Services.ProyectoArchivosService>();

            // ViewModels
            services.AddSingleton<FiltrosViewModel>();
            services.AddSingleton<MapaViewModel>(sp => new MapaViewModel(
                sp.GetRequiredService<BuscarProyectosUseCase>(),
                sp.GetRequiredService<IProyectoRepository>(),
                sp.GetRequiredService<IMunicipioRepository>(),
                sp.GetRequiredService<FiltrosViewModel>(),
                sp.GetRequiredService<ArchivosViewModel>(),
                sp.GetRequiredService<INotificationService>()));

            services.AddTransient<ArchivosViewModel>(sp => new ArchivosViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                sp.GetRequiredService<Geomatica.Desktop.Services.ProyectoArchivosService>(),
                sp.GetRequiredService<IProyectoRepository>(),
                sp.GetRequiredService<INotificationService>()));

            services.AddSingleton<Func<Action, Action?, CrearProyectoViewModel>>(sp => (navigateBack, onCreado) =>
                new CrearProyectoViewModel(
                    sp.GetRequiredService<IProyectoRepository>(),
                    sp.GetRequiredService<IMunicipioRepository>(),
                    sp.GetRequiredService<Geomatica.Desktop.Services.ProyectoArchivosService>(),
                    navigateBack,
                    onCreado,
                    sp.GetRequiredService<INotificationService>()));

            services.AddSingleton<Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel>>(sp => (proyecto, navigateBack, onEditado) =>
                new EditarProyectoViewModel(
                    sp.GetRequiredService<IProyectoRepository>(),
                    sp.GetRequiredService<IMunicipioRepository>(),
                    proyecto,
                    navigateBack,
                    onEditado,
                    sp.GetRequiredService<INotificationService>()));

            services.AddSingleton<MainViewModel>(sp => new MainViewModel(
                sp.GetRequiredService<FiltrosViewModel>(),
                sp.GetRequiredService<INotificationService>(),
                () => sp.GetRequiredService<MapaViewModel>(),
                () => sp.GetRequiredService<ArchivosViewModel>(),
                sp.GetRequiredService<Func<Action, Action?, CrearProyectoViewModel>>(),
                sp.GetRequiredService<Func<ProyectoDetalleDto, Action, Action?, EditarProyectoViewModel>>()
                ));

            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();
            Application.Current.Properties["ServiceProvider"] = _serviceProvider;

            // Precarga de cachés: departamentos y municipios se cargan en segundo plano
            var muniRepo = _serviceProvider.GetRequiredService<IMunicipioRepository>();
            _ = Task.Run(async () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    AppLogger.Info("Iniciando precarga en segundo plano de departamentos y municipios...");
                    await Task.WhenAll(
                        muniRepo.ListarDepartamentosAsync(),
                        muniRepo.ListarTodosMunicipiosAsync()
                    );
                    sw.Stop();
                    AppLogger.Info($"Precarga de departamentos y municipios completada exitosamente en {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error en precarga de departamentos/municipios", ex);
                }
            });

            var main = _serviceProvider.GetRequiredService<MainWindow>();
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            main.DataContext = mainVm;

            main.Loaded += (s, args) =>
            {
                var apiKey = config["ArcGIS:ApiKey"];

                try
                {
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = apiKey;
                        AppLogger.Info("API Key de ArcGIS configurada.");
                    }
                    else
                    {
                        AppLogger.Warn("Aviso: ArcGIS:ApiKey no está configurada.");
                    }

                    Esri.ArcGISRuntime.Security.AuthenticationManager.Current.ChallengeHandler = new Esri.ArcGISRuntime.Security.DefaultChallengeHandler();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error al configurar API Key de ArcGIS", ex);
                }

                try
                {
                    mainVm.ShowMapaCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Error al ejecutar ShowMapaCommand inicial", ex);
                }
            };

            main.Show();
        }
    }
}
