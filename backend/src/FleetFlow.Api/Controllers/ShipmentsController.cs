using FleetFlow.Api.Contracts;
using FleetFlow.Application.Queries;
using FleetFlow.Application.Shipments;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[Route("api/v1/shipments")]
public sealed class ShipmentsController(ShipmentService service, ILogisticsQueries queries) : ApiControllerBase
{
    [HttpGet]
    [EndpointSummary("List shipments with server-side filtering and pagination")]
    [EndpointDescription("Filters combine with AND. Literal case-insensitive search covers shipment number and origin/destination cities. Sort: createdAtUtc descending, id descending. page defaults to 1; pageSize defaults to 20, maximum 100. deadline supports Overdue or DueSoon using the same rules as the dashboard.")]
    [ProducesResponseType<PageResponse<ShipmentResponse>>(200)]
    public async Task<ActionResult<PageResponse<ShipmentResponse>>> List([FromQuery] ShipmentQuery query, CancellationToken token) =>
        Ok(PageResponse<ShipmentResponse>.Map(await queries.ShipmentsAsync(query.ToFilter(), token), ShipmentResponse.From));

    [HttpGet("{id}")]
    [EndpointSummary("Get shipment details including assignment and current version")]
    [ProducesResponseType<ShipmentResponse>(200)]
    public async Task<ActionResult<ShipmentResponse>> Get(Guid id, CancellationToken token) => Ok(ShipmentResponse.From(await queries.ShipmentAsync(id, token)));

    [HttpPost]
    [EndpointSummary("Create a Draft shipment and its Created event atomically")]
    [EndpointDescription("Supply customerId, route addresses, offset-qualified ISO 8601 timestamps, positive weight with at most two decimals and nonnegative palletCount. Deadline must be strictly after pickup. The server generates id, shipmentNumber, Draft status, timestamps, version and actor.")]
    [ProducesResponseType<ShipmentResponse>(201)]
    public async Task<ActionResult<ShipmentResponse>> Create(CreateShipmentRequest request, CancellationToken token)
    {
        var saved = await service.CreateAsync(request.ToDomain(), DemoActor, token);
        return CreatedAtAction(nameof(Get), new { id = saved.Id }, ShipmentResponse.From(saved));
    }

    [HttpPut("{id}")]
    [EndpointSummary("Update editable shipment fields using an expected version")]
    [EndpointDescription("Requires current positive version and all shipment fields. Draft fields are editable; Planned allows cargoDescription only. Other statuses are locked. Status and assignment are never writable through PUT. Stale version returns 409/stale_version. Successful changes increment version and record one DetailsChanged event.")]
    [ProducesResponseType<ShipmentResponse>(200)]
    public async Task<ActionResult<ShipmentResponse>> Update(Guid id, UpdateShipmentRequest request, CancellationToken token) =>
        Ok(ShipmentResponse.From(await service.UpdateAsync(id, request.Version, request.ToDomain(), DemoActor, token)));

    [HttpPost("{id}/assign")]
    [EndpointSummary("Assign one eligible driver and vehicle atomically")]
    [EndpointDescription("Requires current version, driverId and vehicleId. Only Draft/Planned allow assignment. Inactive/unavailable resources and overlapping active reservations return 409; payload overload returns 422. Draft selections do not reserve capacity until planning. Success increments version and records exactly one AssignmentChanged event.")]
    [ProducesResponseType<ShipmentResponse>(200)]
    public async Task<ActionResult<ShipmentResponse>> Assign(Guid id, AssignShipmentRequest request, CancellationToken token) =>
        Ok(ShipmentResponse.From(await service.AssignAsync(id, request.Version, request.DriverId, request.VehicleId, DemoActor, token)));

    [HttpPost("{id}/status")]
    [EndpointSummary("Apply a valid status transition with transactional history")]
    [EndpointDescription("Requires current version and named status. Allowed: Draft → Planned/Cancelled; Planned → InTransit/Cancelled; InTransit → Delivered. Planned/InTransit revalidate assignment. Pickup/delivery timestamps are server-controlled. Terminal, repeated or backwards transitions return 409; no duplicate event is added. Optional reason is limited to 1000 characters.")]
    [ProducesResponseType<ShipmentResponse>(200)]
    public async Task<ActionResult<ShipmentResponse>> Status(Guid id, ChangeStatusRequest request, CancellationToken token) =>
        Ok(ShipmentResponse.From(await service.ChangeStatusAsync(id, request.Version, request.Status, DemoActor, request.Reason, token)));

    [HttpGet("{id}/events")]
    [EndpointSummary("Read an immutable chronological page of shipment history")]
    [EndpointDescription("Sort: occurredAtUtc ascending, id ascending for equal timestamps. page defaults to 1; pageSize defaults to 20, maximum 100. Missing shipment returns 404. There are no event update/delete routes.")]
    [ProducesResponseType<PageResponse<ShipmentEventResponse>>(200)]
    public async Task<ActionResult<PageResponse<ShipmentEventResponse>>> Events(Guid id, [FromQuery] PagingQuery query, CancellationToken token) =>
        Ok(PageResponse<ShipmentEventResponse>.Map(await queries.EventsAsync(id, query.ToPage(), token), ShipmentEventResponse.From));
}
