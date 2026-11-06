using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Application.Fleet;

public interface IFleetStore
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    void AddCustomer(Customer customer);
    void AddDriver(Driver driver);
    void AddVehicle(Vehicle vehicle);
    Task<Driver> LockDriverAsync(Guid id, CancellationToken cancellationToken);
    Task<Vehicle> LockVehicleAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasActiveDriverReservationAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> HasIncompatibleVehicleReservationAsync(Guid id, VehicleStatus status, decimal capacity, CancellationToken cancellationToken);
}
