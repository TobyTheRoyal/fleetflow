namespace FleetFlow.Infrastructure.Insights;

// Technical usage ledger, separate from shipment entities/history. Attempts are never refunded.
public sealed class AiAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly DayUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Fingerprint { get; set; } = null!;
    public string Model { get; set; } = null!;
    public string? SelectionJson { get; set; }
    public DateTimeOffset? CacheExpiresAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? RetryAfterUtc { get; set; }
}
