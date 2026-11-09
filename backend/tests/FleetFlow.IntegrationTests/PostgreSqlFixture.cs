using DotNet.Testcontainers.Configurations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FleetFlow.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase("fleetflow_tests")
        .WithUsername("fleetflow_tests")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .WithResourceMapping(File.ReadAllBytes(TestCertificate.CertificatePath), "/tmp/fleetflow.crt", 999, 999, UnixFileModes.UserRead | UnixFileModes.UserWrite)
        .WithResourceMapping(File.ReadAllBytes(TestCertificate.PrivateKeyPath), "/tmp/fleetflow.key", 999, 999, UnixFileModes.UserRead | UnixFileModes.UserWrite)
        .WithCommand("-c", "ssl=on", "-c", "ssl_cert_file=/tmp/fleetflow.crt", "-c", "ssl_key_file=/tmp/fleetflow.key")
        .Build();

    public string ConnectionString => new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { SslMode = SslMode.VerifyFull, RootCertificate = TestCertificate.CertificatePath, MaxPoolSize = 10 }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();
        // Container-internal pg_isready can precede Docker Desktop's host-port forwarding.
        // Prove the exact host protocol/credentials used by tests before exposing the fixture.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync(deadline.Token);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync(deadline.Token);
                return;
            }
            catch (NpgsqlException) when (!deadline.IsCancellationRequested)
            {
                await Task.Delay(100, deadline.Token);
            }
        }
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}
