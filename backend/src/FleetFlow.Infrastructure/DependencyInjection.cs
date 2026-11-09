using FleetFlow.Application.Fleet;
using FleetFlow.Application.Insights;
using FleetFlow.Application.Queries;
using FleetFlow.Application.Realtime;
using FleetFlow.Application.Shipments;
using FleetFlow.Infrastructure.Fleet;
using FleetFlow.Infrastructure.Health;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Insights;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using FleetFlow.Infrastructure.Portfolio;
using FleetFlow.Infrastructure.Queries;
using FleetFlow.Infrastructure.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FleetFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(PortfolioSettings.Read(configuration));
        services.AddScoped<PortfolioScope>();
        services.AddScoped<PortfolioService>();
        services.AddScoped<PortfolioInitializer>();
        services.AddSingleton<PortfolioRequestLimiter>();
        services.AddDbContext<IdentityPersistence>(options => options.UseNpgsql(configuration.GetConnectionString("FleetFlow")));
        services.AddDbContext<FleetFlowDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("FleetFlow")
                ?? throw new InvalidOperationException("Configure ConnectionStrings: FleetFlow before using the database.");
            options.UseNpgsql(connectionString).ReplaceService<IModelCacheKeyFactory, PortfolioModelCacheKey>();
        });

        services.AddHealthChecks().AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(AiConfiguration.Read(configuration));
        services.AddScoped<IInsightQueries, EfInsightQueries>();
        services.AddScoped<IAiUsageStore, EfAiUsageStore>();
        services.AddScoped<AiAnalysisService>();
        services.AddHttpClient<IAiExplanationProvider, CloudflareExplanationProvider>(http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RemoveAllLoggers();
        services.TryAddSingleton<ILogisticsNotifier, NullLogisticsNotifier>();
        services.AddScoped<IShipmentStore, EfShipmentStore>();
        services.AddScoped<ShipmentService>();
        services.AddScoped<EfTransaction>();
        services.AddScoped<IFleetStore, EfFleetStore>();
        services.AddScoped<FleetService>();
        services.AddScoped<ILogisticsQueries, EfLogisticsQueries>();
        services.AddScoped<DevelopmentSeeder>();
        services.AddScoped<UserAdministration>();
        return services;
    }
}
