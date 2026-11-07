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
        if (failure is null)
            logger.LogError("Unexpected request failure. ExceptionType: {ExceptionType}. TraceId: {TraceId}",
                exception.GetType().Name, httpContext.TraceIdentifier);
        var status = failure?.Kind switch
        {
            FailureKind.Validation => StatusCodes.Status422UnprocessableEntity,
            FailureKind.Conflict => StatusCodes.Status409Conflict,
            FailureKind.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status500InternalServerError
        };
        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = failure is null ? "An unexpected error occurred." : "The operation could not be completed.",
                Detail = failure?.Message ?? "Please try again. If the problem persists, provide the trace identifier.",
                Extensions = { ["code"] = failure?.Code ?? "unexpected_error" }
            }
        });
    }
}
