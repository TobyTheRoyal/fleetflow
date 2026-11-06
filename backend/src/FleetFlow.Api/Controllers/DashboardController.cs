using FleetFlow.Api.Contracts;
using FleetFlow.Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[Route("api/v1/dashboard")]
public sealed class DashboardController(ILogisticsQueries queries) : ApiControllerBase
{
    [HttpGet("summary")]
    [EndpointSummary("Get live PostgreSQL-backed shipment counts and the 10 most recent shipments")]
    [EndpointDescription("totalNonCancelledShipments includes Delivered and excludes Cancelled. Overdue: deadline < asOfUtc, excluding Delivered/Cancelled. Due soon: asOfUtc ≤ deadline ≤ asOfUtc + 24 hours, excluding Delivered/Cancelled. Counts are aggregated in SQL. Recent shipments include all statuses, ordered by createdAtUtc descending then id descending. Counts and recent rows share a repeatable-read snapshot.")]
    [ProducesResponseType<DashboardResponse>(200)]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken token) => Ok(DashboardResponse.From(await queries.DashboardAsync(token)));
}
