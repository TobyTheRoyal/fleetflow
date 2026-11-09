using FleetFlow.Domain.Common;
using FleetFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FleetFlow.Infrastructure.Portfolio;

// A fixed pool of identities, not anonymous registration. Lease IDs/stamps fence account reuse.
public sealed class PortfolioService(IdentityPersistence db, PortfolioSettings settings, IConfiguration configuration, TimeProvider clock)
{
    public async Task<DemoAvailability> AvailabilityAsync(CancellationToken token)
    {
        if (!settings.Enabled) return new(false, 0, 0, settings.LeaseMinutes);
        var now = clock.GetUtcNow();
        var slots = db.PortfolioSlots.AsNoTracking().Where(x => (x.Id <= settings.ViewerSlots || (x.Id > 10 && x.Id <= 10 + settings.DispatcherSlots)) && (x.ExpiresAtUtc == null || x.ExpiresAtUtc <= now)
            && db.Users.Any(user => user.Id == x.UserId && user.IsActive && user.IsDemo));
        return new(true, await slots.CountAsync(x => x.Role == "Viewer", token), await slots.CountAsync(x => x.Role == "Dispatcher", token), settings.LeaseMinutes);
    }
    public async Task<(ApplicationUser User, PortfolioSlot Slot)> StartAsync(string mode, CancellationToken token)
    {
        if (!settings.Enabled) throw FleetFlowException.NotFound("demo");
        if (mode is not ("Viewer" or "Dispatcher")) throw new FleetFlowException("invalid_demo_mode", "Choose Viewer or Dispatcher.");
        var instant = clock.GetUtcNow();
        var now = new DateTimeOffset(instant.UtcTicks - instant.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        PortfolioSlot slot;
        ApplicationUser user;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741070000)", token);
            slot = await db.PortfolioSlots.Where(x => x.Role == mode && (x.Id <= settings.ViewerSlots || (x.Id > 10 && x.Id <= 10 + settings.DispatcherSlots)) && (x.ExpiresAtUtc == null || x.ExpiresAtUtc <= now)
                && db.Users.Any(user => user.Id == x.UserId && user.IsActive && user.IsDemo))
                .OrderBy(x => x.ExpiresAtUtc != null).ThenBy(x => x.ExpiresAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(token)
                ?? throw FleetFlowException.Conflict("demo_capacity", "All demo places for this mode are in use. Try again later or choose the other mode.");
            user = await db.Users.SingleAsync(x => x.Id == slot.UserId && x.IsDemo && x.IsActive, token);
            user.SecurityStamp = Guid.NewGuid().ToString();
            slot.LeaseId = Guid.NewGuid(); slot.ExpiresAtUtc = now.AddMinutes(settings.LeaseMinutes);
            slot.Ready = mode == "Viewer"; slot.Writes = 0;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        if (mode == "Dispatcher")
        {
            try
            {
                var connectionString = configuration.GetConnectionString("PortfolioMaintenance")
                    ?? throw new InvalidOperationException("Portfolio maintenance connection is required.");
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(token);
                await using var reset = new NpgsqlCommand("SELECT public.portfolio_prepare_slot(@id, @lease, @now)", connection) { CommandTimeout = 20 };
                reset.Parameters.AddWithValue("id", slot.Id); reset.Parameters.AddWithValue("lease", slot.LeaseId!.Value); reset.Parameters.AddWithValue("now", now);
                await reset.ExecuteNonQueryAsync(token);
                slot.Ready = true;
                // The definer function updates Ready; leave no stale tracked metadata.
                db.Entry(slot).State = EntityState.Detached;
            }
            catch (Exception)
            {
                await db.PortfolioSlots.Where(x => x.Id == slot.Id && x.LeaseId == slot.LeaseId)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAtUtc, now).SetProperty(s => s.Ready, false), CancellationToken.None);
                throw new FleetFlowException("demo_unavailable", "The demo could not be prepared. No session was issued.", FailureKind.Conflict);
            }
        }
        return (user, slot);
    }
    public async Task RevokeAsync(Guid lease, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await db.PortfolioSlots.Where(s => s.LeaseId == lease).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, now).SetProperty(x => x.Ready, false), token);
    }
}

// At most four DB-backed demo requests can hold a reset fence at once: no pool starvation.
public sealed class PortfolioRequestLimiter
{
    public SemaphoreSlim Requests { get; } = new(4, 4);
}
