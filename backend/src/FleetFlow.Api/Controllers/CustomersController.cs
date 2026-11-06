using FleetFlow.Api.Contracts;
using FleetFlow.Application.Fleet;
using FleetFlow.Application.Queries;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[Route("api/v1/customers")]
public sealed class CustomersController(FleetService service, ILogisticsQueries queries) : ApiControllerBase
{
    [HttpGet]
    [EndpointSummary("List customers ordered by company name and id")]
    [ProducesResponseType<PageResponse<CustomerResponse>>(200)]
    public async Task<ActionResult<PageResponse<CustomerResponse>>> List([FromQuery] PagingQuery query, CancellationToken token) =>
        Ok(PageResponse<CustomerResponse>.Map(await queries.CustomersAsync(query.ToPage(), token), CustomerResponse.From));

    [HttpPost]
    [EndpointSummary("Create a customer with validated contact details")]
    [ProducesResponseType<CustomerResponse>(201)]
    public async Task<ActionResult<CustomerResponse>> Create(CreateCustomerRequest request, CancellationToken token) =>
        StatusCode(201, CustomerResponse.From(await service.CreateCustomerAsync(request.ToInput(), token)));
}
