using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.UnitTests;

public sealed class ShipmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CustomerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static ShipmentDetails Details => new(CustomerId, Address.Create("Demo Road 1", "Graz", "8010", "AT"),
        Address.Create("Demo Road 2", "Vienna", "1010", "AT"), Now.AddHours(1), Now.AddHours(4), 1000m, 2, "Demo cargo");
    private static Driver Driver(bool active = true) => Domain.Drivers.Driver.Create("DEMO-01", "Demo", "Driver", active, "C", Now);
    private static Vehicle Vehicle(decimal payload = 3500m, VehicleStatus status = VehicleStatus.Available) =>
        Domain.Vehicles.Vehicle.Create("demo-01", VehicleType.Van, payload, status, Now);

    public static TheoryData<ShipmentStatus, ShipmentStatus> TransitionPairs
    {
        get
        {
            var pairs = new TheoryData<ShipmentStatus, ShipmentStatus>();
            foreach (var from in Enum.GetValues<ShipmentStatus>())
                foreach (var to in Enum.GetValues<ShipmentStatus>()) pairs.Add(from, to);
            return pairs;
        }
    }

    [Theory]
    [MemberData(nameof(TransitionPairs))]
    public void Transition_matrix_is_enforced_without_partial_mutation(ShipmentStatus from, ShipmentStatus to)
    {
        var driver = Driver();
        var vehicle = Vehicle();
        var shipment = InState(from, driver, vehicle);
        var count = shipment.Events.Count;
        var version = shipment.Version;
        var allowed = (from, to) is (ShipmentStatus.Draft, ShipmentStatus.Planned or ShipmentStatus.Cancelled)
            or (ShipmentStatus.Planned, ShipmentStatus.InTransit or ShipmentStatus.Cancelled)
            or (ShipmentStatus.InTransit, ShipmentStatus.Delivered);
        if (allowed)
        {
            shipment.ChangeStatus(to, driver, vehicle, Now, "demo");
            Assert.Equal(to, shipment.Status);
            Assert.Equal(version + 1, shipment.Version);
            var history = shipment.Events.Last();
            Assert.Equal(from, history.OldStatus);
            Assert.Equal(to, history.NewStatus);
            Assert.Equal(count + 1, shipment.Events.Count);
        }
        else
        {
            Assert.Equal("invalid_transition", Assert.Throws<FleetFlowException>(() => shipment.ChangeStatus(to, driver, vehicle, Now, "demo")).Code);
            Assert.Equal(from, shipment.Status);
            Assert.Equal(version, shipment.Version);
            Assert.Equal(count, shipment.Events.Count);
        }
    }

    [Theory]
    [InlineData("zero_weight")]
    [InlineData("negative_weight")]
    [InlineData("weight_scale")]
    [InlineData("weight_overflow")]
    [InlineData("negative_pallets")]
    [InlineData("deadline_before")]
    [InlineData("deadline_equal")]
    [InlineData("empty_customer")]
    [InlineData("missing_origin")]
    [InlineData("missing_destination")]
    [InlineData("description_too_long")]
    public void Invalid_shipment_data_is_rejected(string invalid)
    {
        var data = invalid switch
        {
            "zero_weight" => Details with { WeightKg = 0 },
            "negative_weight" => Details with { WeightKg = -1 },
            "weight_scale" => Details with { WeightKg = 0.001m },
            "weight_overflow" => Details with { WeightKg = 10000000000m },
            "negative_pallets" => Details with { PalletCount = -1 },
            "deadline_before" => Details with { DeliveryDeadlineUtc = Now },
            "deadline_equal" => Details with { DeliveryDeadlineUtc = Details.PickupWindowStartUtc },
            "empty_customer" => Details with { CustomerId = Guid.Empty },
            "missing_origin" => Details with { Origin = null! },
            "missing_destination" => Details with { Destination = null! },
            _ => Details with { CargoDescription = new string('x', 1001) }
        };
        Assert.Throws<FleetFlowException>(() => Shipment.Create("FF-TEST-1", data, Now, "demo"));
    }

    [Theory]
    [InlineData("", "Graz", "8010", "AT")]
    [InlineData("Demo road", "", "8010", "AT")]
    [InlineData("Demo road", "Graz", "", "AT")]
    [InlineData("Demo road", "Graz", "8010", "ZZ")]
    [InlineData("Demo road", "Graz", "8010", "AUT")]
    public void Address_fields_and_country_are_validated(string street, string city, string postcode, string country) =>
        Assert.Throws<FleetFlowException>(() => Address.Create(street, city, postcode, country));

    [Fact]
    public void Planning_requires_assignment_and_delivery_records_actual_times()
    {
        var shipment = Shipment.Create("FF-TEST-1", Details, Now, "demo");
        Assert.Equal("assignment_required", Assert.Throws<FleetFlowException>(() => shipment.ChangeStatus(ShipmentStatus.Planned, null, null, Now, "demo")).Code);
        Assert.Single(shipment.Events);
        var driver = Driver(); var vehicle = Vehicle();
        shipment.Assign(driver, vehicle, Now, "demo");
        shipment.ChangeStatus(ShipmentStatus.Planned, driver, vehicle, Now, "demo");
        shipment.ChangeStatus(ShipmentStatus.InTransit, driver, vehicle, Now.AddHours(1), "demo");
        shipment.ChangeStatus(ShipmentStatus.Delivered, null, null, Now.AddHours(2), "demo");
        Assert.Equal(Now.AddHours(1), shipment.ActualPickupAtUtc);
        Assert.Equal(Now.AddHours(2), shipment.ActualDeliveredAtUtc);
        Assert.Equal(5, shipment.Events.Count);
    }

    [Theory]
    [InlineData(false, VehicleStatus.Available, 3500, "driver_inactive")]
    [InlineData(true, VehicleStatus.Maintenance, 3500, "vehicle_unavailable")]
    [InlineData(true, VehicleStatus.Inactive, 3500, "vehicle_unavailable")]
    [InlineData(true, VehicleStatus.Available, 999, "vehicle_overloaded")]
    public void Invalid_assignment_preserves_state_and_history(bool active, VehicleStatus status, int capacity, string code)
    {
        var shipment = Shipment.Create("FF-TEST-1", Details, Now, "demo");
        Assert.Equal(code, Assert.Throws<FleetFlowException>(() => shipment.Assign(Driver(active), Vehicle(capacity, status), Now, "demo")).Code);
        Assert.Null(shipment.AssignedDriverId);
        Assert.Null(shipment.AssignedVehicleId);
        Assert.Equal(1, shipment.Version);
        Assert.Single(shipment.Events);
    }

    [Fact]
    public void Exact_capacity_is_allowed_but_4000_kg_cannot_use_3500_kg_van()
    {
        var shipment = Shipment.Create("FF-TEST-1", Details with { WeightKg = 4000 }, Now, "demo");
        Assert.Equal("vehicle_overloaded", Assert.Throws<FleetFlowException>(() => shipment.Assign(Driver(), Vehicle(), Now, "demo")).Code);
        shipment.Assign(Driver(), Vehicle(4000), Now, "demo");
        Assert.NotNull(shipment.AssignedVehicleId);
    }

    [Theory]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Cancelled)]
    public void Assignment_is_locked_after_planning_phase(ShipmentStatus state)
    {
        var shipment = InState(state, Driver(), Vehicle());
        Assert.Equal("assignment_locked", Assert.Throws<FleetFlowException>(() => shipment.Assign(Driver(), Vehicle(), Now, "demo")).Code);
    }

    [Fact]
    public void Stale_version_and_repeated_assignment_are_explicit_conflicts()
    {
        var shipment = Shipment.Create("FF-TEST-1", Details, Now, "demo");
        var driver = Driver(); var vehicle = Vehicle();
        shipment.Assign(driver, vehicle, Now, "demo");
        Assert.Equal("stale_version", Assert.Throws<FleetFlowException>(() => shipment.ExpectVersion(1)).Code);
        shipment.ExpectVersion(2);
        Assert.Equal("assignment_unchanged", Assert.Throws<FleetFlowException>(() => shipment.Assign(driver, vehicle, Now, "demo")).Code);
        Assert.Equal(2, shipment.Events.Count);
    }

    [Fact]
    public void Planned_edits_are_limited_and_always_audited()
    {
        var shipment = InState(ShipmentStatus.Planned, Driver(), Vehicle());
        Assert.Equal("fields_locked", Assert.Throws<FleetFlowException>(() => shipment.UpdateDetails(Details with { WeightKg = 1200 }, Now, "demo")).Code);
        shipment.UpdateDetails(Details with { CargoDescription = "Updated demo cargo" }, Now, "demo");
        Assert.Equal(ShipmentEventType.DetailsChanged, shipment.Events.Last().EventType);
        Assert.Equal(4, shipment.Version);
    }

    [Theory]
    [InlineData(0, 2, 1, 3, true)]
    [InlineData(0, 2, 2, 4, false)]
    [InlineData(2, 4, 0, 2, false)]
    [InlineData(0, 4, 1, 2, true)]
    [InlineData(0, 2, 0, 2, true)]
    public void Reservation_intervals_are_half_open(int a, int b, int c, int d, bool expected) =>
        Assert.Equal(expected, AssignmentRules.Overlaps(Now.AddHours(a), Now.AddHours(b), Now.AddHours(c), Now.AddHours(d)));

    [Theory]
    [InlineData(ShipmentStatus.Draft)]
    [InlineData(ShipmentStatus.Planned)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Cancelled)]
    public void Deadline_classification_excludes_terminal_statuses(ShipmentStatus state)
    {
        var shipment = InState(state, Driver(), Vehicle());
        var active = state is not (ShipmentStatus.Delivered or ShipmentStatus.Cancelled);
        Assert.Equal(active, shipment.IsDueSoon(Now));
        Assert.False(shipment.IsOverdue(Now));
        Assert.Equal(active, shipment.IsOverdue(Now.AddDays(1)));
        Assert.False(shipment.IsDueSoon(Now.AddDays(1)));
    }

    [Fact]
    public void Text_normalization_utc_and_audit_actor_are_enforced()
    {
        Assert.Equal("DEMO-01", Vehicle().PlateNumber);
        Assert.Equal("AT", Address.Create("Demo", "Graz", "8010", "at").CountryCode);
        var shipment = Shipment.Create("FF-TEST-1", Details with { PickupWindowStartUtc = Now.AddHours(3).ToOffset(TimeSpan.FromHours(2)) }, Now, "demo");
        Assert.Equal(TimeSpan.Zero, shipment.PickupWindowStartUtc.Offset);
        Assert.Throws<FleetFlowException>(() => Shipment.Create("FF-TEST-2", Details, Now, " "));
        Assert.Throws<FleetFlowException>(() => shipment.ChangeStatus(ShipmentStatus.Cancelled, null, null, Now.AddSeconds(-1), "demo"));
        Assert.Single(shipment.Events);
    }

    [Fact]
    public void Master_data_validation_rejects_missing_or_invalid_fields()
    {
        Assert.Throws<FleetFlowException>(() => Customer.Create("", "Demo", "demo@example.test", null, Now));
        Assert.Throws<FleetFlowException>(() => Customer.Create("Demo", "Demo", "invalid email", null, Now));
        Assert.Throws<FleetFlowException>(() => Domain.Drivers.Driver.Create("", "Demo", "Driver", true, null, Now));
        Assert.Throws<FleetFlowException>(() => Domain.Vehicles.Vehicle.Create("DEMO", VehicleType.Van, 0, VehicleStatus.Available, Now));
        Assert.Throws<FleetFlowException>(() => Domain.Vehicles.Vehicle.Create("DEMO", (VehicleType)99, 1000, VehicleStatus.Available, Now));
    }

    [Theory]
    [InlineData(-1, false, true)]
    [InlineData(0, true, false)]
    [InlineData(24, true, false)]
    [InlineData(25, false, false)]
    public void Deadline_boundaries_are_explicit(int deadlineHours, bool dueSoon, bool overdue)
    {
        var deadline = Now.AddHours(deadlineHours);
        var shipment = Shipment.Create("FF-TEST-1", Details with { PickupWindowStartUtc = deadline.AddHours(-2), DeliveryDeadlineUtc = deadline }, Now, "demo");
        Assert.Equal(dueSoon, shipment.IsDueSoon(Now));
        Assert.Equal(overdue, shipment.IsOverdue(Now));
    }

    private static Shipment InState(ShipmentStatus state, Driver driver, Vehicle vehicle)
    {
        var shipment = Shipment.Create("FF-TEST-1", Details, Now, "demo");
        shipment.Assign(driver, vehicle, Now, "demo");
        if (state == ShipmentStatus.Cancelled) shipment.ChangeStatus(state, null, null, Now, "demo");
        else if (state != ShipmentStatus.Draft)
        {
            shipment.ChangeStatus(ShipmentStatus.Planned, driver, vehicle, Now, "demo");
            if (state is ShipmentStatus.InTransit or ShipmentStatus.Delivered)
                shipment.ChangeStatus(ShipmentStatus.InTransit, driver, vehicle, Now, "demo");
            if (state == ShipmentStatus.Delivered) shipment.ChangeStatus(state, null, null, Now, "demo");
        }
        return shipment;
    }
}
