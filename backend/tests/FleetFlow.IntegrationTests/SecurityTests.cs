using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FleetFlow.Application.Shipments;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FleetFlow.IntegrationTests;

public sealed class SecurityTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/health", 200)]
    [InlineData("/missing", 404)]
    [InlineData("/api/v1/shipments/not-a-guid", 400)]
    public async Task Operational_and_error_responses_cannot_be_cached_or_content_sniffed(string path, int status)
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, Token);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Unknown_hosts_are_rejected_instead_of_serving_the_local_api()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "untrusted.example";
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cross_origin_preflight_does_not_grant_permission_to_write()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/customers");
        request.Headers.Add("Origin", "https://untrusted.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var response = await client.SendAsync(request, Token);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Browser_simple_content_types_are_not_accepted_as_json_commands(string contentType)
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"name\":\"Untrusted input\"}", Encoding.UTF8, contentType);
        using var response = await client.PostAsync("/api/v1/customers", content, Token);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Unexpected_error_logs_and_response_do_not_contain_exception_data()
    {
        var logs = new SafeLogCapture();
        await using var factory = new FleetFlowApiFactory(database.ConnectionString, configureServices: services => services.AddLogging(builder => builder.AddProvider(logs)));
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IExceptionHandler>());
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "security-test-trace" };
        await using var body = new MemoryStream();
        context.Response.Body = body;
        const string sensitive = "Password=private-test-canary; contact=person@example.test; SELECT private_data";
        Assert.True(await handler.TryHandleAsync(context, new InvalidOperationException(sensitive), Token));
        Assert.Equal(500, context.Response.StatusCode);
        body.Position = 0;
        var response = await new StreamReader(body).ReadToEndAsync(Token);
        Assert.DoesNotContain("private-test-canary", response);
        var message = Assert.Single(logs.Messages);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("security-test-trace", message);
        Assert.DoesNotContain("private-test-canary", message);
        Assert.DoesNotContain("person@example.test", message);
    }

    [Fact]
    public async Task Db_context_and_application_services_are_scoped_while_the_clock_is_shared()
    {
        await using var factory = new FleetFlowApiFactory(database.ConnectionString);
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        Assert.Same(first.ServiceProvider.GetRequiredService<FleetFlowDbContext>(), first.ServiceProvider.GetRequiredService<FleetFlowDbContext>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<FleetFlowDbContext>(), second.ServiceProvider.GetRequiredService<FleetFlowDbContext>());
        Assert.Same(first.ServiceProvider.GetRequiredService<ShipmentService>(), first.ServiceProvider.GetRequiredService<ShipmentService>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<ShipmentService>(), second.ServiceProvider.GetRequiredService<ShipmentService>());
        Assert.Same(first.ServiceProvider.GetRequiredService<TimeProvider>(), second.ServiceProvider.GetRequiredService<TimeProvider>());
    }

    private sealed class SafeLogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.EndsWith("GlobalExceptionHandler", StringComparison.Ordinal) && IsEnabled(logLevel))
                    messages.Enqueue(formatter(state, exception) + exception?.ToString());
            }
        }
    }
}
