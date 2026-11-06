using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Application.Shipments;

// A focused persistence boundary for shipment commands, not a generic repository.
public interface IShipmentStore
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task<Shipment> GetAsync(Guid id, CancellationToken cancellationToken);
    Task RequireCustomerAsync(Guid id, CancellationToken cancellationToken);
    Task<(Driver Driver, Vehicle Vehicle)> LockResourcesAsync(Guid driverId, Guid vehicleId, CancellationToken cancellationToken);
    Task EnsureNoOverlapAsync(Shipment shipment, Guid driverId, Guid vehicleId, CancellationToken cancellationToken);
    Task<string> NextNumberAsync(DateTimeOffset now, CancellationToken cancellationToken);
    void Add(Shipment shipment);
}
