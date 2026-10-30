using Geomatica.Data.Repositories;
using Geomatica.Domain.Interfaces.Repositories;
using Geomatica.ServerAgent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "GeomaticaAuditoria";
    })
    .ConfigureServices((hostContext, services) =>
    {
        var config = hostContext.Configuration;
        string cn = config.GetConnectionString("PostgresConnection")
                    ?? config["GEOMATICA_CONNECTION"]
                    ?? "Host=10.0.29.8;Port=5433;Database=postgres;Username=geovisor_app;Password=G30m@tica2026*";

        services.AddSingleton<IProyectoRepository>(new ProyectoRepository(cn));
        services.AddHostedService<FileAuditorWorker>();
    });

var host = builder.Build();
await host.RunAsync();

