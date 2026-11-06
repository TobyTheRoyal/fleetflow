using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetFlow.Infrastructure.Shipments;

internal sealed class EfShipmentStore(FleetFlowDbContext db) : IShipmentStore
{
    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw FleetFlowException.Conflict("stale_version", "This shipment has changed. Reload it before trying again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw FleetFlowException.Conflict("duplicate_value", "A record with this unique value already exists.");
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
        {
            throw FleetFlowException.Conflict("concurrent_operation", "A competing operation changed the reservation. Please retry.");
        }
        finally
        {
            // A failed operation must not leave pending changes/events in a reused request scope.
            db.ChangeTracker.Clear();
        }
    }

    public async Task<Shipment> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Shipments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw FleetFlowException.NotFound("shipment");

    public async Task RequireCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == id, cancellationToken)) throw FleetFlowException.NotFound("customer");
    }

    public async Task<(Driver Driver, Vehicle Vehicle)> LockResourcesAsync(Guid driverId, Guid vehicleId, CancellationToken cancellationToken)
    {
        // Always acquire Driver before Vehicle. ReadCommitted rechecks overlap after waiting for these locks.
        var driver = await db.Drivers.FromSqlInterpolated($"SELECT * FROM \"Drivers\" WHERE \"Id\" = {driverId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw FleetFlowException.NotFound("driver");
        var vehicle = await db.Vehicles.FromSqlInterpolated($"SELECT * FROM \"Vehicles\" WHERE \"Id\" = {vehicleId} FOR UPDATE")
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
