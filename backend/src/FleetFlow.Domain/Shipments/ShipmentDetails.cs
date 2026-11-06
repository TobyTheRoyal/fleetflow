using FleetFlow.Domain.Common;

namespace FleetFlow.Domain.Shipments;

public sealed record ShipmentDetails(Guid CustomerId, Address Origin, Address Destination,
    DateTimeOffset PickupWindowStartUtc, DateTimeOffset DeliveryDeadlineUtc,
    decimal WeightKg, int PalletCount, string? CargoDescription)
{
    public ShipmentDetails Validate()
    {
        Guard.Id(CustomerId, "Customer ID");
        if (Origin is null || Destination is null) throw new FleetFlowException("invalid_address", "Origin and destination addresses are required.");
        var pickup = Guard.Utc(PickupWindowStartUtc);
        var deadline = Guard.Utc(DeliveryDeadlineUtc);
        if (deadline <= pickup) throw new FleetFlowException("invalid_schedule", "Delivery deadline must be after pickup.");
        Guard.Weight(WeightKg, "Cargo weight");
        if (PalletCount < 0) throw new FleetFlowException("invalid_pallet_count", "Pallet count cannot be negative.");
        return this with { PickupWindowStartUtc = pickup, DeliveryDeadlineUtc = deadline, CargoDescription = Guard.Optional(CargoDescription, "Cargo description", 1000) };
    }
}
