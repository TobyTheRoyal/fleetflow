using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FleetFlow.Api.Contracts;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class HttpApiTests(PostgreSqlFixture server) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private readonly string databaseName = "m2_" + Guid.NewGuid().ToString("N");
    private readonly TestClock clock = new();
    private readonly SqlRecorder sql = new();
    private readonly CommandGate gate = new();
    private FleetFlowApiFactory factory = null!;
    private HttpClient client = null!;
    private string connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(server.ConnectionString);
        await admin.OpenAsync(Token);
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync(Token);
        connectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;
        factory = NewFactory();
        client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().Database.MigrateAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        await using var admin = new NpgsqlConnection(server.ConnectionString);
        await admin.OpenAsync(CancellationToken.None);
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Complete_lifecycle_through_http_persists_exact_events_and_dashboard_counts()
    {
        var resources = await Resources();
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(resources.Customer.Id), HttpStatusCode.Created);
        Assert.Equal(ShipmentStatus.Draft, shipment.Status);
        Assert.Equal(1, shipment.Version);
        var draftSummary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(1, draftSummary.TotalNonCancelledShipments);
        Assert.Equal(1, draftSummary.DraftShipments);
        Assert.Equal(1, draftSummary.DueSoonShipments);
        using var locationResponse = await client.PostAsJsonAsync("/api/v1/shipments", NewShipment(resources.Customer.Id, "Linz"), Json, Token);
        Assert.Equal(HttpStatusCode.Created, locationResponse.StatusCode);
        Assert.NotNull(locationResponse.Headers.Location);
        using var locationGet = await client.GetAsync(locationResponse.Headers.Location, Token);
        Assert.Equal(HttpStatusCode.OK, locationGet.StatusCode);

        clock.Advance();
        shipment = await Post<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}/assign", new AssignShipmentRequest { Version = shipment.Version, DriverId = resources.Driver.Id, VehicleId = resources.Vehicle.Id });
        foreach (var status in new[] { ShipmentStatus.Planned, ShipmentStatus.InTransit, ShipmentStatus.Delivered })
        {
            clock.Advance();
            shipment = await Post<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = shipment.Version, Status = status, Reason = "Demo operation" });
            Assert.Equal(status, shipment.Status);
        }
        Assert.Equal(5, shipment.Version);
        Assert.NotNull(shipment.ActualPickupAtUtc);
        Assert.NotNull(shipment.ActualDeliveredAtUtc);
        var detail = await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}");
        Assert.Equal(shipment.Version, detail.Version);
        Assert.Equal("Demo Customer", detail.CustomerName);
        Assert.Equal("Demo Driver", detail.AssignedDriverName);
        Assert.Equal(resources.Vehicle.PlateNumber, detail.AssignedVehiclePlateNumber);
        var history = await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events");
        Assert.Equal(5, history.TotalCount);
        Assert.Equal(new[] { ShipmentEventType.Created, ShipmentEventType.AssignmentChanged, ShipmentEventType.StatusChanged, ShipmentEventType.StatusChanged, ShipmentEventType.StatusChanged }, history.Items.Select(x => x.EventType));
        Assert.All(history.Items, e => Assert.Equal("local-dispatcher", e.Actor));
        var summary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(2, summary.TotalNonCancelledShipments);
        Assert.Equal(1, summary.DeliveredShipments);
        Assert.Equal(1, summary.DraftShipments);
        Assert.Equal(0, summary.InTransitShipments);
        Assert.Equal(1, summary.DueSoonShipments);
    }

    [Fact]
    public async Task Invalid_transitions_stale_versions_and_repeated_commands_never_duplicate_history()
    {
        var r = await Resources();
        var shipment = await CreateAssigned(r);
        using (var invalid = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 2, Status = ShipmentStatus.InTransit }, Json, Token))
            await Problem(invalid, 409, "invalid_transition");
        using (var repeated = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/assign", new AssignShipmentRequest { Version = 2, DriverId = r.Driver.Id, VehicleId = r.Vehicle.Id }, Json, Token))
            await Problem(repeated, 409, "assignment_unchanged");
        clock.Advance();
        shipment = await Plan(shipment);
        using (var stale = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 2, Status = ShipmentStatus.InTransit }, Json, Token))
            await Problem(stale, 409, "stale_version");
        using (var repeated = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 3, Status = ShipmentStatus.Planned }, Json, Token))
            await Problem(repeated, 409, "invalid_transition");
        Assert.Equal(3, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
        Assert.Equal(ShipmentStatus.Planned, (await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}")).Status);
    }

    [Theory]
    [InlineData(false, VehicleStatus.Available, 3500, "driver_inactive", 409)]
    [InlineData(true, VehicleStatus.Maintenance, 3500, "vehicle_unavailable", 409)]
    [InlineData(true, VehicleStatus.Inactive, 3500, "vehicle_unavailable", 409)]
    [InlineData(true, VehicleStatus.Available, 999, "vehicle_overloaded", 422)]
    public async Task Invalid_assignment_through_http_preserves_the_draft(bool active, VehicleStatus status, int capacity, string code, int expectedStatus)
    {
        var r = await Resources(active, status, capacity);
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        using var response = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/assign", new AssignShipmentRequest { Version = 1, DriverId = r.Driver.Id, VehicleId = r.Vehicle.Id }, Json, Token);
        await Problem(response, expectedStatus, code);
        var saved = await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}");
        Assert.Null(saved.AssignedVehicleId);
        Assert.Equal(1, saved.Version);
        Assert.Equal(1, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Overlap_and_concurrent_planning_are_enforced_for_each_resource(bool shareDriver)
    {
        var r = await Resources(); var other = await Resources();
        var a = await CreateAssigned(r);
        var b = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        var bAssignment = new AssignShipmentRequest { Version = 1, DriverId = shareDriver ? r.Driver.Id : other.Driver.Id, VehicleId = shareDriver ? other.Vehicle.Id : r.Vehicle.Id };
        b = await Post<ShipmentResponse>($"/api/v1/shipments/{b.Id}/assign", bAssignment);
        gate.Match = "FOR UPDATE";
        async Task<HttpResponseMessage> PlanRequest(ShipmentResponse s) => await client.PostAsJsonAsync($"/api/v1/shipments/{s.Id}/status", new ChangeStatusRequest { Version = s.Version, Status = ShipmentStatus.Planned }, Json, Token);
        var responses = await Task.WhenAll(PlanRequest(a), PlanRequest(b));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            var failure = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            await Problem(failure, 409, "assignment_overlap");
            Assert.Equal(5, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{a.Id}/events")).TotalCount +
                (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{b.Id}/events")).TotalCount);
            var fresh = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
            using var blocked = await client.PostAsJsonAsync($"/api/v1/shipments/{fresh.Id}/assign", bAssignment, Json, Token);
            await Problem(blocked, 409, "assignment_overlap");
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Shipment_put_reuses_domain_rules_and_requires_the_current_version()
    {
        var r = await Resources(); var shipment = await CreateAssigned(r);
        using (var overloaded = await client.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", UpdateRequest(shipment, weight: 4000), Json, Token))
            await Problem(overloaded, 422, "vehicle_overloaded");
        shipment = await Put<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}", UpdateRequest(shipment, weight: 1500.25m));
        Assert.Equal(3, shipment.Version);
        shipment = await Plan(shipment);
        using (var locked = await client.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", UpdateRequest(shipment, weight: 1600), Json, Token))
            await Problem(locked, 409, "fields_locked");
        shipment = await Put<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}", UpdateRequest(shipment, description: "Updated demo cargo"));
        Assert.Equal("Updated demo cargo", shipment.CargoDescription);
        Assert.Equal(5, shipment.Version);
        using var stale = await client.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", UpdateRequest(shipment, version: 4), Json, Token);
        await Problem(stale, 409, "stale_version");
    }

    [Fact]
    public async Task Filters_pagination_literal_search_and_tie_breaking_execute_in_postgres()
    {
        var a = await Resources(); var b = await Resources();
        var one = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(a.Customer.Id), HttpStatusCode.Created);
        var two = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(a.Customer.Id, "Percent%Town"), HttpStatusCode.Created);
        var three = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(b.Customer.Id, "Linz"), HttpStatusCode.Created);
        await Post<ShipmentResponse>($"/api/v1/shipments/{three.Id}/status", new ChangeStatusRequest { Version = 1, Status = ShipmentStatus.Cancelled });
        var first = await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?pageSize=2");
        var second = await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?page=2&pageSize=2");
        Assert.Equal(3, first.TotalCount); Assert.Equal(2, first.TotalPages); Assert.Single(second.Items);
        Assert.Equal(new[] { one.Id, two.Id, three.Id }.OrderByDescending(x => x.ToString(), StringComparer.Ordinal), first.Items.Concat(second.Items).Select(x => x.Id));
        Assert.Equal(2, (await Get<PageResponse<ShipmentResponse>>($"/api/v1/shipments?customerId={a.Customer.Id}&status=Draft")).TotalCount);
        Assert.Equal(1, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?search=gRAz")).TotalCount);
        Assert.Equal(1, (await Get<PageResponse<ShipmentResponse>>($"/api/v1/shipments?search={one.ShipmentNumber}")).TotalCount);
        Assert.Equal(two.Id, Assert.Single((await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?search=%25")).Items).Id);
        Assert.Empty((await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?search=%27%3BDROP%20TABLE")).Items);
        Assert.Empty((await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?page=50")).Items);
        sql.Clear();
        await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?pageSize=2");
        var commands = sql.Commands.ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Contains(commands, query => query.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, query => query.Contains("count(", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Dashboard_uses_sql_counts_and_fixed_time_and_matches_deadline_filters()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync("Development", Token);
        sql.Clear();
        var summary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(2, sql.Commands.Count);
        Assert.Equal(clock.Now, summary.AsOfUtc);
        Assert.Equal(30, summary.TotalNonCancelledShipments);
        Assert.Equal(6, summary.DraftShipments); Assert.Equal(6, summary.PlannedShipments);
        Assert.Equal(6, summary.InTransitShipments); Assert.Equal(12, summary.DeliveredShipments);
        Assert.Equal(9, summary.OverdueShipments); Assert.Equal(9, summary.DueSoonShipments);
        Assert.Equal(10, summary.RecentShipments.Count);
        Assert.Equal(summary.RecentShipments.Select(s => s.Id), (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?pageSize=10")).Items.Select(s => s.Id));
        Assert.All(summary.RecentShipments, s => Assert.NotNull(s.CustomerName));
        Assert.Equal(9, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?deadline=Overdue")).TotalCount);
        Assert.Equal(9, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?deadline=DueSoon")).TotalCount);
        clock.Now = clock.Now.AddDays(2);
        summary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(0, summary.DueSoonShipments); Assert.Equal(18, summary.OverdueShipments);
    }

    [Fact]
    public async Task Empty_dashboard_and_paged_collections_are_valid()
    {
        var summary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(0, summary.TotalNonCancelledShipments); Assert.Empty(summary.RecentShipments);
        foreach (var path in new[] { "shipments", "customers", "drivers", "vehicles" })
        {
            var page = await Get<JsonElement>($"/api/v1/{path}");
            Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
        }
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=1000001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    [InlineData("status=Unknown")]
    [InlineData("status=999")]
    [InlineData("customerId=bad")]
    [InlineData("deadline=Unknown")]
    public async Task Invalid_filters_return_safe_400(string query)
    {
        using var response = await client.GetAsync("/api/v1/shipments?" + query, Token);
        await Problem(response, 400, "invalid_request");
    }

    [Theory]
    [InlineData("zero_weight", "invalid_weight")]
    [InlineData("country", "invalid_country")]
    [InlineData("negative_pallets", "invalid_pallet_count")]
    [InlineData("schedule", "invalid_schedule")]
    public async Task Semantic_shipment_errors_return_422_without_writing_rows(string invalid, string code)
    {
        var r = await Resources();
        var body = JsonSerializer.SerializeToNode(NewShipment(r.Customer.Id), Json)!;
        switch (invalid)
        {
            case "zero_weight": body["weightKg"] = 0; break;
            case "country": body["origin"]!["countryCode"] = "ZZ"; break;
            case "negative_pallets": body["palletCount"] = -1; break;
            case "schedule": body["deliveryDeadlineUtc"] = body["pickupWindowStartUtc"]!.DeepClone(); break;
        }
        using var response = await client.PostAsJsonAsync("/api/v1/shipments", body, Json, Token);
        await Problem(response, 422, code);
        Assert.Equal(0, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments")).TotalCount);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().ShipmentEvents.CountAsync(Token));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{not-json}")]
    [InlineData("null")]
    public async Task Malformed_bodies_are_400_and_never_reflect_internal_details(string body)
    {
        using var response = await client.PostAsync("/api/v1/shipments", new StringContent(body, Encoding.UTF8, "application/json"), Token);
        await Problem(response, 400, "invalid_request");
    }

    [Fact]
    public async Task Version_is_required_and_server_owned_fields_are_rejected()
    {
        var r = await Resources();
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        foreach (var body in new[] { new JsonObject { ["status"] = "Cancelled" }, new JsonObject { ["version"] = 1, ["status"] = 999 }, new JsonObject { ["version"] = 0, ["status"] = "Cancelled" }, new JsonObject { ["version"] = 1, ["status"] = "Cancelled", ["actor"] = "forged" } })
        {
            using var response = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", body, Json, Token);
            await Problem(response, 400, "invalid_request");
        }
        var edit = JsonSerializer.SerializeToNode(UpdateRequest(shipment), Json)!;
        edit["status"] = "Delivered";
        using (var overpost = await client.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", edit, Json, Token))
            await Problem(overpost, 400, "invalid_request");
        var create = JsonSerializer.SerializeToNode(NewShipment(r.Customer.Id), Json)!;
        create["pickupWindowStartUtc"] = "2026-11-06T12:00:00";
        using (var timestamp = await client.PostAsJsonAsync("/api/v1/shipments", create, Json, Token))
            await Problem(timestamp, 400, "invalid_request");
        Assert.Equal(1, (await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}")).Version);
    }

    [Fact]
    public async Task Missing_resources_are_404_and_bad_route_ids_are_400()
    {
        foreach (var path in new[] { $"shipments/{Guid.NewGuid()}", $"shipments/{Guid.NewGuid()}/events" })
        {
            using var missing = await client.GetAsync("/api/v1/" + path, Token);
            await Problem(missing, 404, "not_found");
        }
        using var malformed = await client.GetAsync("/api/v1/shipments/not-a-guid", Token);
        await Problem(malformed, 400, "invalid_request");
        using var missingCustomer = await client.PostAsJsonAsync("/api/v1/shipments", NewShipment(Guid.NewGuid()), Json, Token);
        await Problem(missingCustomer, 404, "not_found");
    }

    [Fact]
    public async Task Master_data_create_update_lists_and_unique_conflicts_are_supported()
    {
        var r = await Resources();
        Assert.Single((await Get<PageResponse<CustomerResponse>>("/api/v1/customers")).Items);
        var driver = await Put<DriverResponse>($"/api/v1/drivers/{r.Driver.Id}", new DriverRequest { EmployeeNumber = r.Driver.EmployeeNumber.ToLowerInvariant(), FirstName = "Updated", LastName = "Driver", Active = false, LicenseCategory = "B" });
        Assert.False(driver.Active); Assert.Equal(r.Driver.CreatedAtUtc, driver.CreatedAtUtc);
        var vehicle = await Put<VehicleResponse>($"/api/v1/vehicles/{r.Vehicle.Id}", new VehicleRequest { PlateNumber = r.Vehicle.PlateNumber.ToLowerInvariant(), Type = VehicleType.Truck, MaxPayloadKg = 12000, Status = VehicleStatus.Maintenance });
        Assert.Equal(VehicleStatus.Maintenance, vehicle.Status);
        Assert.Equal(r.Vehicle.PlateNumber, vehicle.PlateNumber);
        using (var duplicate = await client.PostAsJsonAsync("/api/v1/vehicles", new VehicleRequest { PlateNumber = r.Vehicle.PlateNumber.ToLowerInvariant(), Type = VehicleType.Van, MaxPayloadKg = 1000, Status = VehicleStatus.Available }, Json, Token))
            await Problem(duplicate, 409, "duplicate_value");
        using (var duplicate = await client.PostAsJsonAsync("/api/v1/drivers", new DriverRequest { EmployeeNumber = r.Driver.EmployeeNumber.ToLowerInvariant(), FirstName = "Demo", LastName = "Other", Active = true }, Json, Token))
            await Problem(duplicate, 409, "duplicate_value");
        using (var invalidEmail = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequest { Name = "Demo", ContactName = "Demo", Email = "invalid email" }, Json, Token))
            await Problem(invalidEmail, 422, "invalid_email");
        using (var missing = await client.PutAsJsonAsync($"/api/v1/vehicles/{Guid.NewGuid()}", new VehicleRequest { PlateNumber = "FF-MISSING", Type = VehicleType.Van, MaxPayloadKg = 1000, Status = VehicleStatus.Available }, Json, Token))
            await Problem(missing, 404, "not_found");
        Assert.Single((await Get<PageResponse<DriverResponse>>("/api/v1/drivers")).Items);
        Assert.Single((await Get<PageResponse<VehicleResponse>>("/api/v1/vehicles")).Items);
    }

    [Theory]
    [InlineData("driver")]
    [InlineData("status")]
    [InlineData("capacity")]
    public async Task Fleet_updates_cannot_invalidate_active_assignments(string invalid)
    {
        var r = await Resources(); var shipment = await Plan(await CreateAssigned(r));
        using var response = invalid == "driver"
            ? await client.PutAsJsonAsync($"/api/v1/drivers/{r.Driver.Id}", new DriverRequest { EmployeeNumber = r.Driver.EmployeeNumber, FirstName = "Demo", LastName = "Driver", Active = false }, Json, Token)
            : await client.PutAsJsonAsync($"/api/v1/vehicles/{r.Vehicle.Id}", new VehicleRequest { PlateNumber = r.Vehicle.PlateNumber, Type = r.Vehicle.Type, MaxPayloadKg = invalid == "capacity" ? 999 : 3500, Status = invalid == "status" ? VehicleStatus.Maintenance : VehicleStatus.Available }, Json, Token);
        await Problem(response, 409, "resource_in_use");
        Assert.True(Assert.Single((await Get<PageResponse<DriverResponse>>("/api/v1/drivers")).Items).Active);
        Assert.Equal(3500, Assert.Single((await Get<PageResponse<VehicleResponse>>("/api/v1/vehicles")).Items).MaxPayloadKg);
        Assert.Equal(3, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Fact]
    public async Task Failure_during_event_storage_rolls_back_http_status_operation()
    {
        var r = await Resources(); var shipment = await CreateAssigned(r);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_http_event() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                IF NEW."EventType" = 'StatusChanged' THEN RAISE EXCEPTION 'sensitive SQL fault'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER fail_http_event BEFORE INSERT ON "ShipmentEvents" FOR EACH ROW EXECUTE FUNCTION fail_http_event();
                """, Token);
        }
        using var response = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 2, Status = ShipmentStatus.Planned }, Json, Token);
        await Problem(response, 500, "unexpected_error");
        Assert.DoesNotContain("sensitive SQL fault", await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(ShipmentStatus.Draft, (await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}")).Status);
        Assert.Equal(2, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Fact]
    public async Task Restarting_the_api_host_preserves_database_state()
    {
        var r = await Resources(); var shipment = await CreateAssigned(r);
        client.Dispose(); await factory.DisposeAsync();
        factory = NewFactory(); client = factory.CreateClient();
        var saved = await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}");
        Assert.Equal(shipment.ShipmentNumber, saved.ShipmentNumber);
        Assert.Equal(2, saved.Version);
        Assert.Equal(2, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Fact]
    public async Task Openapi_documents_all_routes_dtos_statuses_and_executable_request_examples()
    {
        var document = await Get<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        foreach (var path in new[] { "/api/v1/dashboard/summary", "/api/v1/shipments", "/api/v1/shipments/{id}", "/api/v1/shipments/{id}/assign", "/api/v1/shipments/{id}/status", "/api/v1/shipments/{id}/events", "/api/v1/customers", "/api/v1/drivers", "/api/v1/drivers/{id}", "/api/v1/vehicles", "/api/v1/vehicles/{id}" })
            Assert.True(paths.TryGetProperty(path, out _), path);
        var queryNames = paths.GetProperty("/api/v1/shipments").GetProperty("get").GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToHashSet();
        Assert.True(queryNames.SetEquals(new[] { "page", "pageSize", "status", "customerId", "search", "deadline" }));
        var post = paths.GetProperty("/api/v1/shipments").GetProperty("post");
        foreach (var status in new[] { "201", "400", "404", "409", "422", "500" }) Assert.True(post.GetProperty("responses").TryGetProperty(status, out _));
        foreach (var status in new[] { "400", "404", "409", "422", "500" })
            Assert.True(post.GetProperty("responses").GetProperty(status).GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.True(post.GetProperty("responses").GetProperty("400").GetProperty("content").GetProperty("application/problem+json").GetProperty("example").TryGetProperty("errors", out _));
        var request = JsonNode.Parse(post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example").GetRawText())!;
        var r = await Resources(); request["customerId"] = r.Customer.Id.ToString();
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", request, HttpStatusCode.Created);
        Assert.Equal(1200.50m, shipment.WeightKg);
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("CreateShipmentRequest", out var createSchema));
        // JSON Schema can express string-only values with enum alone; an explicit type is optional.
        Assert.Equal(new[] { "Draft", "Planned", "InTransit", "Delivered", "Cancelled" }, schemas.GetProperty("ShipmentStatus").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(new[] { "Van", "Truck" }, schemas.GetProperty("VehicleType").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("date-time", createSchema.GetProperty("properties").GetProperty("pickupWindowStartUtc").GetProperty("format").GetString());
        Assert.DoesNotContain(schemas.EnumerateObject(), item => item.Name == "FleetFlowDbContext" || item.Name == "Shipment");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fleet_eligibility_updates_and_planning_share_the_same_database_locks(bool driverChange)
    {
        var r = await Resources();
        var shipment = await CreateAssigned(r);
        gate.Match = "FOR UPDATE";
        var planTask = client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 2, Status = ShipmentStatus.Planned }, Json, Token);
        var updateTask = driverChange
            ? client.PutAsJsonAsync($"/api/v1/drivers/{r.Driver.Id}", new DriverRequest { EmployeeNumber = r.Driver.EmployeeNumber, FirstName = "Demo", LastName = "Driver", Active = false }, Json, Token)
            : client.PutAsJsonAsync($"/api/v1/vehicles/{r.Vehicle.Id}", new VehicleRequest { PlateNumber = r.Vehicle.PlateNumber, Type = r.Vehicle.Type, MaxPayloadKg = 3500, Status = VehicleStatus.Maintenance }, Json, Token);
        var responses = await Task.WhenAll(planTask, updateTask);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var saved = await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}");
            if (saved.Status == ShipmentStatus.Planned)
            {
                Assert.True(Assert.Single((await Get<PageResponse<DriverResponse>>("/api/v1/drivers")).Items).Active);
                Assert.Equal(VehicleStatus.Available, Assert.Single((await Get<PageResponse<VehicleResponse>>("/api/v1/vehicles")).Items).Status);
            }
            else
            {
                Assert.Equal(ShipmentStatus.Draft, saved.Status);
                Assert.Equal(2, saved.Version);
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Planning_requires_assignment_and_delivered_is_terminal_over_http()
    {
        var r = await Resources();
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        using (var unassigned = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 1, Status = ShipmentStatus.Planned }, Json, Token))
            await Problem(unassigned, 409, "assignment_required");
        shipment = await Post<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}/assign", new AssignShipmentRequest { Version = 1, DriverId = r.Driver.Id, VehicleId = r.Vehicle.Id });
        shipment = await Plan(shipment);
        foreach (var status in new[] { ShipmentStatus.InTransit, ShipmentStatus.Delivered })
        {
            clock.Advance();
            shipment = await Post<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = shipment.Version, Status = status });
        }
        using var backwards = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new ChangeStatusRequest { Version = 5, Status = ShipmentStatus.Planned }, Json, Token);
        await Problem(backwards, 409, "invalid_transition");
        Assert.Equal(5, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Fact]
    public async Task Dashboard_and_deadline_filters_include_exact_time_boundaries()
    {
        var r = await Resources();
        foreach (var deadline in new[] { clock.Now.AddMinutes(-1), clock.Now, clock.Now.AddHours(24), clock.Now.AddHours(24).AddMinutes(1) })
        {
            var body = JsonSerializer.SerializeToNode(NewShipment(r.Customer.Id), Json)!;
            body["pickupWindowStartUtc"] = clock.Now.AddHours(-2).ToString("O");
            body["deliveryDeadlineUtc"] = deadline.ToString("O");
            await Post<ShipmentResponse>("/api/v1/shipments", body, HttpStatusCode.Created);
        }
        var summary = await Get<DashboardResponse>("/api/v1/dashboard/summary");
        Assert.Equal(4, summary.TotalNonCancelledShipments);
        Assert.Equal(1, summary.OverdueShipments);
        Assert.Equal(2, summary.DueSoonShipments);
        Assert.Equal(1, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?deadline=Overdue")).TotalCount);
        Assert.Equal(2, (await Get<PageResponse<ShipmentResponse>>("/api/v1/shipments?deadline=DueSoon")).TotalCount);
    }

    [Fact]
    public async Task History_is_paged_chronologically_and_cannot_be_changed_over_http()
    {
        var r = await Resources(); var shipment = await CreateAssigned(r);
        clock.Advance(); shipment = await Plan(shipment);
        var first = await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events?pageSize=2");
        var second = await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events?pageSize=2&page=2");
        Assert.Equal(3, first.TotalCount); Assert.Equal(2, first.TotalPages);
        Assert.Equal(ShipmentEventType.Created, first.Items[0].EventType);
        Assert.Equal(ShipmentEventType.AssignmentChanged, first.Items[1].EventType);
        Assert.Equal(ShipmentEventType.StatusChanged, Assert.Single(second.Items).EventType);
        using var remove = await client.DeleteAsync($"/api/v1/shipments/{shipment.Id}/events", Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, remove.StatusCode);
        Assert.Equal(3, (await Get<PageResponse<ShipmentEventResponse>>($"/api/v1/shipments/{shipment.Id}/events")).TotalCount);
    }

    [Fact]
    public async Task Every_shipment_mutation_requires_version_and_timestamps_are_normalized()
    {
        var r = await Resources();
        var body = JsonSerializer.SerializeToNode(NewShipment(r.Customer.Id), Json)!;
        body["pickupWindowStartUtc"] = "2026-11-06T15:00:00+02:00";
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", body, HttpStatusCode.Created);
        Assert.Equal(TimeSpan.Zero, shipment.PickupWindowStartUtc.Offset);
        Assert.Equal(clock.Now.AddHours(1), shipment.PickupWindowStartUtc);
        using (var assignment = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/assign", new { driverId = r.Driver.Id, vehicleId = r.Vehicle.Id }, Json, Token))
            await Problem(assignment, 400, "invalid_request");
        var edit = JsonSerializer.SerializeToNode(UpdateRequest(shipment), Json)!;
        edit.AsObject().Remove("version");
        using var update = await client.PutAsJsonAsync($"/api/v1/shipments/{shipment.Id}", edit, Json, Token);
        await Problem(update, 400, "invalid_request");
    }

    [Fact]
    public async Task Invalid_vehicle_capacity_stays_422_even_when_the_vehicle_is_reserved()
    {
        var r = await Resources(); await Plan(await CreateAssigned(r));
        using var response = await client.PutAsJsonAsync($"/api/v1/vehicles/{r.Vehicle.Id}", new VehicleRequest { PlateNumber = r.Vehicle.PlateNumber, Type = r.Vehicle.Type, MaxPayloadKg = -1, Status = VehicleStatus.Available }, Json, Token);
        await Problem(response, 422, "invalid_weight");
        Assert.Equal(3500, Assert.Single((await Get<PageResponse<VehicleResponse>>("/api/v1/vehicles")).Items).MaxPayloadKg);
    }

    [Fact]
    public async Task Json_numeric_values_cannot_be_sent_as_strings()
    {
        var r = await Resources();
        var create = JsonSerializer.SerializeToNode(NewShipment(r.Customer.Id), Json)!;
        create["weightKg"] = "1200.50";
        using (var invalidWeight = await client.PostAsJsonAsync("/api/v1/shipments", create, Json, Token))
            await Problem(invalidWeight, 400, "invalid_request");
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        using var invalidVersion = await client.PostAsJsonAsync($"/api/v1/shipments/{shipment.Id}/status", new { version = "1", status = "Cancelled" }, Json, Token);
        await Problem(invalidVersion, 400, "invalid_request");
        Assert.Equal(1, (await Get<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}")).Version);
    }

    private FleetFlowApiFactory NewFactory() => new(connectionString, clock: clock, configureServices: services => services.AddDbContext<FleetFlowDbContext>(options => options.AddInterceptors(gate, sql)));
    private async Task<(CustomerResponse Customer, DriverResponse Driver, VehicleResponse Vehicle)> Resources(bool active = true, VehicleStatus status = VehicleStatus.Available, int capacity = 3500)
    {
        var customer = await Post<CustomerResponse>("/api/v1/customers", new CreateCustomerRequest { Name = "Demo Customer", ContactName = "Demo Contact", Email = "demo@fleetflow.example" }, HttpStatusCode.Created);
        var driver = await Post<DriverResponse>("/api/v1/drivers", new DriverRequest { EmployeeNumber = "D-" + Guid.NewGuid().ToString("N"), FirstName = "Demo", LastName = "Driver", Active = active }, HttpStatusCode.Created);
        var vehicle = await Post<VehicleResponse>("/api/v1/vehicles", new VehicleRequest { PlateNumber = "FF-" + Guid.NewGuid().ToString("N")[..12], Type = VehicleType.Van, MaxPayloadKg = capacity, Status = status }, HttpStatusCode.Created);
        return (customer, driver, vehicle);
    }
    private CreateShipmentRequest NewShipment(Guid customer, string city = "Graz") => new()
    {
        CustomerId = customer,
        Origin = new AddressRequest { Street = "Demo road 1", City = city, Postcode = "8010", CountryCode = "AT" },
        Destination = new AddressRequest { Street = "Demo road 2", City = "Vienna", Postcode = "1010", CountryCode = "AT" },
        PickupWindowStartUtc = clock.Now.AddHours(1),
        DeliveryDeadlineUtc = clock.Now.AddHours(5),
        WeightKg = 1000,
        PalletCount = 2,
        CargoDescription = "Demo pallets"
    };
    private static UpdateShipmentRequest UpdateRequest(ShipmentResponse s, decimal? weight = null, string? description = null, long? version = null) => new()
    {
        CustomerId = s.CustomerId,
        Origin = new AddressRequest { Street = s.Origin.Street, City = s.Origin.City, Postcode = s.Origin.Postcode, CountryCode = s.Origin.CountryCode },
        Destination = new AddressRequest { Street = s.Destination.Street, City = s.Destination.City, Postcode = s.Destination.Postcode, CountryCode = s.Destination.CountryCode },
        PickupWindowStartUtc = s.PickupWindowStartUtc,
        DeliveryDeadlineUtc = s.DeliveryDeadlineUtc,
        WeightKg = weight ?? s.WeightKg,
        PalletCount = s.PalletCount,
        CargoDescription = description ?? s.CargoDescription,
        Version = version ?? s.Version
    };
    private async Task<ShipmentResponse> CreateAssigned((CustomerResponse Customer, DriverResponse Driver, VehicleResponse Vehicle) r)
    {
        var shipment = await Post<ShipmentResponse>("/api/v1/shipments", NewShipment(r.Customer.Id), HttpStatusCode.Created);
        clock.Advance();
        return await Post<ShipmentResponse>($"/api/v1/shipments/{shipment.Id}/assign", new AssignShipmentRequest { Version = 1, DriverId = r.Driver.Id, VehicleId = r.Vehicle.Id });
    }
    private Task<ShipmentResponse> Plan(ShipmentResponse s) => Post<ShipmentResponse>($"/api/v1/shipments/{s.Id}/status", new ChangeStatusRequest { Version = s.Version, Status = ShipmentStatus.Planned });
    private async Task<T> Get<T>(string path) { using var response = await client.GetAsync(path, Token); return await Success<T>(response, HttpStatusCode.OK); }
    private async Task<T> Post<T>(string path, object body, HttpStatusCode status = HttpStatusCode.OK) { using var response = await client.PostAsJsonAsync(path, body, Json, Token); return await Success<T>(response, status); }
    private async Task<T> Put<T>(string path, object body) { using var response = await client.PutAsJsonAsync(path, body, Json, Token); return await Success<T>(response, HttpStatusCode.OK); }
    private static async Task<T> Success<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}");
        return (await response.Content.ReadFromJsonAsync<T>(Json, Token))!;
    }
    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(Token);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance() => Now = Now.AddMinutes(1);
    }
    private sealed class SqlRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public void Clear() => Commands.Clear();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }
}
