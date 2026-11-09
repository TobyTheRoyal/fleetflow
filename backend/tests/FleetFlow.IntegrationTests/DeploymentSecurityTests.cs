using System.Net;
using FleetFlow.Api.Security;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class DeploymentSecurityTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal const string RenderHost = "fleetflow-test.onrender.com";
    internal const string RenderOrigin = "https://" + RenderHost;

    internal static Dictionary<string, string?> RenderSettings() => new()
    {
        ["Deployment:RenderFreeEdgeHttps"] = "true",
        ["Deployment:PublicOrigin"] = RenderOrigin,
        ["AllowedHosts"] = RenderHost,
        ["RENDER"] = "true",
        ["RENDER_SERVICE_TYPE"] = "web",
        ["RENDER_EXTERNAL_URL"] = RenderOrigin,
        ["RENDER_EXTERNAL_HOSTNAME"] = RenderHost
    };

    [Theory]
    [InlineData("RENDER", null)]
    [InlineData("RENDER", "false")]
    [InlineData("RENDER_SERVICE_TYPE", "pserv")]
    [InlineData("RENDER_EXTERNAL_HOSTNAME", null)]
    [InlineData("RENDER_EXTERNAL_HOSTNAME", "evil.example")]
    [InlineData("RENDER_EXTERNAL_HOSTNAME", "fleetflow-test.onrender.com.evil.example")]
    [InlineData("RENDER_EXTERNAL_URL", null)]
    [InlineData("RENDER_EXTERNAL_URL", "http://" + RenderHost)]
    [InlineData("RENDER_EXTERNAL_URL", RenderOrigin + "/path")]
    [InlineData("RENDER_EXTERNAL_URL", "https://other.onrender.com")]
    [InlineData("Deployment:PublicOrigin", "https://other.onrender.com")]
    [InlineData("AllowedHosts", "other.onrender.com")]
    [InlineData("AllowedHosts", RenderHost + ";other.onrender.com")]
    [InlineData("Deployment:TrustedProxies:0", "127.0.0.1")]
    [InlineData("Deployment:TrustedNetworks:0", "0.0.0.0/0")]
    [InlineData("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")]
    public void Render_configuration_fails_closed_on_invalid_metadata_or_mixed_trust(string key, string? value)
    {
        var values = Settings();
        foreach (var setting in RenderSettings()) values[setting.Key] = setting.Value;
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() => new DeploymentSecrets(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public async Task Render_serves_the_spa_over_the_internal_http_hop_and_rejects_the_wrong_host()
    {
        var webRoot = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot.FullName, "index.html"), "<!doctype html><div id=\"root\"></div>", Token);
            var settings = RenderSettings();
            settings["SERVE_SPA"] = "true";
            settings["webroot"] = webRoot.FullName;
            await using var app = new FleetFlowApiFactory(postgres.ConnectionString, "Production", configurationOverrides: settings);
            using var client = app.CreateClient(new() { BaseAddress = new("http://" + RenderHost), AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/shipments/example");
            request.Headers.Add("X-Forwarded-Proto", "http");
            request.Headers.Add("X-Forwarded-Host", "evil.example");
            request.Headers.Add("Forwarded", "proto=http;host=evil.example");
            using var page = await client.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("id=\"root\"", await page.Content.ReadAsStringAsync(Token));
            Assert.Contains("max-age=2592000", page.Headers.GetValues("Strict-Transport-Security").Single());
            Assert.Contains("wss://" + RenderHost, page.Headers.GetValues("Content-Security-Policy").Single());
            Assert.Null(page.Headers.Location);

            foreach (var host in new[] { "evil.example", RenderHost + ":10000" })
            {
                using var wrong = new HttpRequestMessage(HttpMethod.Get, "/health");
                wrong.Headers.Host = host;
                wrong.Headers.Add("X-Forwarded-Host", RenderHost);
                using var rejected = await client.SendAsync(wrong, Token);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("Production", HttpStatusCode.TemporaryRedirect)]
    [InlineData("Development", HttpStatusCode.NotFound)]
    public async Task Render_metadata_alone_does_not_enable_the_mode_and_development_ignores_it(string environment, HttpStatusCode expected)
    {
        var values = RenderSettings();
        values["Deployment:RenderFreeEdgeHttps"] = environment == "Development" ? "true" : "false";
        await using var app = new FleetFlowApiFactory(postgres.ConnectionString, environment, configurationOverrides: values);
        using var client = app.CreateClient(new() { BaseAddress = new("http://" + RenderHost), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/api/v1/demo", Token);
        Assert.Equal(expected, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData("Deployment:PublicOrigin", "http://localhost")]
    [InlineData("Deployment:PublicOrigin", "https://localhost/path")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("DataProtection:CertificatePath", "")]
    [InlineData("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")]
    public void Invalid_production_configuration_is_rejected(string key, string value)
    {
        var values = Settings(); values[key] = value;
        Assert.Throws<InvalidOperationException>(() => new DeploymentSecrets(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }
    [Theory]
    [InlineData("SSL Mode=Require;Maximum Pool Size=10")]
    [InlineData("SSL Mode=VerifyFull;Maximum Pool Size=100")]
    [InlineData("SSL Mode=VerifyFull;Maximum Pool Size=10;Keepalive=60")]
    public void Unsafe_database_transport_and_pool_configuration_is_rejected(string options)
    {
        var values = Settings(); values["ConnectionStrings:FleetFlow"] = "Host=localhost;Database=unused;" + options;
        Assert.Throws<InvalidOperationException>(() => new DeploymentSecrets(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }
    [Theory]
    [InlineData("direct.example", "direct.example", 5432, "VerifyFull", true)]
    [InlineData("", "direct.example", 5432, "VerifyFull", false)]
    [InlineData("other.example", "direct.example", 5432, "VerifyFull", false)]
    [InlineData("direct.example", "other.example", 5432, "VerifyFull", false)]
    [InlineData("direct.example", "direct.example", 6432, "VerifyFull", false)]
    [InlineData("direct.example", "direct.example", 5432, "Require", false)]
    public void Initialization_requires_the_confirmed_endpoint_and_secure_runtime_connection(string confirmedHost, string runtimeHost, int runtimePort, string sslMode, bool valid)
    {
        var values = new Dictionary<string, string?>
        {
            ["Portfolio:ConfirmHost"] = confirmedHost,
            ["Portfolio:ProvisionLogins"] = "true",
            ["ConnectionStrings:FleetFlow"] = "Host=direct.example;Database=portfolio;SSL Mode=VerifyFull",
            ["ConnectionStrings:PortfolioRuntime"] = $"Host={runtimeHost};Port={runtimePort};Database=portfolio;SSL Mode={sslMode};Maximum Pool Size=10"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        if (valid) DeploymentSecrets.ValidateInitializationTarget(config);
        else Assert.Throws<InvalidOperationException>(() => DeploymentSecrets.ValidateInitializationTarget(config));
    }
    [Theory]
    [InlineData(false, HttpStatusCode.TemporaryRedirect)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task Forwarded_https_is_accepted_only_from_an_explicit_trusted_peer(bool trusted, HttpStatusCode expected)
    {
        var values = Settings();
        if (trusted) values["Deployment:TrustedProxies:0"] = "127.0.0.1";
        await using var api = new FleetFlowApiFactory(postgres.ConnectionString, "Production", configureServices: services => services.AddSingleton<IStartupFilter, LoopbackPeer>(), configurationOverrides: values);
        using var client = api.CreateClient(new() { BaseAddress = new("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/demo");
        request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-For", "203.0.113.5"); request.Headers.Add("X-Forwarded-Host", "evil.example");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(expected, response.StatusCode);
        if (trusted) Assert.Contains("max-age=2592000", response.Headers.GetValues("Strict-Transport-Security").Single());
        else Assert.Equal("localhost", response.Headers.Location!.Host);
    }
    [Fact]
    public async Task Normal_mode_does_not_expose_demo_entry()
    {
        await using var api = new FleetFlowApiFactory(postgres.ConnectionString);
        await using (var scope = api.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().Database.MigrateAsync(Token);
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/demo", Token)).StatusCode);
        await TestAuthentication.RefreshCsrfAsync(client);
        using var body = new StringContent("{\"mode\":\"Viewer\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/demo/sessions", body, Token)).StatusCode);
    }
    [Fact]
    public async Task Explicit_initialization_and_login_provisioning_need_no_PostgreSQL_superuser()
    {
        var suffix = Guid.NewGuid().ToString("N"); var ownerName = "migration_" + suffix; var database = "release_" + suffix;
        var runtimeName = "web_" + suffix; var maintenanceName = "maintenance_" + suffix;
        var password = TestAuthentication.Password();
        await using var admin = new NpgsqlConnection(postgres.ConnectionString); await admin.OpenAsync(Token);
        await using (var setting = new NpgsqlCommand("SELECT set_config('portfolio.owner_password',@password,false)", admin))
        { setting.Parameters.AddWithValue("password", password); await setting.ExecuteNonQueryAsync(Token); }
        await using (var create = new NpgsqlCommand($"DO $$ BEGIN EXECUTE format('CREATE ROLE {ownerName} LOGIN CREATEROLE NOSUPERUSER NOCREATEDB NOBYPASSRLS PASSWORD %L',current_setting('portfolio.owner_password')); END $$;", admin)) await create.ExecuteNonQueryAsync(Token);
        await using (var createDatabase = new NpgsqlCommand($"CREATE DATABASE {database} OWNER {ownerName}", admin)) await createDatabase.ExecuteNonQueryAsync(Token);
        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Username = ownerName, Password = password, Database = database }.ConnectionString;
        var runtime = new NpgsqlConnectionStringBuilder(connection) { Username = runtimeName, Password = TestAuthentication.Password() }.ConnectionString;
        var maintenance = new NpgsqlConnectionStringBuilder(connection) { Username = maintenanceName, Password = TestAuthentication.Password(), MaxPoolSize = 2 }.ConnectionString;
        await using var setup = new FleetFlowApiFactory(connection, configurationOverrides: new Dictionary<string, string?> { ["Portfolio:Enabled"] = "true", ["ConnectionStrings:PortfolioRuntime"] = runtime, ["ConnectionStrings:PortfolioMaintenance"] = maintenance });
        await using (var scope = setup.Services.CreateAsyncScope())
        {
            var initializer = scope.ServiceProvider.GetRequiredService<PortfolioInitializer>();
            await initializer.InitializeAsync(database, Token); await initializer.ProvisionLoginsAsync(Token);
            await initializer.InitializeAsync(database, Token); await initializer.ProvisionLoginsAsync(Token);
        }
        await using var application = new FleetFlowApiFactory(runtime, configurationOverrides: new Dictionary<string, string?> { ["Portfolio:Enabled"] = "true", ["ConnectionStrings:PortfolioMaintenance"] = maintenance });
        using var client = application.CreateClient(new() { BaseAddress = new("https://localhost") });
        await TestAuthentication.RefreshCsrfAsync(client);
        using var entry = await client.PostAsync("/api/v1/demo/sessions", new StringContent("{\"mode\":\"Dispatcher\"}", System.Text.Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/shipments", Token)).StatusCode);

        var unexpectedGroup = "unexpected_" + suffix;
        await using (var extra = new NpgsqlCommand($"CREATE ROLE {unexpectedGroup} NOLOGIN; GRANT {unexpectedGroup} TO {maintenanceName}", admin))
            await extra.ExecuteNonQueryAsync(Token);
        var otherRuntime = new NpgsqlConnectionStringBuilder(runtime) { Username = "other_web_" + suffix };
        await using var unsafeSetup = new FleetFlowApiFactory(connection, configurationOverrides: new Dictionary<string, string?>
        {
            ["Portfolio:Enabled"] = "true",
            ["ConnectionStrings:PortfolioRuntime"] = otherRuntime.ConnectionString,
            ["ConnectionStrings:PortfolioMaintenance"] = maintenance
        });
        await using var unsafeScope = unsafeSetup.Services.CreateAsyncScope();
        var error = await Assert.ThrowsAsync<PostgresException>(() => unsafeScope.ServiceProvider.GetRequiredService<PortfolioInitializer>().ProvisionLoginsAsync(Token));
        Assert.Equal("Unsafe existing portfolio role", error.MessageText);
        await using var roleCheck = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_roles WHERE rolname=@role)", admin);
        roleCheck.Parameters.AddWithValue("role", otherRuntime.Username!);
        Assert.Equal(false, await roleCheck.ExecuteScalarAsync(Token));
    }
    private Dictionary<string, string?> Settings() => new()
    {
        ["ConnectionStrings:FleetFlow"] = postgres.ConnectionString,
        ["Deployment:PublicOrigin"] = "https://localhost",
        ["AllowedHosts"] = "localhost",
        ["DataProtection:CertificatePath"] = TestCertificate.PfxPath
    };
    private sealed class LoopbackPeer : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; return continuation(context); });
            next(app);
        };
    }
}
