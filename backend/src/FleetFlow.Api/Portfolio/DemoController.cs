using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FleetFlow.Api.Controllers;
using FleetFlow.Api.Security;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FleetFlow.Api.Portfolio;

[ApiController, Route("api/v1/demo")]
public sealed class DemoController(PortfolioSettings settings, PortfolioService portfolio, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) : ControllerBase
{
    [AllowAnonymous, HttpGet]
    [ProducesResponseType<DemoAvailability>(200), ProducesResponseType(404)]
    public async Task<ActionResult<DemoAvailability>> Availability(CancellationToken token) =>
        settings.Enabled ? Ok(await portfolio.AvailabilityAsync(token)) : NotFound();

    [AllowAnonymous, HttpPost("sessions"), EnableRateLimiting("demo-entry")]
    [RequestSizeLimit(2048)]
    [ProducesResponseType<SessionUser>(200)]
    [ProducesResponseType<ProblemDetails>(400), ProducesResponseType(404), ProducesResponseType<ProblemDetails>(409), ProducesResponseType<ProblemDetails>(422), ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<SessionUser>> Start(DemoStartRequest request, CancellationToken token)
    {
        if (!settings.Enabled) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return Conflict(new ProblemDetails { Status = 409, Detail = "End your current demo session before starting another.", Extensions = { ["code"] = "demo_already_active" } });
        var (user, slot) = await portfolio.StartAsync(request.Mode, token);
        await signIn.SignInWithClaimsAsync(user, new AuthenticationProperties { ExpiresUtc = slot.ExpiresAtUtc, IsPersistent = false, AllowRefresh = false },
            [new Claim("portfolio_lease", slot.LeaseId!.Value.ToString())]);
        return Ok(await SessionUser.From(user, users, new(slot.LeaseId.Value, slot.Role, slot.ExpiresAtUtc!.Value)));
    }
}
public sealed record DemoStartRequest([Required, MaxLength(20)] string Mode);
