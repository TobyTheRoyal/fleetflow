using FleetFlow.Application.Realtime;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FleetFlow.Infrastructure.Persistence;

internal sealed class EfTransaction(FleetFlowDbContext db, ILogisticsNotifier notifier, TimeProvider clock, ILogger<EfTransaction> logger)
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            var notifications = Changes();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            // The commit is final. Request cancellation or transport failure must never report it as rolled back.
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await notifier.PublishAsync(notifications, timeout.Token).WaitAsync(timeout.Token);
            }
            catch (Exception)
            {
                logger.LogWarning("Real-time delivery failed after commit; clients will recover through REST.");
            }
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
        catch (Exception exception) when (IsReservationConcurrencyFailure(exception))
        {
            throw FleetFlowException.Conflict("concurrent_operation", "A competing operation changed the reservation. Please retry.");
        }
        finally
        {
            // A failed operation must not leave pending changes/events in a reused request scope.
            db.ChangeTracker.Clear();
        }
    }

    private List<LogisticsNotification> Changes()
    {
        db.ChangeTracker.DetectChanges();
        var result = new List<LogisticsNotification>();
        foreach (var entry in db.ChangeTracker.Entries<ShipmentEvent>().Where(x => x.State == EntityState.Added))
        {
            var history = entry.Entity;
            var shipment = db.ChangeTracker.Entries<Shipment>().Single(x => x.Entity.Id == history.ShipmentId).Entity;
            var type = history.EventType switch
            {
                ShipmentEventType.Created => "ShipmentCreated",
                ShipmentEventType.DetailsChanged => "ShipmentUpdated",
                ShipmentEventType.AssignmentChanged => "ShipmentAssigned",
                _ when history.NewStatus == ShipmentStatus.Cancelled => "ShipmentCancelled",
                _ => "ShipmentStatusChanged"
            };
            result.Add(new(history.Id, "shipment", shipment.Id, type, shipment.Version, history.OccurredAtUtc));
        }
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            var resource = entry.Entity switch
            {
                Driver driver => ("driver", driver.Id),
                Vehicle vehicle => ("vehicle", vehicle.Id),
                Customer customer => ("customer", customer.Id),
                _ => ((string, Guid)?)null
            };
            if (resource is { } value)
                result.Add(new(Guid.CreateVersion7(), value.Item1, value.Item2,
                    value.Item1 + (entry.State == EntityState.Added ? "Created" : "Updated"), null, clock.GetUtcNow()));
        }
        return result.Select(notification => notification with { Sandbox = db.SandboxSchema }).ToList();
    }

    private static bool IsReservationConcurrencyFailure(Exception exception)
    {
        // SaveChanges wraps provider errors in DbUpdateException and Npgsql's
        // execution strategy may add InvalidOperationException for transient errors.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

}
