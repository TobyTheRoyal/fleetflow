using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Persistence;

public sealed class FleetFlowDbContext(DbContextOptions<FleetFlowDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(150);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        for (var i = 0; i < FleetRoles.All.Length; i++)
            modelBuilder.Entity<IdentityRole<Guid>>().HasData(new IdentityRole<Guid>(FleetRoles.All[i])
            { Id = FleetRoles.Id(i), NormalizedName = FleetRoles.All[i].ToUpperInvariant(), ConcurrencyStamp = FleetRoles.Id(i).ToString() });
        modelBuilder.HasSequence<long>("ShipmentNumberSequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FleetFlowDbContext).Assembly);
    }
}
