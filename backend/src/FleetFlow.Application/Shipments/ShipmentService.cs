using FleetFlow.Domain.Common;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Application.Shipments;

public sealed class ShipmentService(IShipmentStore store, TimeProvider clock)
{
    public Task<ShipmentSnapshot> CreateAsync(ShipmentDetails details, string actor, CancellationToken cancellationToken = default) =>
        store.InTransactionAsync(async token =>
        {
            var data = details.Validate();
            await store.RequireCustomerAsync(data.CustomerId, token);
            var now = clock.GetUtcNow();
            var shipment = Shipment.Create(await store.NextNumberAsync(now, token), data, now, actor);
            store.Add(shipment);
            return ShipmentSnapshot.From(shipment);
        }, cancellationToken);

    public Task<ShipmentSnapshot> AssignAsync(Guid id, long expectedVersion, Guid driverId, Guid vehicleId, string actor, CancellationToken cancellationToken = default) =>
        store.InTransactionAsync(async token =>
        {
            var shipment = await store.GetAsync(Guard.Id(id, "Shipment ID"), token);
            shipment.ExpectVersion(expectedVersion);
            var resources = await store.LockResourcesAsync(Guard.Id(driverId, "Driver ID"), Guard.Id(vehicleId, "Vehicle ID"), token);
            await store.EnsureNoOverlapAsync(shipment, driverId, vehicleId, token);
            shipment.Assign(resources.Driver, resources.Vehicle, clock.GetUtcNow(), actor);
            return ShipmentSnapshot.From(shipment);
        }, cancellationToken);

    public Task<ShipmentSnapshot> ChangeStatusAsync(Guid id, long expectedVersion, ShipmentStatus next, string actor,
        string? reason = null, CancellationToken cancellationToken = default) => store.InTransactionAsync(async token =>
        {
            var shipment = await store.GetAsync(Guard.Id(id, "Shipment ID"), token);
            shipment.ExpectVersion(expectedVersion);
            Driver? driver = null;
            Vehicle? vehicle = null;
            if (next is ShipmentStatus.Planned or ShipmentStatus.InTransit)
            {
                if (shipment.AssignedDriverId is not { } driverId || shipment.AssignedVehicleId is not { } vehicleId)
                    throw FleetFlowException.Conflict("assignment_required", "An eligible driver and vehicle must be assigned first.");
                (driver, vehicle) = await store.LockResourcesAsync(driverId, vehicleId, token);
                await store.EnsureNoOverlapAsync(shipment, driverId, vehicleId, token);
            }
            shipment.ChangeStatus(next, driver, vehicle, clock.GetUtcNow(), actor, reason);
            return ShipmentSnapshot.From(shipment);
        }, cancellationToken);

    public Task<ShipmentSnapshot> UpdateAsync(Guid id, long expectedVersion, ShipmentDetails details, string actor,
        CancellationToken cancellationToken = default) => store.InTransactionAsync(async token =>
        {
            var shipment = await store.GetAsync(Guard.Id(id, "Shipment ID"), token);
            shipment.ExpectVersion(expectedVersion);
            var data = details.Validate();
            await store.RequireCustomerAsync(data.CustomerId, token);
            if (shipment.AssignedDriverId is { } driverId && shipment.AssignedVehicleId is { } vehicleId)
            {
                var resources = await store.LockResourcesAsync(driverId, vehicleId, token);
                AssignmentRules.Validate(resources.Driver, resources.Vehicle, data.WeightKg);
                // Planned fields cannot change; Draft selections do not reserve capacity.
            }
            shipment.UpdateDetails(data, clock.GetUtcNow(), actor);
            return ShipmentSnapshot.From(shipment);
        }, cancellationToken);
}
