using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetFlow.Api.Contracts;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class HealthTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readiness_verifies_the_real_postgres_connection()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadFromJsonAsync<HealthResponse>(CancellationToken))!.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        var databaseName = await context.Database
            .SqlQueryRaw<string>("SELECT current_database() AS \"Value\"")
            .SingleAsync(CancellationToken);
        Assert.Equal("fleetflow_tests", databaseName);
    }

    [Fact]
    public async Task Liveness_remains_healthy_without_the_database()
    {
        await using var factory = new FleetFlowApiFactory(UnavailableConnectionString());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadFromJsonAsync<HealthResponse>(CancellationToken))!.Status);
    }

    [Fact]
    public async Task Unavailable_database_returns_safe_problem_details()
    {
        await using var factory = new FleetFlowApiFactory(UnavailableConnectionString());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var problem = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("dependency_unavailable", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Development_exposes_openapi_with_health_routes()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/health", out _));
        Assert.True(paths.TryGetProperty("/health/ready", out _));
    }

    [Fact]
    public async Task Production_does_not_expose_openapi()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString, "Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/does-not-exist", CancellationToken);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("http_error", problem.RootElement.GetProperty("code").GetString());
    }

    private string UnavailableConnectionString() => new NpgsqlConnectionStringBuilder(database.ConnectionString)
    {
        Port = 1,
        Timeout = 1,
        Pooling = false
    }.ConnectionString;
}
