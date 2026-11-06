using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetFlow.Infrastructure.Persistence;

public static class DevelopmentDatabase
{
    public static async Task InitializeAsync(IServiceProvider services, string environmentName, bool migrate, bool seed, CancellationToken cancellationToken = default)
    {
        DevelopmentSeeder.RequireDevelopment(environmentName);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        if (migrate) await db.Database.MigrateAsync(cancellationToken);
        if (seed) await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(environmentName, cancellationToken);
    }
}
