using System.Data;
using FleetFlow.Application.Queries;
using FleetFlow.Application.Shipments;
using FleetFlow.Domain.Common;
using FleetFlow.Domain.Shipments;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Queries;

internal sealed class EfLogisticsQueries(FleetFlowDbContext db, TimeProvider clock) : ILogisticsQueries
{
    public async Task<PageResult<ShipmentReadModel>> ShipmentsAsync(ShipmentFilter filter, CancellationToken token)
    {
        var query = db.Shipments.AsNoTracking().AsQueryable();
        if (filter.Status is { } status) query = query.Where(s => s.Status == status);
        if (filter.CustomerId is { } customer) query = query.Where(s => s.CustomerId == customer);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Escape LIKE wildcards so the API's text search is literal substring search.
            var text = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = "%" + text + "%";
            query = query.Where(s => EF.Functions.ILike(s.ShipmentNumber, pattern, "\\")
                || EF.Functions.ILike(s.Origin.City, pattern, "\\") || EF.Functions.ILike(s.Destination.City, pattern, "\\"));
        }
        var now = clock.GetUtcNow();
        if (filter.Deadline is { } deadline)
        {
            query = query.Where(s => s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Cancelled);
            query = deadline == DeadlineFilter.Overdue ? query.Where(s => s.DeliveryDeadlineUtc < now)
                : query.Where(s => s.DeliveryDeadlineUtc >= now && s.DeliveryDeadlineUtc <= now.AddHours(24));
        }
        return await PageAsync(Project(query), filter.Paging, token);
    }

    public async Task<ShipmentReadModel> ShipmentAsync(Guid id, CancellationToken token) =>
        await Project(db.Shipments.AsNoTracking().Where(s => s.Id == id)).SingleOrDefaultAsync(token)
        ?? throw FleetFlowException.NotFound("shipment");

    public async Task<PageResult<EventData>> EventsAsync(Guid id, PageRequest paging, CancellationToken token)
    {
        if (!await db.Shipments.AsNoTracking().AnyAsync(s => s.Id == id, token)) throw FleetFlowException.NotFound("shipment");
        return await PageAsync(db.ShipmentEvents.AsNoTracking().Where(e => e.ShipmentId == id)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => new EventData(e.Id, e.ShipmentId, e.EventType, e.OldStatus, e.NewStatus, e.Message, e.OccurredAtUtc, e.Actor)), paging, token);
    }

    public async Task<DashboardData> DashboardAsync(CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var soon = now.AddHours(24);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var counts = await db.Shipments.AsNoTracking().GroupBy(_ => 1).Select(group => new DashboardCounts(
            group.Count(s => s.Status != ShipmentStatus.Cancelled),
            group.Count(s => s.Status == ShipmentStatus.Draft), group.Count(s => s.Status == ShipmentStatus.Planned),
            group.Count(s => s.Status == ShipmentStatus.InTransit), group.Count(s => s.Status == ShipmentStatus.Delivered),
            group.Count(s => s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Cancelled && s.DeliveryDeadlineUtc < now),
            group.Count(s => s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Cancelled && s.DeliveryDeadlineUtc >= now && s.DeliveryDeadlineUtc <= soon)))
            .SingleOrDefaultAsync(token) ?? new DashboardCounts(0, 0, 0, 0, 0, 0, 0);
        var recent = await Project(db.Shipments.AsNoTracking()).Take(10).ToListAsync(token);
        await snapshot.CommitAsync(token);
        return new DashboardData(now, counts, recent);
    }

    public Task<PageResult<CustomerData>> CustomersAsync(PageRequest paging, CancellationToken token) =>
        PageAsync(db.Customers.AsNoTracking().OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CustomerData(c.Id, c.Name, c.ContactName, c.Email, c.Phone, c.CreatedAtUtc)), paging, token);
    public Task<PageResult<DriverData>> DriversAsync(PageRequest paging, CancellationToken token) =>
        PageAsync(db.Drivers.AsNoTracking().OrderBy(d => d.EmployeeNumber).ThenBy(d => d.Id)
            .Select(d => new DriverData(d.Id, d.EmployeeNumber, d.FirstName, d.LastName, d.Active, d.LicenseCategory, d.CreatedAtUtc)), paging, token);
    public Task<PageResult<VehicleData>> VehiclesAsync(PageRequest paging, CancellationToken token) =>
        PageAsync(db.Vehicles.AsNoTracking().OrderBy(v => v.PlateNumber).ThenBy(v => v.Id)
            .Select(v => new VehicleData(v.Id, v.PlateNumber, v.Type, v.MaxPayloadKg, v.Status, v.CreatedAtUtc)), paging, token);

    private IQueryable<ShipmentReadModel> Project(IQueryable<Shipment> shipments) =>
        from s in shipments
        join c in db.Customers.AsNoTracking() on s.CustomerId equals c.Id
        join d in db.Drivers.AsNoTracking() on s.AssignedDriverId equals d.Id into drivers
        from d in drivers.DefaultIfEmpty()
        join v in db.Vehicles.AsNoTracking() on s.AssignedVehicleId equals v.Id into vehicles
        from v in vehicles.DefaultIfEmpty()
        orderby s.CreatedAtUtc descending, s.Id descending
        select new ShipmentReadModel(new ShipmentSnapshot(s.Id, s.ShipmentNumber,
            new ShipmentDetails(s.CustomerId, s.Origin, s.Destination, s.PickupWindowStartUtc, s.DeliveryDeadlineUtc, s.WeightKg, s.PalletCount, s.CargoDescription),
            s.Status, s.AssignedDriverId, s.AssignedVehicleId, s.ActualPickupAtUtc, s.ActualDeliveredAtUtc, s.CreatedAtUtc, s.UpdatedAtUtc, s.Version),
            c.Name, d == null ? null : d.FirstName + " " + d.LastName, v == null ? null : v.PlateNumber);

    private static async Task<PageResult<T>> PageAsync<T>(IQueryable<T> query, PageRequest paging, CancellationToken token)
    {
        if (paging.Page is < 1 or > 1000000 || paging.PageSize is < 1 or > 100)
            throw new FleetFlowException("invalid_pagination", "Use page 1..1000000 and pageSize 1..100.");
        var count = await query.CountAsync(token);
        var items = await query.Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(token);
        return new PageResult<T>(items, paging.Page, paging.PageSize, count);
    }
}
