using FleetFlow.Application.Queries;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Application.Fleet;

public sealed record CustomerInput(string Name, string ContactName, string Email, string? Phone);
public sealed record DriverInput(string EmployeeNumber, string FirstName, string LastName, bool Active, string? LicenseCategory);
public sealed record VehicleInput(string PlateNumber, VehicleType Type, decimal MaxPayloadKg, VehicleStatus Status);

public sealed class FleetService(IFleetStore store, TimeProvider clock)
{
    public Task<CustomerData> CreateCustomerAsync(CustomerInput input, CancellationToken token) => store.InTransactionAsync(_ =>
    {
        var customer = Customer.Create(input.Name, input.ContactName, input.Email, input.Phone, clock.GetUtcNow());
        store.AddCustomer(customer);
        return Task.FromResult(new CustomerData(customer.Id, customer.Name, customer.ContactName, customer.Email, customer.Phone, customer.CreatedAtUtc));
    }, token);

    public Task<DriverData> CreateDriverAsync(DriverInput input, CancellationToken token) => store.InTransactionAsync(_ =>
    {
        var driver = Driver.Create(input.EmployeeNumber, input.FirstName, input.LastName, input.Active, input.LicenseCategory, clock.GetUtcNow());
        store.AddDriver(driver);
        return Task.FromResult(DriverResult(driver));
    }, token);

    public Task<VehicleData> CreateVehicleAsync(VehicleInput input, CancellationToken token) => store.InTransactionAsync(_ =>
    {
        var vehicle = Vehicle.Create(input.PlateNumber, input.Type, input.MaxPayloadKg, input.Status, clock.GetUtcNow());
        store.AddVehicle(vehicle);
        return Task.FromResult(VehicleResult(vehicle));
    }, token);

    public Task<DriverData> UpdateDriverAsync(Guid id, DriverInput input, CancellationToken token) => store.InTransactionAsync(async ct =>
    {
        var driver = await store.LockDriverAsync(Guard.Id(id, "Driver ID"), ct);
        if (!input.Active && await store.HasActiveDriverReservationAsync(id, ct))
            throw FleetFlowException.Conflict("resource_in_use", "A driver assigned to active shipments cannot be deactivated.");
        driver.Update(input.EmployeeNumber, input.FirstName, input.LastName, input.Active, input.LicenseCategory);
        return DriverResult(driver);
    }, token);

    public Task<VehicleData> UpdateVehicleAsync(Guid id, VehicleInput input, CancellationToken token) => store.InTransactionAsync(async ct =>
    {
        var vehicle = await store.LockVehicleAsync(Guard.Id(id, "Vehicle ID"), ct);
        vehicle.Update(input.PlateNumber, input.Type, input.MaxPayloadKg, input.Status);
        if (await store.HasIncompatibleVehicleReservationAsync(id, input.Status, input.MaxPayloadKg, ct))
            throw FleetFlowException.Conflict("resource_in_use", "The change would invalidate an active shipment's vehicle assignment.");
        return VehicleResult(vehicle);
    }, token);

    private static DriverData DriverResult(Driver driver) => new(driver.Id, driver.EmployeeNumber, driver.FirstName, driver.LastName, driver.Active, driver.LicenseCategory, driver.CreatedAtUtc);
    private static VehicleData VehicleResult(Vehicle vehicle) => new(vehicle.Id, vehicle.PlateNumber, vehicle.Type, vehicle.MaxPayloadKg, vehicle.Status, vehicle.CreatedAtUtc);
}
