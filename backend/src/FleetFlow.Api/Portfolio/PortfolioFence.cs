using FleetFlow.Api.Security;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace FleetFlow.Api.Portfolio;

public static class PortfolioFence
{
    // Hold a shared advisory lease fence while an API response is assembled. The maintenance
    // definer takes its exclusive counterpart before replacing any table contents, including GET races.
    // This deliberately requires a direct PostgreSQL connection, never transaction-mode PgBouncer.
    public static async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var scope = context.RequestServices.GetRequiredService<PortfolioScope>();
        if (scope.SlotId is not { } slot || !context.Request.Path.StartsWithSegments("/api/v1")) { await next(context); return; }
        var limiter = context.RequestServices.GetRequiredService<PortfolioRequestLimiter>();
        if (!await limiter.Requests.WaitAsync(TimeSpan.FromSeconds(5), context.RequestAborted))
        {
            await SessionSecurity.Problem(context, 429, "demo_busy", "The demo is busy. Please try again shortly.").ExecuteAsync(context); return;
        }
        try
        {
            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("FleetFlow"));
            await connection.OpenAsync(context.RequestAborted);
            await using var acquire = new NpgsqlCommand("SELECT pg_advisory_lock_shared(@key)", connection) { CommandTimeout = 10 };
            acquire.Parameters.AddWithValue("key", 741070000L + slot);
            await acquire.ExecuteNonQueryAsync(context.RequestAborted);
            try
            {
                var clock = context.RequestServices.GetRequiredService<TimeProvider>();
                await using var validate = new NpgsqlCommand("SELECT EXISTS(SELECT FROM public.\"PortfolioSlots\" WHERE \"Id\"=@id AND \"LeaseId\"=@lease AND \"Ready\" AND \"ExpiresAtUtc\">@now)", connection);
                validate.Parameters.AddWithValue("id", slot); validate.Parameters.AddWithValue("lease", scope.LeaseId!.Value); validate.Parameters.AddWithValue("now", clock.GetUtcNow());
                if (!(bool)(await validate.ExecuteScalarAsync(context.RequestAborted))!)
                {
                    await SessionSecurity.Problem(context, 401, "demo_expired", "This temporary demo has expired. Start a new demo session.").ExecuteAsync(context); return;
                }
                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
                {
                    var max = context.RequestServices.GetRequiredService<PortfolioSettings>().MaxWrites;
                    await using var count = new NpgsqlCommand("UPDATE public.\"PortfolioSlots\" SET \"Writes\"=\"Writes\"+1 WHERE \"Id\"=@id AND \"LeaseId\"=@lease AND \"Writes\"<@max", connection);
                    count.Parameters.AddWithValue("id", slot); count.Parameters.AddWithValue("lease", scope.LeaseId.Value); count.Parameters.AddWithValue("max", max);
                    if (await count.ExecuteNonQueryAsync(context.RequestAborted) != 1)
                    {
                        await SessionSecurity.Problem(context, 429, "demo_write_budget", "This demo session's write allowance is exhausted. Read-only access remains available.").ExecuteAsync(context); return;
                    }
                }
                await next(context);
            }
            finally
            {
                await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock_shared(@key)", connection) { CommandTimeout = 5 };
                release.Parameters.AddWithValue("key", 741070000L + slot);
                await release.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        finally { limiter.Requests.Release(); }
    }
}
