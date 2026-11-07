using FleetFlow.Api.Contracts;
using FleetFlow.Api.Security;
using FleetFlow.Application.Fleet;
using FleetFlow.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[Route("api/v1/drivers")]
public sealed class DriversController(FleetService service, ILogisticsQueries queries) : ApiControllerBase
{
    [HttpGet]
    [EndpointSummary("List drivers ordered by employee number and id")]
    [ProducesResponseType<PageResponse<DriverResponse>>(200)]
    public async Task<ActionResult<PageResponse<DriverResponse>>> List([FromQuery] PagingQuery query, CancellationToken token) =>
        Ok(PageResponse<DriverResponse>.Map(await queries.DriversAsync(query.ToPage(), token), DriverResponse.From));
    [HttpPost]
    [Authorize(Policy = SessionSecurity.Write)]
    [EndpointSummary("Create a driver with a unique normalized employee number")]
    [ProducesResponseType<DriverResponse>(201)]
    public async Task<ActionResult<DriverResponse>> Create(DriverRequest request, CancellationToken token) =>
        StatusCode(201, DriverResponse.From(await service.CreateDriverAsync(request.ToInput(), token)));
    [HttpPut("{id}")]
    [Authorize(Policy = SessionSecurity.Write)]
    [EndpointSummary("Update a driver without invalidating active assignments")]
    [EndpointDescription("Deactivation while assigned to a Planned/InTransit shipment returns 409/resource_in_use. Employee numbers are trimmed and uppercased; duplicates return 409/duplicate_value. Fleet updates serialize on the same resource row locks used by shipment assignment.")]
    [ProducesResponseType<DriverResponse>(200)]
    public async Task<ActionResult<DriverResponse>> Update(Guid id, DriverRequest request, CancellationToken token) =>
        Ok(DriverResponse.From(await service.UpdateDriverAsync(id, request.ToInput(), token)));
}
