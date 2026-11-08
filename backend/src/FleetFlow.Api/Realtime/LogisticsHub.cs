using System.Security.Claims;
using FleetFlow.Api.Security;
using FleetFlow.Application.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace FleetFlow.Api.Realtime;

public interface ILogisticsClient
{
    Task LogisticsChanged(LogisticsNotification notification);
}

[Authorize(Policy = SessionSecurity.Read)]
public sealed class LogisticsHub(LiveConnections connections, IOptions<IdentityOptions> identity) : Hub<ILogisticsClient>
{
    public override async Task OnConnectedAsync()
    {
        var authentication = await Context.GetHttpContext()!.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var stamp = Context.User?.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType);
        if (!Guid.TryParse(Context.UserIdentifier, out var userId) || stamp is null || authentication.Properties?.ExpiresUtc is not { } expiry)
        {
            Context.Abort();
            return;
        }
        connections.Add(Context.ConnectionId, new(userId, stamp, expiry, Context.Abort));
        try
        {
            await connections.ValidConnectionsAsync(Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        catch
        {
            connections.Remove(Context.ConnectionId);
            Context.Abort();
            throw;
        }
    }

    // Acknowledges completion of OnConnectedAsync; no client can mutate logistics through the Hub.
    public Task Ready() => Task.CompletedTask;
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class SignalRLogisticsNotifier(IHubContext<LogisticsHub, ILogisticsClient> hub, LiveConnections connections) : ILogisticsNotifier
{
    public async Task PublishAsync(IReadOnlyList<LogisticsNotification> notifications, CancellationToken cancellationToken)
    {
        if (notifications.Count == 0) return;
        // Revalidate before sending, not merely when establishing the socket.
        var recipients = await connections.ValidConnectionsAsync(cancellationToken);
        if (recipients.Length == 0) return;
        foreach (var notification in notifications)
            await hub.Clients.Clients(recipients).LogisticsChanged(notification).WaitAsync(cancellationToken);
    }
}
