using FleetFlow.Domain.Common;

namespace FleetFlow.Domain.Vehicles;

public enum VehicleType { Van, Truck }
public enum VehicleStatus { Available, Maintenance, Inactive }

public sealed class Vehicle
{
    private Vehicle() { }
    public Guid Id { get; private set; }
    public string PlateNumber { get; private set; } = null!;
    public VehicleType Type { get; private set; }
    public decimal MaxPayloadKg { get; private set; }
    public VehicleStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Vehicle Create(string plateNumber, VehicleType type, decimal maxPayloadKg, VehicleStatus status, DateTimeOffset now, Guid? id = null)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(status)) throw new FleetFlowException("invalid_input", "A valid vehicle type and status are required.");
        return new Vehicle
        {
            Id = Guard.Id(id ?? Guid.CreateVersion7(), "Vehicle ID"),
            PlateNumber = Guard.Text(plateNumber, "Plate number", 20).ToUpperInvariant(),
            Type = type,
            MaxPayloadKg = Guard.Weight(maxPayloadKg, "Maximum payload"),
            Status = status,
            CreatedAtUtc = Guard.Utc(now)
        };
    }
}
