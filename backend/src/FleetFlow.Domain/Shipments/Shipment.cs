using FleetFlow.Domain.Common;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Domain.Shipments;

public sealed class Shipment
{
    private readonly List<ShipmentEvent> events = [];
    private Shipment() { }
    public Guid Id { get; private set; }
    public string ShipmentNumber { get; private set; } = null!;
    public Guid CustomerId { get; private set; }
    public Address Origin { get; private set; } = null!;
    public Address Destination { get; private set; } = null!;
    public DateTimeOffset PickupWindowStartUtc { get; private set; }
    public DateTimeOffset DeliveryDeadlineUtc { get; private set; }
    public string? CargoDescription { get; private set; }
    public decimal WeightKg { get; private set; }
    public int PalletCount { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public Guid? AssignedDriverId { get; private set; }
    public Guid? AssignedVehicleId { get; private set; }
    public DateTimeOffset? ActualPickupAtUtc { get; private set; }
    public DateTimeOffset? ActualDeliveredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<ShipmentEvent> Events => events.AsReadOnly();

    public static Shipment Create(string shipmentNumber, ShipmentDetails details, DateTimeOffset now, string actor, Guid? id = null)
    {
        var data = details.Validate();
        var shipment = new Shipment
        {
            Id = Guard.Id(id ?? Guid.CreateVersion7(), "Shipment ID"),
            ShipmentNumber = Guard.Text(shipmentNumber, "Shipment number", 40),
            CreatedAtUtc = Guard.Utc(now),
            UpdatedAtUtc = Guard.Utc(now),
            Version = 1
        };
        shipment.Apply(data);
        shipment.events.Add(ShipmentEvent.Create(shipment.Id, ShipmentEventType.Created, null, ShipmentStatus.Draft,
            null, shipment.CreatedAtUtc, Guard.Text(actor, "Actor", 100)));
        return shipment;
    }

    public ShipmentDetails Details() => new(CustomerId, Origin, Destination, PickupWindowStartUtc, DeliveryDeadlineUtc, WeightKg, PalletCount, CargoDescription);

    public void ExpectVersion(long expectedVersion)
    {
        if (expectedVersion != Version) throw FleetFlowException.Conflict("stale_version", "This shipment has changed. Reload it before trying again.");
    }

    public void Assign(Driver driver, Vehicle vehicle, DateTimeOffset now, string actor)
    {
        var context = ValidateContext(now, actor);
        if (Status is not (ShipmentStatus.Draft or ShipmentStatus.Planned))
            throw FleetFlowException.Conflict("assignment_locked", "Assignment can only change for Draft or Planned shipments.");
        AssignmentRules.Validate(driver, vehicle, WeightKg);
        if (AssignedDriverId == driver.Id && AssignedVehicleId == vehicle.Id)
            throw FleetFlowException.Conflict("assignment_unchanged", "The driver and vehicle are already assigned.");
        AssignedDriverId = driver.Id;
        AssignedVehicleId = vehicle.Id;
        Record(ShipmentEventType.AssignmentChanged, null, null, $"Driver {driver.EmployeeNumber}; vehicle {vehicle.PlateNumber}.", context.Now, context.Actor);
    }

    public void ChangeStatus(ShipmentStatus next, Driver? driver, Vehicle? vehicle, DateTimeOffset now, string actor, string? reason = null)
    {
        var context = ValidateContext(now, actor);
        var message = Guard.Optional(reason, "Reason", 1000);
        var allowed = (Status, next) switch
        {
            (ShipmentStatus.Draft, ShipmentStatus.Planned or ShipmentStatus.Cancelled) => true,
            (ShipmentStatus.Planned, ShipmentStatus.InTransit or ShipmentStatus.Cancelled) => true,
            (ShipmentStatus.InTransit, ShipmentStatus.Delivered) => true,
            _ => false
        };
        if (!allowed) throw FleetFlowException.Conflict("invalid_transition", $"Cannot change status from {Status} to {next}.");
        if (next is ShipmentStatus.Planned or ShipmentStatus.InTransit)
        {
            if (driver is null || vehicle is null || driver.Id != AssignedDriverId || vehicle.Id != AssignedVehicleId)
                throw FleetFlowException.Conflict("assignment_required", "An eligible driver and vehicle must be assigned first.");
            AssignmentRules.Validate(driver, vehicle, WeightKg);
        }
        if (next == ShipmentStatus.Delivered && (ActualPickupAtUtc is null || context.Now < ActualPickupAtUtc))
            throw new FleetFlowException("invalid_delivery_time", "Delivery cannot precede actual pickup.");
        var old = Status;
        Status = next;
        if (next == ShipmentStatus.InTransit) ActualPickupAtUtc = context.Now;
        if (next == ShipmentStatus.Delivered) ActualDeliveredAtUtc = context.Now;
        Record(ShipmentEventType.StatusChanged, old, next, message, context.Now, context.Actor);
    }

    public void UpdateDetails(ShipmentDetails details, DateTimeOffset now, string actor)
    {
        var context = ValidateContext(now, actor);
        var data = details.Validate();
        if (Status is not (ShipmentStatus.Draft or ShipmentStatus.Planned))
            throw FleetFlowException.Conflict("shipment_locked", "Only Draft and Planned shipments can be edited.");
        if (Status == ShipmentStatus.Planned && data with { CargoDescription = CargoDescription } != Details())
            throw FleetFlowException.Conflict("fields_locked", "Only the cargo description can change after planning.");
        if (data == Details()) throw FleetFlowException.Conflict("details_unchanged", "No shipment fields have changed.");
        Apply(data);
        Record(ShipmentEventType.DetailsChanged, null, null, "Shipment details updated.", context.Now, context.Actor);
    }

    public bool IsOverdue(DateTimeOffset now) => Status is not (ShipmentStatus.Delivered or ShipmentStatus.Cancelled) && now > DeliveryDeadlineUtc;
    public bool IsDueSoon(DateTimeOffset now) => Status is not (ShipmentStatus.Delivered or ShipmentStatus.Cancelled)
        && DeliveryDeadlineUtc >= now && DeliveryDeadlineUtc <= now.AddHours(24);

    private (DateTimeOffset Now, string Actor) ValidateContext(DateTimeOffset now, string actor)
    {
        var timestamp = Guard.Utc(now);
        if (timestamp < UpdatedAtUtc) throw new FleetFlowException("invalid_timestamp", "An event cannot precede the last shipment update.");
        return (timestamp, Guard.Text(actor, "Actor", 100));
    }

    private void Record(ShipmentEventType type, ShipmentStatus? old, ShipmentStatus? next, string? message, DateTimeOffset now, string actor)
    {
        Version++;
        UpdatedAtUtc = now;
        events.Add(ShipmentEvent.Create(Id, type, old, next, message, now, actor));
    }

    private void Apply(ShipmentDetails data)
    {
        CustomerId = data.CustomerId; Origin = data.Origin; Destination = data.Destination;
        PickupWindowStartUtc = data.PickupWindowStartUtc; DeliveryDeadlineUtc = data.DeliveryDeadlineUtc;
        WeightKg = data.WeightKg; PalletCount = data.PalletCount; CargoDescription = data.CargoDescription;
    }
}
