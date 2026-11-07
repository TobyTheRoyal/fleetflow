using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FleetFlow.Api.Contracts;
using FleetFlow.Api.Security;
using FleetFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Controllers;

[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(422)]
[ApiController, Authorize(Policy = SessionSecurity.Manage), Route("api/v1/users")]
public sealed class UsersController(UserAdministration administration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PageResponse<UserAccount>>(200)]
    public async Task<ActionResult<PageResponse<UserAccount>>> List([FromQuery] PagingQuery query, CancellationToken token) =>
        Ok(PageResponse<UserAccount>.Map(await administration.ListAsync(query.ToPage(), token), user => user));
    [HttpPost]
    [ProducesResponseType<UserAccount>(201)]
    public async Task<ActionResult<UserAccount>> Create(CreateUserRequest request, CancellationToken token) =>
        StatusCode(201, await administration.CreateAsync(new(request.Email, request.DisplayName, request.Role, request.Password), token));
    [HttpPut("{id}/activation")]
    [ProducesResponseType<UserAccount>(200)]
    public async Task<ActionResult<UserAccount>> Activate(Guid id, ActivationRequest request, CancellationToken token) =>
        Ok(await administration.ActivateAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), id, request.Active, token));
}
public sealed record CreateUserRequest([Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(1), MaxLength(150)] string DisplayName,
    [Required] string Role, [Required, MinLength(12), MaxLength(128)] string Password);
public sealed record ActivationRequest([property: System.Text.Json.Serialization.JsonRequired] bool Active);
