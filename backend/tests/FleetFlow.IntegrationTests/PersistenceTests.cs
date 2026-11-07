using System.Data;
using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class PersistenceTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly string databaseName = "m1_" + Guid.NewGuid().ToString("N");
    private readonly FixedClock clock = new();
    private readonly CommandGate gate = new();
    private ServiceProvider services = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private ShipmentDetails Details(Guid customer) => new(customer, Address.Create("Demo road 1", "Graz", "8010", "AT"),
        Address.Create("Demo road 2", "Vienna", "1010", "AT"), clock.GetUtcNow().AddHours(1), clock.GetUtcNow().AddHours(5), 1000m, 2, "Demo pallets");

    public async ValueTask InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync(Token);
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync(Token);
        var connection = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = databaseName }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:FleetFlow"] = connection }).Build();
        var registry = new ServiceCollection();
        registry.AddLogging();
        registry.AddInfrastructure(configuration);
        registry.AddSingleton<TimeProvider>(clock);
        registry.AddDbContext<FleetFlowDbContext>(options => options.AddInterceptors(gate));
        services = registry.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await WithDb(async db => { await db.Database.MigrateAsync(Token); return 0; });
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await using var admin = new NpgsqlConnection(postgres.ConnectionString);
        await admin.OpenAsync(CancellationToken.None);
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Migration_is_repeatable_and_schema_matches_the_model()
    {
        await WithDb(async db =>
        {
            Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync(Token)).Count());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.MigrateAsync(Token);
            Assert.Empty(await db.Shipments.ToListAsync(Token));
            await db.GetService<IMigrator>().MigrateAsync("0", Token);
            Assert.Empty(await db.Database.GetAppliedMigrationsAsync(Token));
            await db.Database.MigrateAsync(Token);
            Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync(Token)).Count());
            return 0;
        });
    }

    [Fact]
    public async Task Happy_path_persists_timestamps_versions_and_exact_history()
    {
        var data = await Basics();
        var created = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        var assigned = await Execute(service => service.AssignAsync(created.Id, created.Version, data.Driver.Id, data.Vehicle.Id, "demo", Token));
        var planned = await Execute(service => service.ChangeStatusAsync(created.Id, assigned.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
        clock.Now = clock.Now.AddHours(1);
        var transit = await Execute(service => service.ChangeStatusAsync(created.Id, planned.Version, ShipmentStatus.InTransit, "demo", cancellationToken: Token));
        clock.Now = clock.Now.AddHours(2);
        var delivered = await Execute(service => service.ChangeStatusAsync(created.Id, transit.Version, ShipmentStatus.Delivered, "demo", cancellationToken: Token));
        var saved = await Load(created.Id);
        Assert.Equal(ShipmentStatus.Delivered, saved.Status);
        Assert.Equal(5, saved.Version);
        Assert.Equal(transit.ActualPickupAtUtc, saved.ActualPickupAtUtc);
        Assert.Equal(clock.Now, saved.ActualDeliveredAtUtc);
        Assert.Equal(TimeSpan.Zero, saved.CreatedAtUtc.Offset);
        Assert.Equal(Details(data.Customer.Id).Origin, saved.Origin);
        var events = await Events(created.Id);
        Assert.Equal(5, events.Count);
        Assert.Equal(3, events.Count(e => e.EventType == ShipmentEventType.StatusChanged));
        Assert.All(events, e => Assert.Equal("demo", e.Actor));
        Assert.Equal(delivered.Id, saved.Id);
    }

    [Fact]
    public async Task Invalid_transition_and_stale_request_leave_no_history_or_state_changes()
    {
        var data = await Basics();
        var created = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        var invalid = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.ChangeStatusAsync(created.Id, 1, ShipmentStatus.Delivered, "demo", cancellationToken: Token)));
        Assert.Equal("invalid_transition", invalid.Code);
        await Execute(service => service.ChangeStatusAsync(created.Id, 1, ShipmentStatus.Cancelled, "demo", cancellationToken: Token));
        var stale = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.ChangeStatusAsync(created.Id, 1, ShipmentStatus.Cancelled, "demo", cancellationToken: Token)));
        Assert.Equal("stale_version", stale.Code);
        Assert.Equal(2, (await Events(created.Id)).Count);
        Assert.Equal(2, (await Load(created.Id)).Version);
    }

    [Theory]
    [InlineData(false, VehicleStatus.Available, 3500, "driver_inactive")]
    [InlineData(true, VehicleStatus.Maintenance, 3500, "vehicle_unavailable")]
    [InlineData(true, VehicleStatus.Inactive, 3500, "vehicle_unavailable")]
    [InlineData(true, VehicleStatus.Available, 500, "vehicle_overloaded")]
    public async Task Invalid_assignment_is_rejected_by_the_backend(bool active, VehicleStatus status, int capacity, string code)
    {
        var data = await Basics(active, status, capacity);
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        var exception = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.AssignAsync(shipment.Id, 1, data.Driver.Id, data.Vehicle.Id, "demo", Token)));
        Assert.Equal(code, exception.Code);
        Assert.Null((await Load(shipment.Id)).AssignedVehicleId);
        Assert.Single(await Events(shipment.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Overlap_blocks_either_shared_driver_or_shared_vehicle(bool shareDriver)
    {
        var first = await Basics();
        var other = await Basics();
        var a = await CreateAndAssign(first.Customer.Id, first.Driver.Id, first.Vehicle.Id);
        await Execute(service => service.ChangeStatusAsync(a.Id, a.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
        var b = await Execute(service => service.CreateAsync(Details(first.Customer.Id), "demo", Token));
        var exception = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.AssignAsync(b.Id, b.Version,
            shareDriver ? first.Driver.Id : other.Driver.Id, shareDriver ? other.Vehicle.Id : first.Vehicle.Id, "demo", Token)));
        Assert.Equal("assignment_overlap", exception.Code);
        Assert.Single(await Events(b.Id));
    }

    [Fact]
    public async Task Adjacent_intervals_are_allowed_and_cancelled_reservations_are_released()
    {
        var data = await Basics();
        var a = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        var planned = await Execute(service => service.ChangeStatusAsync(a.Id, a.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
        var adjacent = Details(data.Customer.Id) with { PickupWindowStartUtc = a.Details.DeliveryDeadlineUtc, DeliveryDeadlineUtc = a.Details.DeliveryDeadlineUtc.AddHours(3) };
        var b = await Execute(service => service.CreateAsync(adjacent, "demo", Token));
        await Execute(service => service.AssignAsync(b.Id, b.Version, data.Driver.Id, data.Vehicle.Id, "demo", Token));
        await Execute(service => service.ChangeStatusAsync(a.Id, planned.Version, ShipmentStatus.Cancelled, "demo", cancellationToken: Token));
        var c = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        await Execute(service => service.ChangeStatusAsync(c.Id, c.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Draft_selection_is_revalidated_when_planning_concurrently(bool shareDriver)
    {
        var data = await Basics();
        var other = await Basics();
        var a = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        var b = await CreateAndAssign(data.Customer.Id, shareDriver ? data.Driver.Id : other.Driver.Id,
            shareDriver ? other.Vehicle.Id : data.Vehicle.Id);
        gate.Match = "FOR UPDATE";
        async Task<string> Plan(ShipmentSnapshot shipment)
        {
            try { await Execute(service => service.ChangeStatusAsync(shipment.Id, shipment.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token)); return "ok"; }
            catch (FleetFlowException exception) { return exception.Code; }
        }
        var outcomes = await Task.WhenAll(Plan(a), Plan(b));
        Assert.Single(outcomes, value => value == "ok");
        Assert.Single(outcomes, value => value == "assignment_overlap");
        Assert.Equal(5, (await Events(a.Id)).Count + (await Events(b.Id)).Count);
    }

    [Fact]
    public async Task Concurrent_edits_of_one_shipment_return_a_translated_stale_conflict()
    {
        var data = await Basics();
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        gate.Match = "FROM \"Shipments\"";
        async Task<string> Cancel()
        {
            try { await Execute(service => service.ChangeStatusAsync(shipment.Id, 1, ShipmentStatus.Cancelled, "demo", cancellationToken: Token)); return "ok"; }
            catch (FleetFlowException exception) { return exception.Code; }
        }
        var outcomes = await Task.WhenAll(Cancel(), Cancel());
        Assert.Single(outcomes, value => value == "ok");
        Assert.Single(outcomes, value => value == "stale_version");
        Assert.Equal(2, (await Load(shipment.Id)).Version);
        Assert.Equal(2, (await Events(shipment.Id)).Count);
    }

    [Fact]
    public async Task Event_insert_failure_rolls_back_status_and_allows_clean_retry_in_the_same_scope()
    {
        var data = await Basics();
        var shipment = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        await WithDb(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_status_event() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW."EventType" = 'StatusChanged' THEN RAISE EXCEPTION 'Simulated event storage failure'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER fail_status_event BEFORE INSERT ON "ShipmentEvents" FOR EACH ROW EXECUTE FUNCTION fail_status_event();
                """, Token);
            return 0;
        });
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ShipmentService>();
        await Assert.ThrowsAsync<DbUpdateException>(() => service.ChangeStatusAsync(shipment.Id, shipment.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
        Assert.Equal(ShipmentStatus.Draft, (await Load(shipment.Id)).Status);
        Assert.Equal(shipment.Version, (await Load(shipment.Id)).Version);
        Assert.Equal(2, (await Events(shipment.Id)).Count);
        await WithDb(async db => { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_status_event ON \"ShipmentEvents\"; DROP FUNCTION fail_status_event();", Token); return 0; });
        await service.ChangeStatusAsync(shipment.Id, shipment.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token);
        Assert.Equal(3, (await Events(shipment.Id)).Count);
    }

    [Theory]
    [InlineData("CK_Shipments_Weight")]
    [InlineData("CK_Shipments_Pallets")]
    [InlineData("CK_Shipments_Schedule")]
    [InlineData("CK_Shipments_Origin")]
    [InlineData("CK_Shipments_Version")]
    [InlineData("CK_Shipments_Status")]
    [InlineData("CK_Shipments_Assignment")]
    public async Task Check_constraints_reject_invalid_database_writes(string constraint)
    {
        var data = await Basics();
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        await WithDb(async db =>
        {
            var sql = constraint switch
            {
                "CK_Shipments_Weight" => "UPDATE \"Shipments\" SET \"WeightKg\" = 0 WHERE \"Id\" = {0}",
                "CK_Shipments_Pallets" => "UPDATE \"Shipments\" SET \"PalletCount\" = -1 WHERE \"Id\" = {0}",
                "CK_Shipments_Schedule" => "UPDATE \"Shipments\" SET \"DeliveryDeadlineUtc\" = \"PickupWindowStartUtc\" WHERE \"Id\" = {0}",
                "CK_Shipments_Origin" => "UPDATE \"Shipments\" SET \"OriginCountryCode\" = 'ZZ' WHERE \"Id\" = {0}",
                "CK_Shipments_Version" => "UPDATE \"Shipments\" SET \"Version\" = 0 WHERE \"Id\" = {0}",
                "CK_Shipments_Status" => "UPDATE \"Shipments\" SET \"Status\" = 'Unknown' WHERE \"Id\" = {0}",
                "CK_Shipments_Assignment" => "UPDATE \"Shipments\" SET \"AssignedDriverId\" = gen_random_uuid() WHERE \"Id\" = {0}",
                _ => throw new InvalidOperationException("Unknown constraint test")
            };
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, [shipment.Id], Token));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal(constraint, error.ConstraintName);
            return 0;
        });
    }

    [Fact]
    public async Task Foreign_keys_unique_values_and_immutable_history_are_enforced()
    {
        var data = await Basics();
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        await WithDb(async db =>
        {
            var fk = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Shipments\" SET \"CustomerId\" = gen_random_uuid() WHERE \"Id\" = {0}", [shipment.Id], Token));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fk.SqlState);
            var duplicate = Vehicle.Create(data.Vehicle.PlateNumber.ToLowerInvariant(), VehicleType.Truck, 12000, VehicleStatus.Available, clock.Now);
            db.Vehicles.Add(duplicate);
            var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(unique.InnerException).SqlState);
            db.ChangeTracker.Clear();
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"ShipmentEvents\" SET \"Actor\" = 'tampered' WHERE \"ShipmentId\" = {0}", [shipment.Id], Token));
            Assert.Equal(PostgresErrorCodes.CheckViolation, immutable.SqlState);
            return 0;
        });
    }

    [Fact]
    public async Task Sequence_numbers_are_unique_under_concurrent_creation()
    {
        var data = await Basics();
        var shipments = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token))));
        Assert.Equal(12, shipments.Select(x => x.ShipmentNumber).Distinct().Count());
        Assert.All(shipments, shipment => Assert.StartsWith("FF-2026-", shipment.ShipmentNumber));
        Assert.Equal(12, await WithDb(db => db.ShipmentEvents.CountAsync(Token)));
    }

    [Fact]
    public async Task Missing_customer_and_invalid_data_do_not_create_shipments()
    {
        var missing = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.CreateAsync(Details(Guid.NewGuid()), "demo", Token)));
        Assert.Equal(FailureKind.NotFound, missing.Kind);
        var data = await Basics();
        await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.CreateAsync(Details(data.Customer.Id) with { WeightKg = -1 }, "demo", Token)));
        Assert.Equal(0, await WithDb(db => db.Shipments.CountAsync(Token)));
        Assert.Equal(0, await WithDb(db => db.ShipmentEvents.CountAsync(Token)));
    }

    [Fact]
    public async Task Seed_is_development_only_idempotent_and_has_valid_fictional_data()
    {
        await using var scope = services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync("Production", Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DevelopmentDatabase.InitializeAsync(services, "Production", true, true, Token));
        await seeder.SeedAsync("Development", Token);
        var before = await WithDb(db => db.ShipmentEvents.CountAsync(Token));
        var original = await WithDb(db => db.Shipments.AsNoTracking().OrderBy(x => x.ShipmentNumber).ToListAsync(Token));
        clock.Now = clock.Now.AddDays(2);
        await seeder.SeedAsync("Development", Token);
        Assert.Equal(before, await WithDb(db => db.ShipmentEvents.CountAsync(Token)));
        Assert.Equal(10, await WithDb(db => db.Customers.CountAsync(Token)));
        Assert.Equal(12, await WithDb(db => db.Drivers.CountAsync(Token)));
        Assert.Equal(8, await WithDb(db => db.Vehicles.CountAsync(Token)));
        Assert.Equal(36, original.Count);
        Assert.True(original.Count(x => x.IsOverdue(clock.Now.AddDays(-2))) >= 3);
        Assert.True(original.Count(x => x.IsDueSoon(clock.Now.AddDays(-2))) >= 4);
        Assert.Equal(5, original.Select(x => x.Status).Distinct().Count());
        var reloaded = await WithDb(db => db.Shipments.AsNoTracking().OrderBy(x => x.ShipmentNumber).ToListAsync(Token));
        Assert.Equal(original.Select(x => x.DeliveryDeadlineUtc), reloaded.Select(x => x.DeliveryDeadlineUtc));
        Assert.All(await WithDb(db => db.Customers.ToListAsync(Token)), c => Assert.EndsWith("@fleetflow.example", c.Email));
        var active = original.Where(x => x.Status is ShipmentStatus.Planned or ShipmentStatus.InTransit).ToList();
        foreach (var a in active)
            foreach (var b in active.Where(x => x.Id != a.Id && (x.AssignedDriverId == a.AssignedDriverId || x.AssignedVehicleId == a.AssignedVehicleId)))
                Assert.False(AssignmentRules.Overlaps(a.PickupWindowStartUtc, a.DeliveryDeadlineUtc, b.PickupWindowStartUtc, b.DeliveryDeadlineUtc));
    }

    [Fact]
    public async Task Seed_refuses_to_overwrite_existing_non_demo_data()
    {
        await Basics();
        await using var scope = services.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<FleetFlowException>(() => scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync("Development", Token));
        Assert.Equal("seed_database_not_empty", failure.Code);
        Assert.Equal(1, await WithDb(db => db.Customers.CountAsync(Token)));
        Assert.Equal(0, await WithDb(db => db.Shipments.CountAsync(Token)));
    }

    [Fact]
    public async Task Ef_concurrency_token_rejects_an_update_loaded_before_another_commit()
    {
        var data = await Basics();
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        await using var firstScope = services.CreateAsyncScope();
        await using var secondScope = services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        var first = await firstDb.Shipments.SingleAsync(x => x.Id == shipment.Id, Token);
        var second = await secondDb.Shipments.SingleAsync(x => x.Id == shipment.Id, Token);
        first.ChangeStatus(ShipmentStatus.Cancelled, null, null, clock.Now, "first-operator");
        second.ChangeStatus(ShipmentStatus.Cancelled, null, null, clock.Now, "second-operator");
        await firstDb.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(Token));
        Assert.Equal(2, (await Load(shipment.Id)).Version);
        Assert.Equal(2, (await Events(shipment.Id)).Count);
        Assert.DoesNotContain(await Events(shipment.Id), e => e.Actor == "second-operator");
    }

    [Fact]
    public async Task Draft_edits_recheck_capacity_and_increment_version_with_an_event()
    {
        var data = await Basics();
        var shipment = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        var failure = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.UpdateAsync(shipment.Id,
            shipment.Version, shipment.Details with { WeightKg = 4000m }, "demo", Token)));
        Assert.Equal("vehicle_overloaded", failure.Code);
        Assert.Equal(1000m, (await Load(shipment.Id)).WeightKg);
        Assert.Equal(2, (await Events(shipment.Id)).Count);
        var updated = await Execute(service => service.UpdateAsync(shipment.Id, shipment.Version,
            shipment.Details with { WeightKg = 1500.25m }, "demo", Token));
        Assert.Equal(3, updated.Version);
        Assert.Equal(1500.25m, (await Load(shipment.Id)).WeightKg);
        Assert.Single((await Events(shipment.Id)), e => e.EventType == ShipmentEventType.DetailsChanged);
        await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.UpdateAsync(shipment.Id,
            shipment.Version, shipment.Details, "demo", Token)));
    }

    [Theory]
    [InlineData("employee")]
    [InlineData("shipment_number")]
    [InlineData("vehicle_payload")]
    [InlineData("missing_address")]
    [InlineData("event_delete")]
    public async Task Additional_database_invariants_are_enforced(string invariant)
    {
        var data = await Basics();
        var shipment = await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
        await WithDb(async db =>
        {
            if (invariant == "employee")
            {
                db.Drivers.Add(Driver.Create(data.Driver.EmployeeNumber.ToLowerInvariant(), "Demo", "Other", true, null, clock.Now));
                var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Token));
                Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
            }
            else
            {
                var statement = invariant switch
                {
                    "shipment_number" => "UPDATE \"Shipments\" SET \"ShipmentNumber\" = {0} WHERE \"Id\" <> {1}",
                    "vehicle_payload" => "UPDATE \"Vehicles\" SET \"MaxPayloadKg\" = 0 WHERE \"Id\" = {0}",
                    "missing_address" => "UPDATE \"Shipments\" SET \"OriginStreet\" = ' ' WHERE \"Id\" = {0}",
                    _ => "DELETE FROM \"ShipmentEvents\" WHERE \"ShipmentId\" = {0}"
                };
                object[] parameters = invariant == "vehicle_payload" ? [data.Vehicle.Id] : [shipment.Id];
                if (invariant == "shipment_number")
                {
                    await Execute(service => service.CreateAsync(Details(data.Customer.Id), "demo", Token));
                    parameters = [shipment.ShipmentNumber, shipment.Id];
                }
                var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(statement, parameters, Token));
                Assert.Equal(invariant == "shipment_number" ? PostgresErrorCodes.UniqueViolation : PostgresErrorCodes.CheckViolation, failure.SqlState);
            }
            return 0;
        });
    }

    [Theory]
    [InlineData(true, ShipmentStatus.Planned)]
    [InlineData(false, ShipmentStatus.Planned)]
    [InlineData(true, ShipmentStatus.InTransit)]
    [InlineData(false, ShipmentStatus.InTransit)]
    public async Task Planning_and_pickup_recheck_resource_eligibility(bool disableDriver, ShipmentStatus target)
    {
        var data = await Basics();
        var shipment = await CreateAndAssign(data.Customer.Id, data.Driver.Id, data.Vehicle.Id);
        if (target == ShipmentStatus.InTransit)
            shipment = await Execute(service => service.ChangeStatusAsync(shipment.Id, shipment.Version, ShipmentStatus.Planned, "demo", cancellationToken: Token));
        await WithDb(async db =>
        {
            if (disableDriver)
                await db.Database.ExecuteSqlRawAsync("UPDATE \"Drivers\" SET \"Active\" = false WHERE \"Id\" = {0}", [data.Driver.Id], Token);
            else
                await db.Database.ExecuteSqlRawAsync("UPDATE \"Vehicles\" SET \"Status\" = 'Maintenance' WHERE \"Id\" = {0}", [data.Vehicle.Id], Token);
            return 0;
        });
        var failure = await Assert.ThrowsAsync<FleetFlowException>(() => Execute(service => service.ChangeStatusAsync(shipment.Id,
            shipment.Version, target, "demo", cancellationToken: Token)));
        Assert.Equal(disableDriver ? "driver_inactive" : "vehicle_unavailable", failure.Code);
        var saved = await Load(shipment.Id);
        Assert.Equal(shipment.Status, saved.Status);
        Assert.Equal(shipment.Version, saved.Version);
        Assert.Null(saved.ActualPickupAtUtc);
        Assert.Equal(shipment.Version, (await Events(shipment.Id)).Count);
    }

    private async Task<(Customer Customer, Driver Driver, Vehicle Vehicle)> Basics(bool active = true, VehicleStatus status = VehicleStatus.Available, int payload = 3500)
    {
        var customer = Customer.Create("Demo Company", "Demo Contact", "demo@example.test", null, clock.Now);
        var driver = Driver.Create("EMP-" + Guid.NewGuid().ToString("N"), "Demo", "Driver", active, "C", clock.Now);
        var vehicle = Vehicle.Create("FF-" + Guid.NewGuid().ToString("N")[..12], VehicleType.Van, payload, status, clock.Now);
        await WithDb(async db => { db.AddRange(customer, driver, vehicle); await db.SaveChangesAsync(Token); return 0; });
        return (customer, driver, vehicle);
    }

    private async Task<ShipmentSnapshot> CreateAndAssign(Guid customer, Guid driver, Guid vehicle)
    {
        var shipment = await Execute(service => service.CreateAsync(Details(customer), "demo", Token));
        return await Execute(service => service.AssignAsync(shipment.Id, shipment.Version, driver, vehicle, "demo", Token));
    }

    private async Task<T> Execute<T>(Func<ShipmentService, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ShipmentService>());
    }

    private async Task<T> WithDb<T>(Func<FleetFlowDbContext, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>());
    }

    private Task<Shipment> Load(Guid id) => WithDb(db => db.Shipments.AsNoTracking().SingleAsync(x => x.Id == id, Token));
    private Task<List<ShipmentEvent>> Events(Guid id) => WithDb(db => db.ShipmentEvents.AsNoTracking().Where(x => x.ShipmentId == id).ToListAsync(Token));

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
