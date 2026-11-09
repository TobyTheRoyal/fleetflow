using System.Security.Claims;
using FleetFlow.Api.Security;
using FleetFlow.Application.Realtime;
using FleetFlow.Infrastructure.Portfolio;
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
        var http = Context.GetHttpContext()!;
        var authentication = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        // Hub activation has a different DI scope. Use the scope bound by cookie validation
        // on the authenticated connection request, never a fresh unbound Hub service scope.
        var scope = http.RequestServices.GetRequiredService<PortfolioScope>();
        var stamp = Context.User?.FindFirstValue(identity.Value.ClaimsIdentity.SecurityStampClaimType);
        if (!Guid.TryParse(Context.UserIdentifier, out var userId) || stamp is null || authentication.Properties?.ExpiresUtc is not { } expiry)
        {
            Context.Abort();
            return;
        }
        connections.Add(Context.ConnectionId, new(userId, stamp, expiry, Context.Abort, scope.Schema, scope.LeaseId));
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
        foreach (var group in notifications.GroupBy(n => n.Sandbox))
        {
            var recipients = await connections.RecipientsAsync(group.Key, cancellationToken);
            if (recipients.Length == 0) continue;
            foreach (var notification in group)
                await hub.Clients.Clients(recipients).LogisticsChanged(notification).WaitAsync(cancellationToken);
        }
    }
}
