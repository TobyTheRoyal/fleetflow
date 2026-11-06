using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Persistence;

public sealed class FleetFlowDbContext(DbContextOptions<FleetFlowDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("ShipmentNumberSequence");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FleetFlowDbContext).Assembly);
    }
}
