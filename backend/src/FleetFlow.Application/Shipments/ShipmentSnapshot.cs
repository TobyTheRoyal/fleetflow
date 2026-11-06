using FleetFlow.Domain.Shipments;

namespace FleetFlow.Application.Shipments;

public sealed record ShipmentSnapshot(Guid Id, string ShipmentNumber, ShipmentDetails Details, ShipmentStatus Status,
    Guid? AssignedDriverId, Guid? AssignedVehicleId, DateTimeOffset? ActualPickupAtUtc,
    DateTimeOffset? ActualDeliveredAtUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version)
{
    public static ShipmentSnapshot From(Shipment shipment) => new(shipment.Id, shipment.ShipmentNumber, shipment.Details(),
        shipment.Status, shipment.AssignedDriverId, shipment.AssignedVehicleId, shipment.ActualPickupAtUtc,
        shipment.ActualDeliveredAtUtc, shipment.CreatedAtUtc, shipment.UpdatedAtUtc, shipment.Version);
}
