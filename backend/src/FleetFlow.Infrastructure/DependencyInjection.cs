using FleetFlow.Application.Shipments;
using FleetFlow.Infrastructure.Health;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using FleetFlow.Infrastructure.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FleetFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FleetFlowDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("FleetFlow")
                ?? throw new InvalidOperationException("Configure ConnectionStrings: FleetFlow before using the database.");
            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks().AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IShipmentStore, EfShipmentStore>();
        services.AddScoped<ShipmentService>();
        services.AddScoped<DevelopmentSeeder>();
        return services;
    }
}
