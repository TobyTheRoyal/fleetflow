using System.Text.Json;
using FleetFlow.Application.Insights;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FleetFlow.BrowserHost;

// Test executable only: never included in either production API/web image.
public static class BrowserHost
{
    public static async Task Main()
    {
        if (Environment.GetEnvironmentVariable("FLEETFLOW_E2E_HOST") != "1" || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN")))
            throw new InvalidOperationException("The browser test host requires an isolated Development stack and no external credentials.");
        var ids = new Guid[3];
        var settings = new AiSettings
        {
            Enabled = true,
            FreeTierVerified = true,
            UsageTermsVerified = true,
            FictionalDataConfirmed = true,
            AccountId = new string('a', 32),
            ApiToken = "synthetic-browser-token-never-used",
            VerifiedModel = AiSettings.ApprovedModel,
            VerifiedUntilUtc = DateTimeOffset.UtcNow.AddDays(1),
            AllowedUserIds = ids
        };
        await using var factory = new BrowserApiFactory(settings);
        factory.UseKestrel(options => options.ListenAnyIP(8080));
        var services = factory.Services;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; stop.Cancel(); };
        System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += _ => stop.Cancel();
        // Normal CLI migrations/Identity seeding runs after startup. Authorize only those real fixture accounts.
        while (!stop.IsCancellationRequested)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
            var exists = await db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"AspNetUsers\"') IS NOT NULL AS \"Value\"").SingleAsync(stop.Token);
            if (exists)
            {
                string[] emails = ["admin@fleetflow.example", "dispatcher@fleetflow.example", "viewer@fleetflow.example"];
                var users = await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email!)).OrderBy(u => u.Email).Select(u => u.Id).ToArrayAsync(stop.Token);
                if (users.Length == 3) { Array.Copy(users, ids, 3); break; }
            }
            await Task.Delay(250, stop.Token);
        }
        try { await Task.Delay(Timeout.Infinite, stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private sealed class BrowserApiFactory(AiSettings settings) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(settings);
                services.AddSingleton<IAiExplanationProvider, DeterministicProvider>();
            });
        }
    }
    private sealed class DeterministicProvider : IAiExplanationProvider
    {
        public Task<string> GenerateAsync(AiPrompt prompt, CancellationToken token) => Task.FromResult(JsonSerializer.Serialize(
            new AiSelection(prompt.Candidates.Take(3).Select(c => new AiChoice(c.FindingId, c.AllowedActions[0])).ToArray(), prompt.AllowedSummarySentences.Take(2).ToArray()), InsightJson.Options));
    }
}
