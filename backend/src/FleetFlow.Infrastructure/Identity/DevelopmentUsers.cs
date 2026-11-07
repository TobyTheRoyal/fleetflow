using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FleetFlow.Infrastructure.Identity;

public sealed class DevelopmentUsers(FleetFlowDbContext db, UserManager<ApplicationUser> users, IConfiguration configuration, TimeProvider clock)
{
    public async Task SeedAsync(string environment, CancellationToken token)
    {
        DevelopmentSeeder.RequireDevelopment(environment);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741062006)", token);
        foreach (var role in FleetRoles.All)
        {
            var email = $"{role.ToLowerInvariant()}@fleetflow.example";
            if (await users.FindByEmailAsync(email) is not null) continue; // Never reset passwords or reactivate accounts.
            var password = configuration[$"DemoUsers:{role}Password"];
            if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Configure generated Development demo passwords before seeding users.");
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, DisplayName = $"Demo {role}", CreatedAtUtc = clock.GetUtcNow() };
            if (!(await users.CreateAsync(user, password)).Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded)
                throw new InvalidOperationException("Demo user provisioning failed. Check the configured password policy.");
        }
        await transaction.CommitAsync(token);
    }
}
