using FleetFlow.Application.Queries;
using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Api.Contracts;

public sealed record AddressResponse(string Street, string City, string Postcode, string CountryCode)
{
    public static AddressResponse From(Address address) => new(address.Street, address.City, address.Postcode, address.CountryCode);
}
public sealed record ShipmentResponse(Guid Id, string ShipmentNumber, Guid CustomerId, AddressResponse Origin, AddressResponse Destination,
    DateTimeOffset PickupWindowStartUtc, DateTimeOffset DeliveryDeadlineUtc, decimal WeightKg, int PalletCount, string? CargoDescription,
    ShipmentStatus Status, Guid? AssignedDriverId, Guid? AssignedVehicleId, DateTimeOffset? ActualPickupAtUtc, DateTimeOffset? ActualDeliveredAtUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, long Version, string? CustomerName, string? AssignedDriverName, string? AssignedVehiclePlateNumber)
{
    public static ShipmentResponse From(ShipmentSnapshot data, string? customerName = null, string? driverName = null, string? vehiclePlate = null) =>
        new(data.Id, data.ShipmentNumber, data.Details.CustomerId, AddressResponse.From(data.Details.Origin), AddressResponse.From(data.Details.Destination),
            data.Details.PickupWindowStartUtc, data.Details.DeliveryDeadlineUtc, data.Details.WeightKg, data.Details.PalletCount, data.Details.CargoDescription,
            data.Status, data.AssignedDriverId, data.AssignedVehicleId, data.ActualPickupAtUtc, data.ActualDeliveredAtUtc, data.CreatedAtUtc, data.UpdatedAtUtc,
            data.Version, customerName, driverName, vehiclePlate);
    public static ShipmentResponse From(ShipmentReadModel model) => From(model.Data, model.CustomerName, model.DriverName, model.VehiclePlateNumber);
}
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)
{
    public static PageResponse<T> Map<TSource>(PageResult<TSource> page, Func<TSource, T> convert) =>
        new(page.Items.Select(convert).ToArray(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);
}
public sealed record CustomerResponse(Guid Id, string Name, string ContactName, string Email, string? Phone, DateTimeOffset CreatedAtUtc)
{
    public static CustomerResponse From(CustomerData c) => new(c.Id, c.Name, c.ContactName, c.Email, c.Phone, c.CreatedAtUtc);
}
public sealed record DriverResponse(Guid Id, string EmployeeNumber, string FirstName, string LastName, bool Active, string? LicenseCategory, DateTimeOffset CreatedAtUtc)
{
    public static DriverResponse From(DriverData d) => new(d.Id, d.EmployeeNumber, d.FirstName, d.LastName, d.Active, d.LicenseCategory, d.CreatedAtUtc);
}
public sealed record VehicleResponse(Guid Id, string PlateNumber, VehicleType Type, decimal MaxPayloadKg, VehicleStatus Status, DateTimeOffset CreatedAtUtc)
{
    public static VehicleResponse From(VehicleData v) => new(v.Id, v.PlateNumber, v.Type, v.MaxPayloadKg, v.Status, v.CreatedAtUtc);
}
public sealed record ShipmentEventResponse(Guid Id, Guid ShipmentId, ShipmentEventType EventType, ShipmentStatus? OldStatus,
    ShipmentStatus? NewStatus, string? Message, DateTimeOffset OccurredAtUtc, string Actor)
{
    public static ShipmentEventResponse From(EventData e) => new(e.Id, e.ShipmentId, e.EventType, e.OldStatus, e.NewStatus, e.Message, e.OccurredAtUtc, e.Actor);
}
public sealed record DashboardResponse(DateTimeOffset AsOfUtc, int TotalNonCancelledShipments, int DraftShipments, int PlannedShipments,
    int InTransitShipments, int DeliveredShipments, int OverdueShipments, int DueSoonShipments, IReadOnlyList<ShipmentResponse> RecentShipments)
{
    public static DashboardResponse From(DashboardData data) => new(data.AsOfUtc, data.Counts.TotalNonCancelled, data.Counts.Draft, data.Counts.Planned,
        data.Counts.InTransit, data.Counts.Delivered, data.Counts.Overdue, data.Counts.DueSoon, data.RecentShipments.Select(ShipmentResponse.From).ToArray());
}
