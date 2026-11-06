using System.ComponentModel.DataAnnotations;
using FleetFlow.Application.Fleet;
using FleetFlow.Application.Queries;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Contracts;

public sealed class AddressRequest
{
    [Required, StringLength(200)] public required string Street { get; init; }
    [Required, StringLength(100)] public required string City { get; init; }
    [Required, StringLength(20)] public required string Postcode { get; init; }
    [Required, StringLength(2)] public required string CountryCode { get; init; }
    public Address ToDomain() => Address.Create(Street, City, Postcode, CountryCode);
}

public abstract class ShipmentFields
{
    public required Guid CustomerId { get; init; }
    [Required] public required AddressRequest Origin { get; init; }
    [Required] public required AddressRequest Destination { get; init; }
    public required DateTimeOffset PickupWindowStartUtc { get; init; }
    public required DateTimeOffset DeliveryDeadlineUtc { get; init; }
    public required decimal WeightKg { get; init; }
    public required int PalletCount { get; init; }
    [StringLength(1000)] public string? CargoDescription { get; init; }
    public ShipmentDetails ToDomain() => new(CustomerId, Origin.ToDomain(), Destination.ToDomain(), PickupWindowStartUtc,
        DeliveryDeadlineUtc, WeightKg, PalletCount, CargoDescription);
}
public sealed class CreateShipmentRequest : ShipmentFields;
public sealed class UpdateShipmentRequest : ShipmentFields
{
    [Range(1, long.MaxValue)] public required long Version { get; init; }
}
public sealed class AssignShipmentRequest
{
    [Range(1, long.MaxValue)] public required long Version { get; init; }
    public required Guid DriverId { get; init; }
    public required Guid VehicleId { get; init; }
}
public sealed class ChangeStatusRequest
{
    [Range(1, long.MaxValue)] public required long Version { get; init; }
    [EnumDataType(typeof(ShipmentStatus))] public required ShipmentStatus Status { get; init; }
    [StringLength(1000)] public string? Reason { get; init; }
}
public class PagingQuery
{
    [FromQuery(Name = "page"), Range(1, 1000000)] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 100)] public int PageSize { get; init; } = 20;
    public PageRequest ToPage() => new(Page, PageSize);
}
public sealed class ShipmentQuery : PagingQuery
{
    [FromQuery(Name = "status"), EnumDataType(typeof(ShipmentStatus))] public ShipmentStatus? Status { get; init; }
    [FromQuery(Name = "customerId")] public Guid? CustomerId { get; init; }
    [FromQuery(Name = "search"), StringLength(100)] public string? Search { get; init; }
    [FromQuery(Name = "deadline"), EnumDataType(typeof(DeadlineFilter))] public DeadlineFilter? Deadline { get; init; }
    public ShipmentFilter ToFilter() => new(ToPage(), Status, CustomerId, Search, Deadline);
}
public sealed class CreateCustomerRequest
{
    [Required, StringLength(200)] public required string Name { get; init; }
    [Required, StringLength(150)] public required string ContactName { get; init; }
    [Required, StringLength(254)] public required string Email { get; init; }
    [StringLength(40)] public string? Phone { get; init; }
    public CustomerInput ToInput() => new(Name, ContactName, Email, Phone);
}
public sealed class DriverRequest
{
    [Required, StringLength(40)] public required string EmployeeNumber { get; init; }
    [Required, StringLength(100)] public required string FirstName { get; init; }
    [Required, StringLength(100)] public required string LastName { get; init; }
    public required bool Active { get; init; }
    [StringLength(30)] public string? LicenseCategory { get; init; }
    public DriverInput ToInput() => new(EmployeeNumber, FirstName, LastName, Active, LicenseCategory);
}
public sealed class VehicleRequest
{
    [Required, StringLength(20)] public required string PlateNumber { get; init; }
    [EnumDataType(typeof(VehicleType))] public required VehicleType Type { get; init; }
    public required decimal MaxPayloadKg { get; init; }
    [EnumDataType(typeof(VehicleStatus))] public required VehicleStatus Status { get; init; }
    public VehicleInput ToInput() => new(PlateNumber, Type, MaxPayloadKg, Status);
}
