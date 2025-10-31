using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Geomatica.Domain.Repositories;
using Geomatica.Data.Repositories;
using Geomatica.AppCore.UseCases;
using Geomatica.Desktop.ViewModels;

namespace Geomatica.Desktop;

public static class Bootstrap
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var connStr = "Host=localhost;Port=5432;Database=geodata;Username=postgres;Password=postgres;Pooling=true;MaxPoolSize=20";
        services.AddSingleton(NpgsqlDataSource.Create(connStr));

        services.AddScoped<IProyectoRepository, ProyectoRepository>();
        services.AddScoped<IAdministrativoRepository, AdministrativoRepository>();

        services.AddScoped<BuscarProyectosUseCase>();
        services.AddScoped<CargarAdministrativosUseCase>();

        services.AddScoped<MainViewModel>();
        services.AddScoped<MapaViewModel>();
        services.AddScoped<ArchivosViewModel>();
        services.AddScoped<FiltrosViewModel>();

        return services.BuildServiceProvider();
    }
}