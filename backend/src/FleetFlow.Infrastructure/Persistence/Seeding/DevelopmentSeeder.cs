using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Persistence.Seeding;

public sealed class DevelopmentSeeder(FleetFlowDbContext db, TimeProvider clock)
{
    public async Task SeedAsync(string environmentName, CancellationToken cancellationToken = default)
    {
        RequireDevelopment(environmentName);
        await SeedCoreAsync(cancellationToken);
    }
    internal Task SeedPortfolioAsync(CancellationToken cancellationToken)
    {
        if (db.SandboxSchema != "portfolio_template") throw new InvalidOperationException("Only the explicit demo template may be initialized.");
        return SeedCoreAsync(cancellationToken);
    }
    private async Task SeedCoreAsync(CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        // Serialize concurrent seed commands without blocking normal shipment operations.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741062001)", cancellationToken);
        if (await db.Shipments.AnyAsync(x => x.Id == SeedId(4, 1), cancellationToken)) return;
        if (await db.Customers.AnyAsync(cancellationToken) || await db.Drivers.AnyAsync(cancellationToken)
            || await db.Vehicles.AnyAsync(cancellationToken) || await db.Shipments.AnyAsync(cancellationToken))
            throw FleetFlowException.Conflict("seed_database_not_empty", "Sample data requires an empty database. Existing data has not been changed.");

        var now = clock.GetUtcNow();
        var companyNames = new[] { "Alpenstern", "Donaubogen", "Murfeld", "Nordhafen", "Talblick", "Bergkontor", "Stadtlager", "Wiesenwerk", "Sonnenkai", "Westbruecke" };
        var customers = Enumerable.Range(1, 10).Select(i => Customer.Create($"{companyNames[i - 1]} Demo Handels GmbH",
            $"Demo Contact {i:00}", $"contact{i:00}@fleetflow.example", null, now.AddDays(-30), SeedId(1, i))).ToArray();
        var drivers = Enumerable.Range(1, 12).Select(i => Driver.Create($"DEMO-{i:000}", "Demo", $"Driver{i:00}",
            i <= 10, i <= 4 ? "B" : "C", now.AddDays(-30), SeedId(2, i))).ToArray();
        decimal[] capacities = [3500m, 3500m, 12000m, 18000m, 24000m, 12000m, 3500m, 18000m];
        var vehicles = Enumerable.Range(1, 8).Select(i => Vehicle.Create($"FF-DEMO{i:00}", i is 1 or 2 or 7 ? VehicleType.Van : VehicleType.Truck,
            capacities[i - 1], i == 7 ? VehicleStatus.Maintenance : i == 8 ? VehicleStatus.Inactive : VehicleStatus.Available,
            now.AddDays(-30), SeedId(3, i))).ToArray();
        db.AddRange(customers);
        db.AddRange(drivers);
        db.AddRange(vehicles);

        for (var i = 0; i < 36; i++)
        {
            var pickup = i < 6 ? now.AddHours(-1)
                : i < 12 ? now.AddHours(-12)
                : i < 18 ? now.AddHours(-72)
                : now.AddDays(-7);
            var deadline = i < 6 ? now.AddHours(2 + i)
                : i < 9 ? now.AddHours(-2)
                : i < 12 ? now.AddHours(6 + i - 9)
                : i < 18 ? now.AddHours(-48)
                : now.AddDays(-6);
            var details = new ShipmentDetails(customers[i % customers.Length].Id,
                Address.Create($"Demo Depotstrasse {i + 1}", i % 2 == 0 ? "Graz" : "Linz", i % 2 == 0 ? "8010" : "4020", "AT"),
                Address.Create($"Demo Lagerweg {i + 1}", i % 2 == 0 ? "Vienna" : "Salzburg", i % 2 == 0 ? "1010" : "5020", "AT"),
                pickup, deadline, 500m + i * 20m, 1 + i % 6, "Fictional demo pallet shipment");
            var sequence = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"ShipmentNumberSequence\"') AS \"Value\"").SingleAsync(cancellationToken);
            var shipment = Shipment.Create($"FF-{now.Year}-{sequence:D6}", details, pickup.AddDays(-2), "development-seed", SeedId(4, i + 1));
            if (i is >= 6 and < 30)
            {
                var driver = drivers[i % 6];
                var vehicle = vehicles[i % 6];
                shipment.Assign(driver, vehicle, pickup.AddHours(-4), "development-seed");
                shipment.ChangeStatus(ShipmentStatus.Planned, driver, vehicle, pickup.AddHours(-3), "development-seed");
                if (i >= 12)
                    shipment.ChangeStatus(ShipmentStatus.InTransit, driver, vehicle, pickup.AddMinutes(10), "development-seed");
                if (i >= 18)
                    shipment.ChangeStatus(ShipmentStatus.Delivered, null, null, deadline.AddMinutes(-20), "development-seed");
            }
            else if (i >= 30)
                shipment.ChangeStatus(ShipmentStatus.Cancelled, null, null, pickup.AddHours(-3), "development-seed", "Fictional customer cancellation");
            db.Shipments.Add(shipment);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public static void RequireDevelopment(string environmentName)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.Ordinal))
            throw new InvalidOperationException("Database initialization and fictional seeding are allowed only in Development.");
    }

    private static Guid SeedId(int group, int index) => Guid.Parse($"0000000{group}-0000-4000-8000-{index:D12}");
}
