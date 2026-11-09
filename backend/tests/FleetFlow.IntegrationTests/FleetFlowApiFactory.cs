using FleetFlow.Application.Insights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FleetFlow.IntegrationTests;

internal sealed class FleetFlowApiFactory(string connectionString, string environment = "Development", TimeProvider? clock = null, Action<IServiceCollection>? configureServices = null, IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IAiExplanationProvider, NoExternalInference>();
            if (clock is not null) services.AddSingleton(clock);
            configureServices?.Invoke(services);
        });
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings()));
    }
    private sealed class NoExternalInference : IAiExplanationProvider
    {
        public Task<string> GenerateAsync(AiPrompt prompt, CancellationToken token) => throw new InvalidOperationException("Integration tests require an explicitly configured fake provider; external inference is forbidden.");
    }
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Minimal hosting reads production settings before ConfigureWebHost's late app configuration.
        // Host configuration is forwarded as in-process arguments by WebApplicationFactory.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(Settings()));
        return base.CreateHost(builder);
    }
    private Dictionary<string, string?> Settings()
    {
        var values = new Dictionary<string, string?> { ["environment"] = environment, ["ConnectionStrings:FleetFlow"] = connectionString, ["AI_ENABLED"] = "false", ["CLOUDFLARE_API_TOKEN"] = "", ["Deployment:PublicOrigin"] = "https://localhost", ["DataProtection:CertificatePath"] = TestCertificate.PfxPath };
        if (configurationOverrides is not null) foreach (var item in configurationOverrides) values[item.Key] = item.Value;
        return values;
    }
}
