using FleetFlow.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetFlow.Infrastructure.Persistence;

internal sealed class EfTransaction(FleetFlowDbContext db)
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
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

}
