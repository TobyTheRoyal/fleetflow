using FleetFlow.Application.Insights;
using FleetFlow.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FleetFlow.Api.Errors;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var failure = exception as FleetFlowException;
        var ai = exception as AiFailure;
        if (failure is null && ai is null)
            logger.LogError("Unexpected request failure. ExceptionType: {ExceptionType}. TraceId: {TraceId}",
                exception.GetType().Name, httpContext.TraceIdentifier);
        var status = ai is not null ? ai.Kind switch
        {
            AiFailureKind.Limit => 429,
            AiFailureKind.Conflict => 409,
            AiFailureKind.Validation => 422,
            AiFailureKind.Forbidden => 403,
            _ => 503
        } : (failure?.Kind switch
        {
            FailureKind.Validation => StatusCodes.Status422UnprocessableEntity,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        });
        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = failure is null && ai is null ? "An unexpected error occurred." : "The operation could not be completed.",
                Detail = failure?.Message ?? ai?.Message ?? "Please try again. If the problem persists, provide the trace identifier.",
                Extensions = { ["code"] = failure?.Code ?? ai?.Code ?? "unexpected_error" }
            }
        });
    }
}
