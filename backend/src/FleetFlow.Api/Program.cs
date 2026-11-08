using System.Text.Json.Serialization;
using FleetFlow.Api.Errors;
using FleetFlow.Api.OpenApi;
using FleetFlow.Api.Realtime;
using FleetFlow.Api.Security;
using FleetFlow.Application.Realtime;
using FleetFlow.Infrastructure;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;

var migrate = args.Contains("--migrate");
var seedUsers = args.Contains("--seed-users");
var seed = args.Contains("--seed");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--seed" or "--seed-users")).ToArray());

// First-party OpenAPI reads HTTP JSON options independently of MVC's JSON options.
// Keep enum and unknown-property schemas consistent with the controller contract.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddControllers(options => options.Filters.AddService<CsrfFilter>())
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
        document.Info.Description = "Local development API. JSON uses camelCase, named enums and UTC ISO 8601 timestamps. Shipment mutations require the current version; 409/stale_version means reload. Page responses include items/page/pageSize/totalCount/totalPages. Actor is server-controlled. Cookie authentication; unsafe requests also require X-CSRF-TOKEN from /api/v1/auth/csrf. Roles: Admin, Dispatcher, Viewer. Local development only.";
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

builder.Services.AddSessionSecurity(builder.Environment.IsDevelopment());
builder.Services.AddScoped<DevelopmentUsers>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = false;
    options.MaximumReceiveMessageSize = 4096;
});
builder.Services.AddSingleton<LiveConnections>();
builder.Services.AddHostedService<LiveSessionMonitor>();
builder.Services.AddSingleton<ILogisticsNotifier, SignalRLogisticsNotifier>();
var serveSpa = builder.Configuration.GetValue<bool>("SERVE_SPA");
if (serveSpa && int.TryParse(builder.Configuration["PORT"], out var port) && port is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();

if (migrate || seed || seedUsers)
{
    await DevelopmentDatabase.InitializeAsync(app.Services, app.Environment.EnvironmentName, migrate, seed);
    if (seedUsers)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentUsers>().SeedAsync(app.Environment.EnvironmentName, default);
    }
    return;
}

// Responses contain operational data, including on failure. Do not let clients cache it
// or interpret a JSON response as executable content. OnStarting also covers handled errors.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await next(context);
});
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseWebSockets();
if (serveSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(HubSecurity.GuardAsync);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapHub<LogisticsHub>(HubSecurity.Path, options => options.CloseOnAuthenticationExpiration = true);
if (serveSpa)
{
    // Known SPA routes only: unknown API/Hub URLs must never return index.html.
    foreach (var route in new[] { "/login", "/insights", "/customers", "/drivers", "/vehicles", "/users", "/shipments/{*path:nonfile}" })
        app.MapFallbackToFile(route, "index.html").AllowAnonymous();
}
app.Run();

// Allows WebApplicationFactory to start the real application in integration tests.
public partial class Program;
