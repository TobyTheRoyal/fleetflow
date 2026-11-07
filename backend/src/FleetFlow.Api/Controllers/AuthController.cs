using System.ComponentModel.DataAnnotations;
using FleetFlow.Api.Security;
using FleetFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FleetFlow.Api.Controllers;

[ApiController]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(429)]
[Route("api/v1/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery) : ControllerBase
{
    private static readonly string DummyHash = new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    [AllowAnonymous, HttpGet("csrf")]
    [ProducesResponseType<CsrfResponse>(200)]
    public ActionResult<CsrfResponse> Csrf() => Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!));

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    [ProducesResponseType<SessionUser>(200)]
    public async Task<ActionResult<SessionUser>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            _ = users.PasswordHasher.VerifyHashedPassword(user ?? new ApplicationUser(), user?.PasswordHash ?? DummyHash, request.Password);
            return InvalidLogin();
        }
        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded) return InvalidLogin();
        return Ok(await SessionUser.From(user, users));
    }

    [Authorize, HttpGet("me")]
    [ProducesResponseType<SessionUser>(200)]
    public async Task<ActionResult<SessionUser>> Me() => Ok(await SessionUser.From((await users.GetUserAsync(User))!, users));

    [Authorize, HttpPost("password"), EnableRateLimiting("login")]
    [ProducesResponseType<OperationResponse>(200)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<ActionResult<OperationResponse>> Password(ChangePasswordRequest request)
    {
        var user = (await users.GetUserAsync(User))!;
        if (!(await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true)).Succeeded)
            throw new FleetFlow.Domain.Common.FleetFlowException("invalid_password", "The current password could not be verified. Try again later or contact an administrator.");
        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded) throw new FleetFlow.Domain.Common.FleetFlowException("invalid_password", "The password could not be changed. Use 12–128 characters, six distinct characters, uppercase, lowercase, a digit and a symbol.");
        await signIn.RefreshSignInAsync(user);
        return Ok(new OperationResponse(true));
    }

    [Authorize, HttpPost("logout")]
    [ProducesResponseType<OperationResponse>(200)]
    public async Task<ActionResult<OperationResponse>> Logout()
    {
        // Invalidate copied cookies and other sessions as well as deleting this browser cookie.
        var user = (await users.GetUserAsync(User))!;
        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) throw FleetFlow.Domain.Common.FleetFlowException.Conflict("concurrent_operation", "Reload before trying again.");
        await signIn.SignOutAsync();
        return Ok(new OperationResponse(true));
    }

    private ObjectResult InvalidLogin() => new(new ProblemDetails
    {
        Status = 401,
        Title = "Sign-in failed.",
        Detail = "Email or password is incorrect, or the account is unavailable. Try again later or contact an administrator.",
        Extensions = { ["code"] = "invalid_credentials", ["traceId"] = HttpContext.TraceIdentifier }
    })
    { StatusCode = 401 };
}

public sealed record LoginRequest([Required, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
public sealed record ChangePasswordRequest([Required, MaxLength(128)] string CurrentPassword,
    [Required, MinLength(12), MaxLength(128)] string NewPassword);
public sealed record CsrfResponse(string Token);
public sealed record OperationResponse(bool Success);
public sealed record SessionUser(Guid Id, string Email, string DisplayName, string Role, bool Active)
{
    public static async Task<SessionUser> From(ApplicationUser user, UserManager<ApplicationUser> users) =>
        new(user.Id, user.Email!, user.DisplayName, (await users.GetRolesAsync(user)).Single(), user.IsActive);
}
