using FleetFlow.Api.Contracts;
using FleetFlow.Api.Security;
using FleetFlow.Application.Fleet;
using FleetFlow.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[Route("api/v1/vehicles")]
public sealed class VehiclesController(FleetService service, ILogisticsQueries queries) : ApiControllerBase
{
    [HttpGet]
    [EndpointSummary("List vehicles ordered by normalized plate and id")]
    [ProducesResponseType<PageResponse<VehicleResponse>>(200)]
    public async Task<ActionResult<PageResponse<VehicleResponse>>> List([FromQuery] PagingQuery query, CancellationToken token) =>
        Ok(PageResponse<VehicleResponse>.Map(await queries.VehiclesAsync(query.ToPage(), token), VehicleResponse.From));
    [HttpPost]
    [Authorize(Policy = SessionSecurity.Write)]
    [EndpointSummary("Create a vehicle with positive decimal payload and a unique normalized plate")]
    [ProducesResponseType<VehicleResponse>(201)]
    public async Task<ActionResult<VehicleResponse>> Create(VehicleRequest request, CancellationToken token) =>
        StatusCode(201, VehicleResponse.From(await service.CreateVehicleAsync(request.ToInput(), token)));
    [HttpPut("{id}")]
    [Authorize(Policy = SessionSecurity.Write)]
    [EndpointSummary("Update a vehicle without invalidating active assignments")]
    [EndpointDescription("Unavailable status or insufficient capacity for a Planned/InTransit shipment returns 409/resource_in_use. Duplicate normalized plates return 409/duplicate_value. Resource updates use the existing reservation locking protocol.")]
    [ProducesResponseType<VehicleResponse>(200)]
    public async Task<ActionResult<VehicleResponse>> Update(Guid id, VehicleRequest request, CancellationToken token) =>
        Ok(VehicleResponse.From(await service.UpdateVehicleAsync(id, request.ToInput(), token)));
}
