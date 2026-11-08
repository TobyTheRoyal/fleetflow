using System.Data;
using FleetFlow.Application.Insights;
using FleetFlow.Application.Queries;
using FleetFlow.Domain.Vehicles;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Insights;

public sealed class EfInsightQueries(FleetFlowDbContext db, TimeProvider clock) : IInsightQueries
{
    private sealed class FindingRow
    {
        public ShipmentFacts Facts { get; init; } = null!;
        public FindingType Type { get; init; }
        public FindingSeverity Severity { get; init; }
    }
    private IQueryable<FindingRow> Rows(DateTimeOffset now)
    {
        var facts = from s in db.Shipments.AsNoTracking()
                    join d in db.Drivers.AsNoTracking() on s.AssignedDriverId equals d.Id into drivers
                    from d in drivers.DefaultIfEmpty()
                    join v in db.Vehicles.AsNoTracking() on s.AssignedVehicleId equals v.Id into vehicles
                    from v in vehicles.DefaultIfEmpty()
                    select new ShipmentFacts
                    {
                        Id = s.Id,
                        Number = s.ShipmentNumber,
                        Version = s.Version,
                        Status = s.Status,
                        PickupUtc = s.PickupWindowStartUtc,
                        DeadlineUtc = s.DeliveryDeadlineUtc,
                        DriverAssigned = s.AssignedDriverId != null,
                        VehicleAssigned = s.AssignedVehicleId != null,
                        DriverEligible = d != null && d.Active,
                        VehicleEligible = v != null && v.Status == VehicleStatus.Available && v.MaxPayloadKg >= s.WeightKg
                    };
        IQueryable<FindingRow>? rows = null;
        foreach (var rule in OperationalRules.Definitions(now))
        {
            var type = rule.Type;
            var severity = rule.Severity;
            var branch = facts.Where(rule.Matches).Select(f => new FindingRow { Facts = f, Type = type, Severity = severity });
            rows = rows is null ? branch : rows.Concat(branch);
        }
        return rows!;
    }
    private static IOrderedQueryable<FindingRow> Ordered(IQueryable<FindingRow> rows) => rows
        .OrderByDescending(f => f.Severity).ThenBy(f => f.Facts.DeadlineUtc).ThenBy(f => f.Facts.Id).ThenBy(f => f.Type);
    public async Task<PageResult<OperationalFinding>> ListAsync(PageRequest paging, Guid? shipmentId, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var rows = Rows(now);
        if (shipmentId is { } id) rows = rows.Where(f => f.Facts.Id == id);
        var count = await rows.CountAsync(token);
        var items = await Ordered(rows).Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(token);
        await transaction.CommitAsync(token);
        return new(items.Select(x => OperationalRules.Finding(x.Facts, x.Type, x.Severity, now)).ToArray(), paging.Page, paging.PageSize, count);
    }
    public async Task<InsightSnapshot> SnapshotAsync(CancellationToken token)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var rows = Rows(now);
        var counts = await rows.GroupBy(f => f.Type).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(token);
        var affected = await rows.Select(f => f.Facts.Id).Distinct().CountAsync(token);
        var top = await Ordered(rows).Take(6).ToListAsync(token);
        await transaction.CommitAsync(token);
        var findings = top.Select(x => OperationalRules.Finding(x.Facts, x.Type, x.Severity, now)).ToArray();
        var byType = counts.ToDictionary(x => x.Type.ToString(), x => x.Count);
        int Count(FindingType type) => byType.GetValueOrDefault(type.ToString());
        var rules = OperationalRules.Definitions(now);
        var summary = new InsightSummary(now, counts.Sum(x => x.Count), affected, Count(FindingType.Overdue) + Count(FindingType.InTransitOverdue),
            Count(FindingType.DueSoon), counts.Where(c => rules.Single(r => r.Type == c.Type).Severity == FindingSeverity.Critical).Sum(c => c.Count),
            counts.Where(c => rules.Single(r => r.Type == c.Type).Severity == FindingSeverity.High).Sum(c => c.Count),
            byType, InsightJson.Fingerprint(findings, byType), findings.Length);
        return new(summary, findings);
    }
}
