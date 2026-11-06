namespace FleetFlow.Domain.Shipments;

public enum ShipmentStatus { Draft, Planned, InTransit, Delivered, Cancelled }
public enum ShipmentEventType { Created, AssignmentChanged, StatusChanged, DetailsChanged }

public sealed class ShipmentEvent
{
    private ShipmentEvent() { }
    public Guid Id { get; private set; }
    public Guid ShipmentId { get; private set; }
    public ShipmentEventType EventType { get; private set; }
    public ShipmentStatus? OldStatus { get; private set; }
    public ShipmentStatus? NewStatus { get; private set; }
    public string? Message { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Actor { get; private set; } = null!;

    internal static ShipmentEvent Create(Guid shipmentId, ShipmentEventType eventType, ShipmentStatus? oldStatus,
        ShipmentStatus? newStatus, string? message, DateTimeOffset now, string actor) => new()
        {
            Id = Guid.CreateVersion7(),
            ShipmentId = shipmentId,
            EventType = eventType,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Message = message,
            OccurredAtUtc = now,
            Actor = actor
        };
}
