using FleetFlow.Domain.Common;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Domain.Shipments;

public static class AssignmentRules
{
    public static void Validate(Driver driver, Vehicle vehicle, decimal weightKg)
    {
        if (!driver.Active) throw FleetFlowException.Conflict("driver_inactive", "The selected driver is inactive.");
        if (vehicle.Status != VehicleStatus.Available) throw FleetFlowException.Conflict("vehicle_unavailable", "The selected vehicle is not available.");
        if (weightKg > vehicle.MaxPayloadKg) throw new FleetFlowException("vehicle_overloaded", "Cargo weight exceeds the selected vehicle's maximum payload.");
    }

    public static bool Overlaps(DateTimeOffset firstStart, DateTimeOffset firstEnd, DateTimeOffset secondStart, DateTimeOffset secondEnd) =>
        firstStart < secondEnd && secondStart < firstEnd;
}
