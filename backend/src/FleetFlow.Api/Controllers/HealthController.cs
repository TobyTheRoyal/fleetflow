using FleetFlow.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FleetFlow.Api.Controllers;

[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[ApiController]
[Route("health")]
public sealed class HealthController(HealthCheckService healthChecks) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse("Healthy"));

    [HttpGet("ready")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Ready(CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"), cancellationToken);

        if (report.Status == HealthStatus.Healthy)
        {
            return Ok(new HealthResponse("Healthy"));
        }

        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "A required dependency is unavailable.",
            detail: "The application cannot currently connect to its database. Please try again shortly.");
        problem.Extensions["code"] = "dependency_unavailable";
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }
}
