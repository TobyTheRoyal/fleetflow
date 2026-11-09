using FleetFlow.Application.Fleet;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Fleet;

internal sealed class EfFleetStore(FleetFlowDbContext db, EfTransaction transaction) : IFleetStore
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => transaction.RunAsync(action, cancellationToken);
    public void AddCustomer(Customer customer) => db.Customers.Add(customer);
    public void AddDriver(Driver driver) => db.Drivers.Add(driver);
    public void AddVehicle(Vehicle vehicle) => db.Vehicles.Add(vehicle);
    public async Task<Driver> LockDriverAsync(Guid id, CancellationToken token) =>
        await db.Drivers.FromSqlRaw(string.Concat("SELECT * FROM ", db.SqlTable("Drivers"), " WHERE \"Id\" = {0} FOR UPDATE"), id).SingleOrDefaultAsync(token)
        ?? throw FleetFlowException.NotFound("driver");
    public async Task<Vehicle> LockVehicleAsync(Guid id, CancellationToken token) =>
        await db.Vehicles.FromSqlRaw(string.Concat("SELECT * FROM ", db.SqlTable("Vehicles"), " WHERE \"Id\" = {0} FOR UPDATE"), id).SingleOrDefaultAsync(token)
        ?? throw FleetFlowException.NotFound("vehicle");
    public Task<bool> HasActiveDriverReservationAsync(Guid id, CancellationToken token) => db.Shipments.AsNoTracking().AnyAsync(s =>
        s.AssignedDriverId == id && (s.Status == ShipmentStatus.Planned || s.Status == ShipmentStatus.InTransit), token);
    public Task<bool> HasIncompatibleVehicleReservationAsync(Guid id, VehicleStatus status, decimal capacity, CancellationToken token) => db.Shipments.AsNoTracking().AnyAsync(s =>
        s.AssignedVehicleId == id && (s.Status == ShipmentStatus.Planned || s.Status == ShipmentStatus.InTransit)
        && (status != VehicleStatus.Available || s.WeightKg > capacity), token);
}
