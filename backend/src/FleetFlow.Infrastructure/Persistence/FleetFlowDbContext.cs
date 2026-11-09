using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Insights;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FleetFlow.Infrastructure.Persistence;

public sealed class FleetFlowDbContext(DbContextOptions<FleetFlowDbContext> options, PortfolioScope? portfolio = null) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<AiAttempt> AiAttempts => Set<AiAttempt>();
    public DbSet<PortfolioSlot> PortfolioSlots => Set<PortfolioSlot>();
    public string? SandboxSchema => portfolio?.Schema;
    public string SqlTable(string table) => SandboxSchema is null ? $"\"{table}\"" : $"\"{SandboxSchema}\".\"{table}\"";
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        IdentityPersistence.ConfigureIdentity(modelBuilder);
        modelBuilder.HasSequence<long>("ShipmentNumberSequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FleetFlowDbContext).Assembly);
        if (SandboxSchema is { } schema)
            foreach (var type in new[] { typeof(Customer), typeof(Driver), typeof(Vehicle), typeof(Shipment), typeof(ShipmentEvent) })
                modelBuilder.Entity(type).ToTable(modelBuilder.Entity(type).Metadata.GetTableName()!, schema);
    }
}

public sealed class PortfolioModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is FleetFlowDbContext fleet
        ? (context.GetType(), fleet.SandboxSchema, designTime) : (object)(context.GetType(), designTime);
}
