using FleetFlow.Api.Errors;
using FleetFlow.Infrastructure;
using FleetFlow.Infrastructure.Persistence;

var migrate = args.Contains("--migrate");
var seed = args.Contains("--seed");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--seed")).ToArray());

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions.TryAdd("code", "http_error");
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (migrate || seed)
{
    await DevelopmentDatabase.InitializeAsync(app.Services, app.Environment.EnvironmentName, migrate, seed);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.Run();

// Allows WebApplicationFactory to start the real application in integration tests.
public partial class Program;
