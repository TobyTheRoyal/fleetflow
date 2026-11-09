namespace FleetFlow.Application.Realtime;

// Invalidation hints only. REST remains authoritative; resource records have no concurrency version.
public sealed record LogisticsNotification(Guid EventId, string EntityType, Guid EntityId, string EventType,
    long? Version, DateTimeOffset TimestampUtc)
{
    // Internal routing metadata, never part of the wire notification.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Sandbox { get; init; }
}

public interface ILogisticsNotifier
{
    Task PublishAsync(IReadOnlyList<LogisticsNotification> notifications, CancellationToken cancellationToken);
}

public sealed class NullLogisticsNotifier : ILogisticsNotifier
{
    public Task PublishAsync(IReadOnlyList<LogisticsNotification> notifications, CancellationToken cancellationToken) => Task.CompletedTask;
}
