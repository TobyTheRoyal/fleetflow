using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;

namespace FleetFlow.Application.Queries;

public sealed record PageRequest(int Page = 1, int PageSize = 20);
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)(((long)TotalCount + PageSize - 1) / PageSize);
}
public enum DeadlineFilter { Overdue, DueSoon }
public sealed record ShipmentFilter(PageRequest Paging, ShipmentStatus? Status = null, Guid? CustomerId = null, string? Search = null, DeadlineFilter? Deadline = null);
public sealed record ShipmentReadModel(ShipmentSnapshot Data, string CustomerName, string? DriverName, string? VehiclePlateNumber);
public sealed record CustomerData(Guid Id, string Name, string ContactName, string Email, string? Phone, DateTimeOffset CreatedAtUtc);
public sealed record DriverData(Guid Id, string EmployeeNumber, string FirstName, string LastName, bool Active, string? LicenseCategory, DateTimeOffset CreatedAtUtc);
public sealed record VehicleData(Guid Id, string PlateNumber, VehicleType Type, decimal MaxPayloadKg, VehicleStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record EventData(Guid Id, Guid ShipmentId, ShipmentEventType EventType, ShipmentStatus? OldStatus,
    ShipmentStatus? NewStatus, string? Message, DateTimeOffset OccurredAtUtc, string Actor);
public sealed record DashboardCounts(int TotalNonCancelled, int Draft, int Planned, int InTransit, int Delivered, int Overdue, int DueSoon);
public sealed record DashboardData(DateTimeOffset AsOfUtc, DashboardCounts Counts, IReadOnlyList<ShipmentReadModel> RecentShipments);

public interface ILogisticsQueries
{
    Task<PageResult<ShipmentReadModel>> ShipmentsAsync(ShipmentFilter filter, CancellationToken cancellationToken);
    Task<ShipmentReadModel> ShipmentAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<EventData>> EventsAsync(Guid id, PageRequest paging, CancellationToken cancellationToken);
    Task<DashboardData> DashboardAsync(CancellationToken cancellationToken);
    Task<PageResult<CustomerData>> CustomersAsync(PageRequest paging, CancellationToken cancellationToken);
    Task<PageResult<DriverData>> DriversAsync(PageRequest paging, CancellationToken cancellationToken);
    Task<PageResult<VehicleData>> VehiclesAsync(PageRequest paging, CancellationToken cancellationToken);
}
