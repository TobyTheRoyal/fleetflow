using Testcontainers.PostgreSql;

namespace FleetFlow.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase("fleetflow_tests")
        .WithUsername("fleetflow_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}
