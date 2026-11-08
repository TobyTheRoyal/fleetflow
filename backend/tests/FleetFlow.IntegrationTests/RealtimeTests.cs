using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using FleetFlow.Api.Contracts;
using FleetFlow.Api.Realtime;
using FleetFlow.Application.Fleet;
using FleetFlow.Application.Realtime;
using FleetFlow.Domain.Shipments;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class RealtimeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly string database = "live_" + Guid.NewGuid().ToString("N");
    private readonly Clock clock = new();
    private readonly RollbackInterceptor rollback = new();
    private FleetFlowApiFactory factory = null!;
    private HttpClient writer = null!;
    private readonly List<HubConnection> hubs = [];
    private readonly List<HttpClient> clients = [];
    private string connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Token);
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await create.ExecuteNonQueryAsync(Token);
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        factory = new(connectionString, clock: clock, configureServices: services =>
            services.AddDbContext<FleetFlowDbContext>(options => options.AddInterceptors(rollback)));
        writer = await TestAuthentication.ClientAsync(factory);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var hub in hubs) await hub.DisposeAsync();
        foreach (var client in clients) client.Dispose();
        writer.Dispose();
        await factory.DisposeAsync();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(CancellationToken.None);
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(FleetRoles.Admin)]
    [InlineData(FleetRoles.Dispatcher)]
    [InlineData(FleetRoles.Viewer)]
    public async Task All_authenticated_read_roles_can_connect(string role)
    {
        var live = await Connect(role);
        Assert.Equal(HubConnectionState.Connected, live.Hub.State);
        Assert.Equal(1, factory.Services.GetRequiredService<LiveConnections>().Count);
    }

    [Fact]
    public async Task Anonymous_negotiate_is_rejected()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/hubs/logistics/negotiate?negotiateVersion=1", null, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example", true, HttpStatusCode.Forbidden)]
    [InlineData("null", true, HttpStatusCode.Forbidden)]
    [InlineData("http://127.0.0.1:5173.evil.example", true, HttpStatusCode.Forbidden)]
    [InlineData("http://127.0.0.1:5173", false, HttpStatusCode.BadRequest)]
    [InlineData("http://127.0.0.1:5173", true, HttpStatusCode.OK)]
    [InlineData("https://localhost", true, HttpStatusCode.OK)]
    public async Task Negotiation_enforces_origin_and_csrf(string origin, bool csrf, HttpStatusCode expected)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/hubs/logistics/negotiate?negotiateVersion=1");
        message.Headers.Add("Origin", origin);
        if (!csrf) writer.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await writer.SendAsync(message, Token);
        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync(Token);
            Assert.Contains(csrf ? "origin_forbidden" : "csrf_invalid", body);
            Assert.DoesNotContain("Exception", body);
        }
    }

    [Fact]
    public async Task Two_users_receive_only_committed_minimal_lifecycle_notifications()
    {
        var a = await Connect(FleetRoles.Dispatcher);
        var b = await Connect(FleetRoles.Viewer);
        var customer = await Post<CustomerResponse>("customers", new { name = "Live Demo", contactName = "Fictional", email = "live@example.test" });
        await Both("customerCreated", null, a, b);
        var driver = await Post<DriverResponse>("drivers", new { employeeNumber = "LIVE-1", firstName = "Demo", lastName = "Driver", active = true });
        await Both("driverCreated", null, a, b);
        var vehicle = await Post<VehicleResponse>("vehicles", new { plateNumber = "LIVE-1", type = "Truck", maxPayloadKg = 10000, status = "Available" });
        await Both("vehicleCreated", null, a, b);
        var shipment = await Post<ShipmentResponse>("shipments", Input(customer.Id));
        var created = await Both("ShipmentCreated", 1, a, b);
        Assert.Equal(shipment.Id, created.EntityId);
        Assert.Equal(clock.GetUtcNow(), created.TimestampUtc);
        // This read runs in another DbContext after receipt, proving publication follows commit.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
            Assert.Equal(1, await db.Shipments.Where(x => x.Id == created.EntityId).Select(x => x.Version).SingleAsync(Token));
            Assert.Equal(1, await db.ShipmentEvents.CountAsync(x => x.ShipmentId == shipment.Id, Token));
        }
        shipment = await Post<ShipmentResponse>($"shipments/{shipment.Id}/assign", new { version = shipment.Version, driverId = driver.Id, vehicleId = vehicle.Id });
        await Both("ShipmentAssigned", 2, a, b);
        foreach (var status in new[] { "Planned", "InTransit", "Delivered" })
        {
            shipment = await Post<ShipmentResponse>($"shipments/{shipment.Id}/status", new { version = shipment.Version, status, reason = "Live transport" });
            await Both("ShipmentStatusChanged", shipment.Version, a, b);
        }
        var json = JsonSerializer.Serialize(created, Json);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actor", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Updates_cancellation_and_resource_changes_publish_the_correct_kind()
    {
        var live = await Connect();
        var customer = await Post<CustomerResponse>("customers", new { name = "Live Demo", contactName = "Fictional", email = "live@example.test" });
        await Next(live);
        var shipment = await Post<ShipmentResponse>("shipments", Input(customer.Id));
        await Next(live);
        using var update = await writer.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", new
        {
            version = shipment.Version,
            customerId = customer.Id,
            origin = Input(customer.Id).Origin,
            destination = Input(customer.Id).Destination,
            pickupWindowStartUtc = clock.GetUtcNow().AddHours(1),
            deliveryDeadlineUtc = clock.GetUtcNow().AddHours(4),
            weightKg = 1000,
            palletCount = 2,
            cargoDescription = "Updated"
        }, Json, Token);
        Assert.True(update.IsSuccessStatusCode);
        Assert.Equal("ShipmentUpdated", (await Next(live)).EventType);
        shipment = (await update.Content.ReadFromJsonAsync<ShipmentResponse>(Json, Token))!;
        await Post<ShipmentResponse>($"shipments/{shipment.Id}/status", new { version = shipment.Version, status = "Cancelled" });
        Assert.Equal("ShipmentCancelled", (await Next(live)).EventType);
        var driver = await Post<DriverResponse>("drivers", new { employeeNumber = "LIVE-2", firstName = "Demo", lastName = "Driver", active = true });
        await Next(live);
        using var editDriver = await writer.PutAsJsonAsync($"/api/v1/drivers/{driver.Id}", new { employeeNumber = "LIVE-2", firstName = "Demo", lastName = "Driver", active = false }, Token);
        Assert.True(editDriver.IsSuccessStatusCode);
        Assert.Equal("driverUpdated", (await Next(live)).EventType);
        var vehicle = await Post<VehicleResponse>("vehicles", new { plateNumber = "LIVE-2", type = "Van", maxPayloadKg = 2500, status = "Available" });
        await Next(live);
        using var editVehicle = await writer.PutAsJsonAsync($"/api/v1/vehicles/{vehicle.Id}", new { plateNumber = "LIVE-2", type = "Van", maxPayloadKg = 2500, status = "Maintenance" }, Token);
        Assert.True(editVehicle.IsSuccessStatusCode);
        Assert.Equal("vehicleUpdated", (await Next(live)).EventType);
    }

    [Fact]
    public async Task Failed_validation_stale_versions_and_database_rollback_do_not_publish()
    {
        var live = await Connect();
        var customer = await Post<CustomerResponse>("customers", new { name = "Live Demo", contactName = "Fictional", email = "live@example.test" });
        await Next(live);
        var shipment = await Post<ShipmentResponse>("shipments", Input(customer.Id));
        await Next(live);
        using var invalid = await writer.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = 1, status = "Delivered" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        using var stale = await writer.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = 7, status = "Cancelled" }, Token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        rollback.FailNext = true;
        using var failed = await writer.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = 1, status = "Cancelled" }, Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
            Assert.Equal(ShipmentStatus.Draft, (await db.Shipments.SingleAsync(Token)).Status);
            Assert.Equal(1, await db.ShipmentEvents.CountAsync(Token));
        }
        // A subsequent successful marker provides a deterministic ordering barrier, without a sleep.
        await Post<DriverResponse>("drivers", new { employeeNumber = "MARKER", firstName = "Demo", lastName = "Marker", active = true });
        Assert.Equal("driverCreated", (await Next(live)).EventType);
        Assert.False(live.Events.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Delivery_failure_does_not_fail_or_undo_a_committed_http_mutation()
    {
        await using var broken = new FleetFlowApiFactory(connectionString, configureServices: services => services.AddSingleton<ILogisticsNotifier, BrokenNotifier>());
        using var client = await TestAuthentication.ClientAsync(broken);
        using var response = await client.PostAsJsonAsync("/api/v1/customers", new { name = "Committed", contactName = "Demo", email = "saved@example.test" }, Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = broken.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().Customers.AnyAsync(x => x.Name == "Committed", Token));
    }

    [Theory]
    [InlineData("deactivation")]
    [InlineData("logout")]
    [InlineData("expiration")]
    public async Task Established_connections_are_revoked_before_further_delivery(string reason)
    {
        var live = await Connect();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        live.Hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        if (reason == "deactivation")
        {
            using var admin = await TestAuthentication.ClientAsync(factory, FleetRoles.Admin);
            using var response = await admin.PutAsJsonAsync($"/api/v1/users/{live.User.Id}/activation", new { active = false }, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else if (reason == "logout")
        {
            using var response = await live.Client.PostAsync("/api/v1/auth/logout", null, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else clock.Now = clock.Now.AddMinutes(31);
        // A real committed write invokes recipient validation, without trusting an existing socket principal.
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FleetService>().CreateCustomerAsync(
                new("Revocation marker", "Demo", "revocation@example.test", null), Token);
        var recipients = await factory.Services.GetRequiredService<LiveConnections>().ValidConnectionsAsync(Token);
        Assert.Empty(recipients);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        Assert.False(live.Events.Reader.TryRead(out _));
        Assert.Equal(0, factory.Services.GetRequiredService<LiveConnections>().Count);
    }

    [Fact]
    public async Task Disconnect_cleanup_removes_connection_metadata()
    {
        var live = await Connect();
        await live.Hub.StopAsync(Token);
        Assert.Equal(0, factory.Services.GetRequiredService<LiveConnections>().Count);
    }

    private async Task<Live> Connect(string role = FleetRoles.Dispatcher)
    {
        var account = await TestAuthentication.ProvisionAsync(factory, role);
        var capture = new Cookies();
        var client = factory.CreateDefaultClient(capture);
        client.BaseAddress = new Uri("https://localhost");
        clients.Add(client);
        await TestAuthentication.LoginAsync(client, account.User.Email!, account.Password);
        var events = Channel.CreateUnbounded<LogisticsNotification>();
        var hub = new HubConnectionBuilder().WithUrl("https://localhost/hubs/logistics", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers["Cookie"] = capture.Value;
            options.Headers["Origin"] = "https://localhost";
            options.Headers["X-CSRF-TOKEN"] = client.DefaultRequestHeaders.GetValues("X-CSRF-TOKEN").Single();
        }).Build();
        hubs.Add(hub);
        hub.On<LogisticsNotification>("LogisticsChanged", item => events.Writer.TryWrite(item));
        await hub.StartAsync(Token);
        await hub.InvokeAsync("Ready", Token);
        return new(hub, events, client, account.User);
    }
    private async Task<T> Post<T>(string path, object body)
    {
        using var response = await writer.PostAsJsonAsync("/api/v1/" + path, body, Json, Token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }
    private CreateShipmentRequest Input(Guid customer) => new()
    {
        CustomerId = customer,
        Origin = new() { Street = "Depot 1", City = "Graz", Postcode = "8010", CountryCode = "AT" },
        Destination = new() { Street = "Depot 2", City = "Vienna", Postcode = "1010", CountryCode = "AT" },
        PickupWindowStartUtc = clock.Now.AddHours(1),
        DeliveryDeadlineUtc = clock.Now.AddHours(4),
        WeightKg = 1000,
        PalletCount = 2
    };
    private static async Task<LogisticsNotification> Next(Live live) => await live.Events.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
    private static async Task<LogisticsNotification> Both(string type, long? version, params Live[] users)
    {
        var notifications = await Task.WhenAll(users.Select(Next));
        Assert.All(notifications, n => { Assert.Equal(type, n.EventType); Assert.Equal(version, n.Version); });
        Assert.Single(notifications.Select(x => x.EventId).Distinct());
        return notifications[0];
    }
    private sealed record Live(HubConnection Hub, Channel<LogisticsNotification> Events, HttpClient Client, ApplicationUser User);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Cookies : DelegatingHandler
    {
        private readonly CookieContainer cookies = new();
        public string Value => cookies.GetCookieHeader(new Uri("https://localhost"));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", header);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }
    private sealed class BrokenNotifier : ILogisticsNotifier
    {
        public Task PublishAsync(IReadOnlyList<LogisticsNotification> notifications, CancellationToken cancellationToken) => throw new IOException("Synthetic unavailable transport");
    }
    private sealed class RollbackInterceptor : SaveChangesInterceptor
    {
        public bool FailNext { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Synthetic failure before commit"); }
            return ValueTask.FromResult(result);
        }
    }
}
