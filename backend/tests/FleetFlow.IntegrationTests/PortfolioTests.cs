using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using FleetFlow.Api.Contracts;
using FleetFlow.Api.Controllers;
using FleetFlow.Api.Realtime;
using FleetFlow.Application.Insights;
using FleetFlow.Application.Realtime;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class PortfolioTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private readonly string database = "portfolio_" + Guid.NewGuid().ToString("N");
    private readonly string runtimeRole = "ff_runtime_" + Guid.NewGuid().ToString("N");
    private readonly string maintenanceRole = "ff_maint_" + Guid.NewGuid().ToString("N");
    private readonly PortfolioSettings settings = new() { Enabled = true, ViewerSlots = 1, DispatcherSlots = 2, LeaseMinutes = 30 };
    private readonly FrozenClock clock = new();
    private FleetFlowApiFactory owner = null!;
    private FleetFlowApiFactory api = null!;
    private string ownerConnection = null!;
    private string runtimeConnection = null!;
    private string maintenanceConnection = null!;
    private readonly List<HubConnection> hubs = [];
    private readonly ReadBarrier barrier = new();
    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString); await connection.OpenAsync(Token);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection)) await create.ExecuteNonQueryAsync(Token);
        ownerConnection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        owner = Factory(ownerConnection);
        await using (var scope = owner.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PortfolioInitializer>().InitializeAsync(database, Token);
        foreach (var (role, group) in new[] { (runtimeRole, "fleetflow_portfolio_runtime"), (maintenanceRole, "fleetflow_portfolio_maintenance") })
        {
            var password = TestAuthentication.Password();
            await using var passwordSetting = new NpgsqlCommand("SELECT set_config('portfolio.test_password',@password,false)", connection);
            passwordSetting.Parameters.AddWithValue("password", password); await passwordSetting.ExecuteNonQueryAsync(Token);
            await using var create = new NpgsqlCommand($"DO $$ BEGIN EXECUTE format('CREATE ROLE {role} LOGIN PASSWORD %L',current_setting('portfolio.test_password')); END $$; GRANT {group} TO {role}", connection);
            await create.ExecuteNonQueryAsync(Token);
            var configured = new NpgsqlConnectionStringBuilder(ownerConnection) { Username = role, Password = password, MaxPoolSize = group == "fleetflow_portfolio_maintenance" ? 2 : 10 }.ConnectionString;
            if (group == "fleetflow_portfolio_runtime") runtimeConnection = configured; else maintenanceConnection = configured;
        }
        api = Factory(runtimeConnection);
    }
    private FleetFlowApiFactory Factory(string connection) => new(connection, clock: clock,
        configureServices: services => { services.AddSingleton(settings); services.Configure<MvcOptions>(options => options.Filters.Add(barrier)); },
        configurationOverrides: new Dictionary<string, string?> { ["Portfolio:Enabled"] = "true", ["ConnectionStrings:PortfolioMaintenance"] = maintenanceConnection });
    public async ValueTask DisposeAsync()
    {
        foreach (var hub in hubs) await hub.DisposeAsync();
        if (api is not null) await api.DisposeAsync();
        if (owner is not null) await owner.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString); await connection.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection)) await drop.ExecuteNonQueryAsync();
        foreach (var role in new[] { runtimeRole, maintenanceRole })
        {
            await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", connection); await drop.ExecuteNonQueryAsync();
        }
    }
    private async Task<(HttpClient Client, SessionUser User)> Start(string mode)
    {
        var client = api.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await TestAuthentication.RefreshCsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode }, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<SessionUser>(Token))!;
        await TestAuthentication.RefreshCsrfAsync(client);
        return (client, user);
    }
    [Theory]
    [InlineData("logout")]
    [InlineData("lease-expiry")]
    [InlineData("deactivation")]
    public async Task SignalR_routes_only_within_the_lease_and_revokes_old_connections(string revocation)
    {
        var captureA = new Cookies(); var captureB = new Cookies();
        using var a = api.CreateDefaultClient(captureA); a.BaseAddress = new("https://localhost");
        using var b = api.CreateDefaultClient(captureB); b.BaseAddress = new("https://localhost");
        foreach (var client in new[] { a, b })
        {
            await TestAuthentication.RefreshCsrfAsync(client);
            using var start = await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Dispatcher" }, Token);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            await TestAuthentication.RefreshCsrfAsync(client);
        }
        var first = await Connect(a, captureA.Value);
        var secondTab = await Connect(a, captureA.Value);
        var unrelated = await Connect(b, captureB.Value);
        using var writeA = await a.PostAsJsonAsync("/api/v1/customers", new { name = "Only A", contactName = "Demo", email = "a@example.test" }, Token);
        Assert.Equal(HttpStatusCode.Created, writeA.StatusCode);
        var eventA = await first.Events.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
        var tabEvent = await secondTab.Events.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(eventA.EventId, tabEvent.EventId);
        using var writeB = await b.PostAsJsonAsync("/api/v1/customers", new { name = "Only B", contactName = "Demo", email = "b@example.test" }, Token);
        using var json = JsonDocument.Parse(await writeB.Content.ReadAsStringAsync(Token));
        var eventB = await unrelated.Events.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(json.RootElement.GetProperty("id").GetGuid(), eventB.EntityId);
        Assert.False(unrelated.Events.Reader.TryRead(out _));
        Assert.False(first.Events.Reader.TryRead(out _));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        if (revocation == "logout") Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/api/v1/auth/logout", null, Token)).StatusCode);
        else
        {
            var userA = (await a.GetFromJsonAsync<SessionUser>("/api/v1/auth/me", Token))!;
            await using var scope = owner.Services.CreateAsyncScope();
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityPersistence>();
            if (revocation == "lease-expiry") await identityDb.PortfolioSlots.Where(x => x.UserId == userA.Id).ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAtUtc, clock.Now), Token);
            else await identityDb.Users.Where(x => x.Id == userA.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false), Token);
        }
        await api.Services.GetRequiredService<LiveConnections>().ValidConnectionsAsync(Token);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.Equal(1, api.Services.GetRequiredService<LiveConnections>().Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/v1/shipments", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/v1/shipments", Token)).StatusCode);
    }
    [Fact]
    public async Task Production_encrypts_persistent_keys_and_restores_the_session_after_restart()
    {
        var configuration = new Dictionary<string, string?> { ["Portfolio:Enabled"] = "true", ["ConnectionStrings:PortfolioMaintenance"] = maintenanceConnection };
        var cookies = new Cookies();
        string header;
        await using (var production = new FleetFlowApiFactory(runtimeConnection, "Production", clock, services => services.AddSingleton(settings), configuration))
        {
            using var client = production.CreateDefaultClient(cookies); client.BaseAddress = new("https://localhost");
            await TestAuthentication.RefreshCsrfAsync(client);
            using var start = await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token);
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);
            Assert.Contains(start.Headers.GetValues("Set-Cookie"), x => x.Contains("secure", StringComparison.OrdinalIgnoreCase) && x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
            header = cookies.Value;
            using var page = await client.GetAsync("/api/v1/shipments", Token);
            Assert.Equal("no-store", page.Headers.CacheControl!.ToString());
            Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
            Assert.True(page.Headers.Contains("Strict-Transport-Security"));
        }
        await using (var scope = owner.Services.CreateAsyncScope())
        {
            var keys = await scope.ServiceProvider.GetRequiredService<IdentityPersistence>().DataProtectionKeys.Select(k => k.Xml!).ToArrayAsync(Token);
            Assert.NotEmpty(keys); Assert.All(keys, xml => { Assert.Contains("encryptedSecret", xml); Assert.DoesNotContain("<masterKey", xml); });
        }
        await using var restarted = new FleetFlowApiFactory(runtimeConnection, "Production", clock, services => services.AddSingleton(settings), configuration);
        using var restored = restarted.CreateClient(new() { BaseAddress = new("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        restored.DefaultRequestHeaders.Add("Cookie", header);
        Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        using var spoof = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/demo");
        spoof.Headers.Add("X-Forwarded-Proto", "https"); spoof.Headers.Add("X-Forwarded-For", "203.0.113.20");
        using var refused = await restored.SendAsync(spoof, Token);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, refused.StatusCode);
        Assert.Equal("https", refused.Headers.Location!.Scheme);
    }
    [Fact]
    public async Task Viewer_is_a_real_bounded_identity_and_cannot_write_or_administer()
    {
        var (client, user) = await Start("Viewer"); using var owned = client;
        Assert.Equal("Viewer", user.Role); Assert.NotNull(user.Demo);
        Assert.Equal(clock.Now.AddMinutes(30), user.Demo.ExpiresAtUtc);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/shipments", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/customers", new { name = "Fictional", contactName = "Demo", email = "demo@example.test" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/users", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/insights/ai-analysis", new { snapshotFingerprint = new string('a', 64) }, Token)).StatusCode);
        var availability = (await client.GetFromJsonAsync<DemoAvailability>("/api/v1/demo", Token))!;
        Assert.Equal(0, availability.ViewerAvailable);
        using var second = api.CreateClient(new() { BaseAddress = new("https://localhost") }); await TestAuthentication.RefreshCsrfAsync(second);
        using var full = await second.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
    }
    [Fact]
    public async Task Dispatcher_queries_writes_insights_and_foreign_keys_are_isolated()
    {
        var (a, _) = await Start("Dispatcher"); using var ownedA = a;
        var (b, _) = await Start("Dispatcher"); using var ownedB = b;
        using var created = await b.PostAsJsonAsync("/api/v1/customers", new { name = "Private fictional B", contactName = "Demo", email = "b@example.test" }, Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var customer = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Token));
        var id = customer.RootElement.GetProperty("id").GetGuid();
        var listA = await a.GetStringAsync("/api/v1/customers", Token);
        Assert.DoesNotContain(id.ToString(), listA); Assert.DoesNotContain("Private fictional B", listA);
        Assert.Contains(id.ToString(), await b.GetStringAsync("/api/v1/customers", Token));
        var slots = await Slots(); var userA = (await a.GetFromJsonAsync<SessionUser>("/api/v1/auth/me", Token))!;
        var schemaA = slots.Single(s => s.UserId == userA.Id).SchemaName;
        await using var connection = new NpgsqlConnection(ownerConnection); await connection.OpenAsync(Token);
        // Direct SQL proves that the sandbox's FK cannot reference a customer existing only in B.
        var sql = $"UPDATE \"{schemaA}\".\"Shipments\" SET \"CustomerId\"=@customer WHERE \"Id\"=(SELECT \"Id\" FROM \"{schemaA}\".\"Shipments\" LIMIT 1)";
        await using var update = new NpgsqlCommand(sql, connection); update.Parameters.AddWithValue("customer", id);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync(Token));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, failure.SqlState);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/insights/summary", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/v1/dashboard/summary", Token)).StatusCode);
    }
    [Fact]
    public async Task Concurrent_leasing_never_issues_the_same_active_slot()
    {
        using var a = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var b = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        await TestAuthentication.RefreshCsrfAsync(a); await TestAuthentication.RefreshCsrfAsync(b);
        var replies = await Task.WhenAll(a.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token), b.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var reply in replies) reply.Dispose();
    }
    [Fact]
    public async Task Expired_lease_is_revoked_and_reuse_resets_only_its_sandbox_and_preserves_quotas()
    {
        var (a, original) = await Start("Dispatcher"); using var ownedA = a;
        clock.Now = clock.Now.AddMinutes(15);
        var (b, _) = await Start("Dispatcher"); using var ownedB = b;
        using var privateB = await b.PostAsJsonAsync("/api/v1/customers", new { name = "Retain B", contactName = "Demo", email = "b@example.test" }, Token);
        Assert.Equal(HttpStatusCode.Created, privateB.StatusCode);
        using var create = await a.PostAsJsonAsync("/api/v1/customers", new { name = "Discard me", contactName = "Demo", email = "discard@example.test" }, Token);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await using (var scope = owner.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAiUsageStore>();
            var reservation = await store.ReserveAsync(original.Id, new string('a', 64), AiSettings.ApprovedModel, Token);
            await store.CompleteAsync(reservation.RequestId!.Value, null, "test_failure", Token);
        }
        clock.Now = clock.Now.AddMinutes(16);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/v1/shipments", Token)).StatusCode);
        var (reused, next) = await Start("Dispatcher"); using var ownedNext = reused;
        Assert.Equal(original.Id, next.Id); Assert.NotEqual(original.Demo!.LeaseId, next.Demo!.LeaseId);
        Assert.DoesNotContain("Discard me", await reused.GetStringAsync("/api/v1/customers", Token));
        Assert.Contains("Retain B", await b.GetStringAsync("/api/v1/customers", Token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/v1/customers", Token)).StatusCode);
        await using var verification = owner.Services.CreateAsyncScope(); var db = verification.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        Assert.Equal(1, await db.AiAttempts.CountAsync(Token));
        Assert.Equal(3, await db.Users.CountAsync(Token));
        Assert.True(await db.DataProtectionKeys.AnyAsync(Token));
    }
    [Fact]
    public async Task In_flight_reads_fence_reset_until_the_old_response_is_complete()
    {
        var (client, original) = await Start("Dispatcher"); using var owned = client;
        await client.PostAsJsonAsync("/api/v1/customers", new { name = "Old visitor only", contactName = "Demo", email = "old@example.test" }, Token);
        using var oldRead = new HttpRequestMessage(HttpMethod.Get, "/api/v1/customers"); oldRead.Headers.Add("X-Test-Read-Barrier", "hold");
        var reading = client.SendAsync(oldRead, Token);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        clock.Now = clock.Now.AddMinutes(31);
        // Occupy the other slot so reuse must reserve the expired first slot.
        var (other, _) = await Start("Dispatcher"); using var ownedOther = other;
        var starting = Start("Dispatcher");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await Slots()).Single(s => s.UserId == original.Id).Ready) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(10, Token); }
        Assert.False(starting.IsCompleted);
        barrier.Release.TrySetResult();
        using var response = await reading; Assert.Contains("Old visitor only", await response.Content.ReadAsStringAsync(Token));
        var (next, _) = await starting; using var ownedNext = next;
        Assert.DoesNotContain("Old visitor only", await next.GetStringAsync("/api/v1/customers", Token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/customers", Token)).StatusCode);
    }
    [Fact]
    public async Task A_late_logout_cannot_revoke_the_next_visitor_of_the_same_identity()
    {
        var (old, user) = await Start("Dispatcher"); using var owned = old;
        var (other, _) = await Start("Dispatcher"); using var ownedOther = other;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout"); request.Headers.Add("X-Test-Read-Barrier", "hold");
        var loggingOut = old.SendAsync(request, Token);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        clock.Now = clock.Now.AddMinutes(31);
        var starting = Start("Dispatcher");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((await Slots()).Single(s => s.UserId == user.Id).Ready) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(10, Token); }
        Assert.False(starting.IsCompleted);
        barrier.Release.TrySetResult();
        using var ended = await loggingOut; Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        var (next, reused) = await starting; using var ownedNext = next;
        Assert.Equal(user.Id, reused.Id); Assert.NotEqual(user.Demo!.LeaseId, reused.Demo!.LeaseId);
        Assert.Equal(HttpStatusCode.OK, (await next.GetAsync("/api/v1/shipments", Token)).StatusCode);
    }
    [Fact]
    public async Task Sandbox_lifecycle_keeps_versions_history_and_resource_conflicts()
    {
        var (client, _) = await Start("Dispatcher"); using var owned = client;
        var customer = await Post<CustomerResponse>(client, "customers", new { name = "Portfolio logistics", contactName = "Demo", email = "portfolio@example.test" });
        var driver = await Post<DriverResponse>(client, "drivers", new { employeeNumber = "PORT-1", firstName = "Morgan", lastName = "Demo", active = true });
        var vehicle = await Post<VehicleResponse>(client, "vehicles", new { plateNumber = "PORT-1", type = "Van", maxPayloadKg = 2000, status = "Available" });
        var shipment = await Post<ShipmentResponse>(client, "shipments", new
        {
            customerId = customer.Id,
            origin = new { street = "Depot 1", city = "Graz", postcode = "8010", countryCode = "AT" },
            destination = new { street = "Depot 2", city = "Vienna", postcode = "1010", countryCode = "AT" },
            pickupWindowStartUtc = clock.Now.AddHours(1),
            deliveryDeadlineUtc = clock.Now.AddHours(5),
            weightKg = 1000m,
            palletCount = 1
        });
        shipment = await Post<ShipmentResponse>(client, $"shipments/{shipment.Id}/assign", new { version = shipment.Version, driverId = driver.Id, vehicleId = vehicle.Id });
        using var stale = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = 1, status = "Planned" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        foreach (var status in new[] { "Planned", "InTransit", "Delivered" }) shipment = await Post<ShipmentResponse>(client, $"shipments/{shipment.Id}/status", new { version = shipment.Version, status });
        using var events = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/shipments/{shipment.Id}/events", Token));
        Assert.Equal(5, events.RootElement.GetProperty("totalCount").GetInt32());
        var (other, _) = await Start("Dispatcher"); using var ownedOther = other;
        other.DefaultRequestHeaders.Add("X-Sandbox-Id", "portfolio_dispatch_1");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/shipments/{shipment.Id}", Token)).StatusCode);
        using var cross = await other.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = shipment.Version, status = "Cancelled" }, Token);
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
    }
    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/" + path, body, Token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }, Token))!;
    }
    [Fact]
    public async Task Runtime_cannot_reset_tables_or_disable_immutable_history()
    {
        await using var connection = new NpgsqlConnection(runtimeConnection); await connection.OpenAsync(Token);
        foreach (var sql in new[] { "SELECT * FROM public.\"ShipmentEvents\"", "TRUNCATE portfolio_dispatch_1.\"ShipmentEvents\"", "ALTER TABLE portfolio_dispatch_1.\"ShipmentEvents\" DISABLE TRIGGER ALL", "SELECT public.portfolio_prepare_slot(11,gen_random_uuid(),now())" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);
        }
        await using var ownerConnectionDb = new NpgsqlConnection(ownerConnection); await ownerConnectionDb.OpenAsync(Token);
        await using var mutate = new NpgsqlCommand("DELETE FROM portfolio_dispatch_1.\"ShipmentEvents\"", ownerConnectionDb);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync(Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
    }
    [Fact]
    public async Task Initialization_is_idempotent_and_downgrade_is_refused()
    {
        var (client, user) = await Start("Dispatcher"); using var owned = client;
        await using var scope = owner.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PortfolioInitializer>().InitializeAsync(database, Token);
        Assert.Equal(user.Id, (await client.GetFromJsonAsync<SessionUser>("/api/v1/auth/me", Token))!.Id);
        var options = new DbContextOptionsBuilder<FleetFlowDbContext>().UseNpgsql(ownerConnection).Options;
        await using var db = new FleetFlowDbContext(options);
        await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261108214714_AddAiUsageLedger", Token));
    }
    [Fact]
    public async Task Anonymous_entry_requires_csrf_and_rejects_invalid_roles_and_excessive_requests()
    {
        using var client = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token)).StatusCode);
        await TestAuthentication.RefreshCsrfAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Admin" }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token)).StatusCode);
    }
    private async Task<PortfolioSlot[]> Slots()
    {
        await using var scope = owner.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IdentityPersistence>().PortfolioSlots.AsNoTracking().ToArrayAsync(Token);
    }
    [Fact]
    public async Task Disabled_slots_are_not_leased_and_active_cookies_are_rejected()
    {
        var (client, user) = await Start("Viewer"); using var owned = client;
        await using (var scope = owner.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IdentityPersistence>().Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false), Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/shipments", Token)).StatusCode);
        clock.Now = clock.Now.AddMinutes(31);
        using var visitor = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        Assert.Equal(0, (await visitor.GetFromJsonAsync<DemoAvailability>("/api/v1/demo", Token))!.ViewerAvailable);
        await TestAuthentication.RefreshCsrfAsync(visitor);
        Assert.Equal(HttpStatusCode.Conflict, (await visitor.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Viewer" }, Token)).StatusCode);
    }
    [Fact]
    public async Task Write_allowance_is_atomic_and_does_not_block_reading()
    {
        await using var limited = new FleetFlowApiFactory(runtimeConnection, clock: clock,
            configureServices: services => services.AddSingleton(new PortfolioSettings { Enabled = true, ViewerSlots = 1, DispatcherSlots = 2, MaxWrites = 1 }),
            configurationOverrides: new Dictionary<string, string?> { ["Portfolio:Enabled"] = "true", ["ConnectionStrings:PortfolioMaintenance"] = maintenanceConnection });
        using var client = limited.CreateClient(new() { BaseAddress = new("https://localhost") });
        await TestAuthentication.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/demo/sessions", new { mode = "Dispatcher" }, Token)).StatusCode);
        await TestAuthentication.RefreshCsrfAsync(client);
        var replies = await Task.WhenAll(Enumerable.Range(1, 2).Select(i => client.PostAsJsonAsync("/api/v1/customers", new { name = $"Budget {i}", contactName = "Demo", email = "budget@example.test" }, Token)));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        foreach (var reply in replies) reply.Dispose();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/customers", Token)).StatusCode);
    }
    private sealed class FrozenClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private async Task<(HubConnection Hub, Channel<LogisticsNotification> Events)> Connect(HttpClient client, string cookie)
    {
        var events = Channel.CreateUnbounded<LogisticsNotification>();
        var hub = new HubConnectionBuilder().WithUrl("https://localhost/hubs/logistics", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
            options.Headers["Cookie"] = cookie; options.Headers["Origin"] = "https://localhost";
            options.Headers["X-CSRF-TOKEN"] = client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        }).Build();
        hubs.Add(hub); hub.On<LogisticsNotification>("LogisticsChanged", item => events.Writer.TryWrite(item));
        await hub.StartAsync(Token); await hub.InvokeAsync("Ready", Token); return (hub, events);
    }
    private sealed class Cookies : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();
        public string Value => cookies.GetCookieHeader(new("https://localhost"));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", header);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }
    private sealed class ReadBarrier : IAsyncActionFilter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.Request.Headers.ContainsKey("X-Test-Read-Barrier"))
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), context.HttpContext.RequestAborted);
            }
            await next();
        }
    }
}
