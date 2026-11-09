using FleetFlow.Api.Security;
using Microsoft.AspNetCore.Antiforgery;

namespace FleetFlow.Api.Realtime;

public static class HubSecurity
{
    public const string Path = "/hubs/logistics";
    public static async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments(Path)) { await next(context); return; }
        var request = context.Request;
        var origin = request.Headers.Origin.ToString();
        var sameOrigin = context.RequestServices.GetRequiredService<IConfiguration>()["Deployment:PublicOrigin"]?.TrimEnd('/') ?? $"{request.Scheme}://{request.Host}";
        var environment = context.RequestServices.GetRequiredService<IHostEnvironment>();
        string[] developmentOrigins = ["http://127.0.0.1:5173", "http://localhost:5173", "http://127.0.0.1:5174", "http://localhost:5174", "http://127.0.0.1:5175", "http://localhost:5175"];
        if ((origin.Length > 0 && origin != sameOrigin && !(environment.IsDevelopment() && developmentOrigins.Contains(origin)))
            || (context.WebSockets.IsWebSocketRequest && origin.Length == 0))
        {
            await SessionSecurity.Problem(context, 403, "origin_forbidden", "This origin is not allowed.").ExecuteAsync(context);
            return;
        }
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method))
        {
            try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException)
            {
                await SessionSecurity.Problem(context, 400, "csrf_invalid", "Refresh the session and try again.").ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }
}
