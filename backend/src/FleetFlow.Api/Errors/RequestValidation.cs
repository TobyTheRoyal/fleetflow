using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FleetFlow.Api.Errors;

internal static class RequestValidation
{
    public static IActionResult Invalid(ActionContext context)
    {
        // Never reflect serializer exceptions, rejected payload values or internal type names.
        var safeState = new ModelStateDictionary();
        foreach (var field in context.ModelState.Where(entry => entry.Value?.Errors.Count > 0))
            safeState.AddModelError(field.Key, "A required value is missing, invalid, or outside its allowed range.");
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, safeState, 400, "The request is invalid.");
        problem.Extensions["code"] = "invalid_request";
        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
