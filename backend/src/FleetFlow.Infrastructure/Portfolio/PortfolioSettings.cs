using Microsoft.Extensions.Configuration;

namespace FleetFlow.Infrastructure.Portfolio;

public sealed class PortfolioSettings
{
    public bool Enabled { get; init; }
    public int ViewerSlots { get; init; } = 10;
    public int DispatcherSlots { get; init; } = 3;
    public int LeaseMinutes { get; init; } = 30;
    public int MaxWrites { get; init; } = 100;
    public int EntryPerMinute { get; init; } = 6;
    public static PortfolioSettings Read(IConfiguration configuration)
    {
        var settings = configuration.GetSection("Portfolio").Get<PortfolioSettings>() ?? new();
        if (settings.ViewerSlots is < 1 or > 10 || settings.DispatcherSlots is < 1 or > 3 || settings.LeaseMinutes is < 1 or > 30 || settings.MaxWrites is < 1 or > 100 || settings.EntryPerMinute is < 1 or > 30)
            throw new InvalidOperationException("Portfolio limits exceed the supported bounds.");
        return settings;
    }
}

// Scoped server state. Never bound from an HTTP request body or a sandbox header.
public sealed class PortfolioScope(PortfolioSettings settings)
{
    public string? Schema { get; private set; } = settings.Enabled ? "portfolio_denied" : null;
    public int? SlotId { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public string? Role { get; private set; }
    public void Bind(PortfolioSlot slot)
    {
        var expected = slot.Role == "Viewer" && slot.Id is >= 1 and <= 10 ? "portfolio_viewer"
            : slot.Role == "Dispatcher" && slot.Id is >= 11 and <= 13 ? $"portfolio_dispatch_{slot.Id - 10}" : throw new InvalidOperationException("Invalid demo slot.");
        if (slot.SchemaName != expected) throw new InvalidOperationException("Invalid demo schema mapping.");
        Schema = expected; SlotId = slot.Id; LeaseId = slot.LeaseId;
        ExpiresAtUtc = slot.ExpiresAtUtc; Role = slot.Role;
    }
    public static PortfolioScope ForSchema(string schema)
    {
        if (schema != "portfolio_template" && schema != "portfolio_viewer" && !System.Text.RegularExpressions.Regex.IsMatch(schema, "^portfolio_dispatch_[1-3]$"))
            throw new InvalidOperationException("Unknown portfolio schema.");
        return new(new PortfolioSettings()) { Schema = schema };
    }
}
public sealed class PortfolioSlot
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool Ready { get; set; }
    public int Writes { get; set; }
}
public sealed record DemoSession(Guid LeaseId, string Mode, DateTimeOffset ExpiresAtUtc);
public sealed record DemoAvailability(bool Enabled, int ViewerAvailable, int DispatcherAvailable, int LeaseMinutes);
