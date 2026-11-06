using System.Text.Json.Serialization;
using FleetFlow.Api.Errors;
using FleetFlow.Api.OpenApi;
using FleetFlow.Infrastructure;
using FleetFlow.Infrastructure.Persistence;

var migrate = args.Contains("--migrate");
var seed = args.Contains("--seed");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--seed")).ToArray());

// First-party OpenAPI reads HTTP JSON options independently of MVC's JSON options.
// Keep enum and unknown-property schemas consistent with the controller contract.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = RequestValidation.Invalid)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.JsonSerializerOptions.Converters.Add(new UtcTimestampConverter());
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    });
builder.Services.AddOpenApi(options =>
{
    options.AddOperationTransformer(ApiExamples.Apply);
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "FleetFlow REST API";
        document.Info.Version = "v1";
        document.Info.Description = "Local development API. JSON uses camelCase, named enums and UTC ISO 8601 timestamps. Shipment mutations require the current version; 409/stale_version means reload. Page responses include items/page/pageSize/totalCount/totalPages. Actor is server-controlled. No authentication: do not expose writable endpoints publicly.";
        return Task.CompletedTask;
    });
});
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
