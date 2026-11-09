using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetFlow.Infrastructure.Shipments;

internal sealed class EfShipmentStore(FleetFlowDbContext db, EfTransaction transaction) : IShipmentStore
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => transaction.RunAsync(action, cancellationToken);

    public async Task<Shipment> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Shipments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw FleetFlowException.NotFound("shipment");

    public async Task RequireCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == id, cancellationToken)) throw FleetFlowException.NotFound("customer");
    }

    public async Task<(Driver Driver, Vehicle Vehicle)> LockResourcesAsync(Guid driverId, Guid vehicleId, CancellationToken cancellationToken)
    {
        // Always acquire Driver before Vehicle. ReadCommitted rechecks overlap after waiting for these locks.
        var driver = await db.Drivers.FromSqlRaw(string.Concat("SELECT * FROM ", db.SqlTable("Drivers"), " WHERE \"Id\" = {0} FOR UPDATE"), driverId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw FleetFlowException.NotFound("driver");
        var vehicle = await db.Vehicles.FromSqlRaw(string.Concat("SELECT * FROM ", db.SqlTable("Vehicles"), " WHERE \"Id\" = {0} FOR UPDATE"), vehicleId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw FleetFlowException.NotFound("vehicle");
        return (driver, vehicle);
    }

    public async Task EnsureNoOverlapAsync(Shipment shipment, Guid driverId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var conflict = await db.Shipments.AsNoTracking().AnyAsync(other => other.Id != shipment.Id
            && (other.Status == ShipmentStatus.Planned || other.Status == ShipmentStatus.InTransit)
            && (other.AssignedDriverId == driverId || other.AssignedVehicleId == vehicleId)
            && other.PickupWindowStartUtc < shipment.DeliveryDeadlineUtc
            && shipment.PickupWindowStartUtc < other.DeliveryDeadlineUtc, cancellationToken);
        if (conflict) throw FleetFlowException.Conflict("assignment_overlap", "The driver or vehicle is reserved for an overlapping active shipment.");
    }

    public async Task<string> NextNumberAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var number = await db.Database.SqlQueryRaw<long>("SELECT nextval('\"ShipmentNumberSequence\"') AS \"Value\"").SingleAsync(cancellationToken);
        return $"FF-{now.UtcDateTime.Year}-{number:D6}";
    }

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);
}
