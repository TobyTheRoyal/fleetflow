using System.Collections.Concurrent;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Api.Realtime;

public sealed record LiveSession(Guid UserId, string Stamp, DateTimeOffset ExpiresAtUtc, Action Abort, string? Sandbox = null, Guid? LeaseId = null);

// Instance-local connection metadata, never tokens or customer data. Hub instances themselves are transient.
public sealed class LiveConnections(IServiceScopeFactory scopes, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, LiveSession> connections = new();
    private readonly SemaphoreSlim validation = new(1, 1);
    public void Add(string connectionId, LiveSession session) => connections[connectionId] = session;
    public void Remove(string connectionId) => connections.TryRemove(connectionId, out _);
    public int Count => connections.Count;

    public async Task<string[]> RecipientsAsync(string? sandbox, CancellationToken token)
    {
        var valid = await ValidConnectionsAsync(token);
        return valid.Where(id => connections.TryGetValue(id, out var session) && session.Sandbox == sandbox).ToArray();
    }
    public async Task<string[]> ValidConnectionsAsync(CancellationToken token)
    {
        await validation.WaitAsync(token);
        try
        {
            var snapshot = connections.ToArray();
            if (snapshot.Length == 0) return [];
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityPersistence>();
            var valid = new List<string>();
            foreach (var batch in snapshot.Chunk(100))
            {
                var ids = batch.Select(x => x.Value.UserId).Distinct().ToArray();
                var users = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive
                    && db.UserRoles.Any(ur => ur.UserId == x.Id && db.Roles.Any(r => r.Id == ur.RoleId
                        && (r.Name == "Admin" || r.Name == "Dispatcher" || r.Name == "Viewer"))))
                    .Select(x => new { x.Id, x.SecurityStamp, x.IsDemo }).ToDictionaryAsync(x => x.Id, token);
                var leases = await db.PortfolioSlots.AsNoTracking().Where(x => ids.Contains(x.UserId) && x.Ready && x.ExpiresAtUtc > clock.GetUtcNow())
                    .ToDictionaryAsync(x => x.UserId, token);
                foreach (var (id, session) in batch)
                {
                    if (session.ExpiresAtUtc > clock.GetUtcNow() && users.TryGetValue(session.UserId, out var user)
                        && string.Equals(user.SecurityStamp, session.Stamp, StringComparison.Ordinal)
                        && (!user.IsDemo || (leases.TryGetValue(user.Id, out var lease) && lease.LeaseId == session.LeaseId && lease.SchemaName == session.Sandbox))) valid.Add(id);
                    else if (connections.TryRemove(id, out var removed)) removed.Abort();
                }
            }
            return valid.ToArray();
        }
        finally { validation.Release(); }
    }
}

public sealed class LiveSessionMonitor(LiveConnections connections, TimeProvider clock, ILogger<LiveSessionMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await connections.ValidConnectionsAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("Real-time session verification is temporarily unavailable."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
