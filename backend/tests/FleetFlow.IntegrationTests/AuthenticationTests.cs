using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetFlow.Api.Controllers;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using FleetFlow.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FleetFlow.IntegrationTests;

public sealed class AuthenticationTests(PostgreSqlFixture server) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly string database = "auth_" + Guid.NewGuid().ToString("N");
    private string connection = null!;
    private FleetFlowApiFactory factory = null!;
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(server.ConnectionString);
        await admin.OpenAsync(Token);
        await new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin).ExecuteNonQueryAsync(Token);
        connection = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database }.ConnectionString;
        factory = new(connection);
    }
    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        await using var admin = new NpgsqlConnection(server.ConnectionString); await admin.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }
    [Theory]
    [InlineData(FleetRoles.Admin, 201)]
    [InlineData(FleetRoles.Dispatcher, 201)]
    [InlineData(FleetRoles.Viewer, 403)]
    public async Task Role_matrix_is_enforced_by_http(string role, int writeStatus)
    {
        using var client = await TestAuthentication.ClientAsync(factory, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/shipments", Token)).StatusCode);
        using var write = await client.PostAsJsonAsync("/api/v1/customers", new { name = "Fictional customer", contactName = "Demo", email = "demo@example.test" }, Token);
        Assert.Equal(writeStatus, (int)write.StatusCode);
        Assert.Equal(role == FleetRoles.Admin ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/v1/users", Token)).StatusCode);
    }
    [Theory]
    [InlineData("dashboard/summary")]
    [InlineData("shipments")]
    [InlineData("customers")]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    [InlineData("users")]
    public async Task Anonymous_reads_require_authentication(string path)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/" + path, Token)).StatusCode);
    }
    [Fact]
    public async Task Login_logout_csrf_and_current_user_use_real_identity_cookies()
    {
        var account = await TestAuthentication.ProvisionAsync(factory);
        using var client = factory.CreateClient();
        using var missingCsrf = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.User.Email, password = account.Password }, Token);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        await TestAuthentication.LoginAsync(client, account.User.Email!, account.Password);
        var me = await client.GetFromJsonAsync<SessionUser>("/api/v1/auth/me", Token);
        Assert.Equal(account.User.Id, me!.Id);
        Assert.Equal(FleetRoles.Dispatcher, me.Role);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/logout", new { }, Token)).StatusCode);
        await TestAuthentication.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/logout", new { }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me", Token)).StatusCode);
    }
    [Fact]
    public async Task Invalid_passwords_lock_the_account_and_do_not_disclose_account_state()
    {
        var account = await TestAuthentication.ProvisionAsync(factory);
        using var client = factory.CreateClient(); await TestAuthentication.RefreshCsrfAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.User.Email, password = TestAuthentication.Password() }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.User.Email, password = account.Password }, Token)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync(account.User.Id.ToString()))!));
    }
    [Fact]
    public async Task Login_is_rate_limited_even_for_nonexistent_accounts()
    {
        await TestAuthentication.ProvisionAsync(factory);
        using var client = factory.CreateClient(); await TestAuthentication.RefreshCsrfAsync(client);
        for (var i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "missing@example.test", password = TestAuthentication.Password() }, Token)).StatusCode);
        using var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "missing@example.test", password = TestAuthentication.Password() }, Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }
    [Fact]
    public async Task Admin_can_create_disable_and_reactivate_but_disabled_sessions_and_logins_fail()
    {
        using var admin = await TestAuthentication.ClientAsync(factory, FleetRoles.Admin);
        var email = "new@example.test"; var password = TestAuthentication.Password();
        using var created = await admin.PostAsJsonAsync("/api/v1/users", new { email, displayName = "New Viewer", role = FleetRoles.Viewer, password }, Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<SessionUser>(Token))!;
        using var browser = factory.CreateClient(); await TestAuthentication.LoginAsync(browser, email, password);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/activation", new { active = false }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/shipments", Token)).StatusCode);
        await TestAuthentication.RefreshCsrfAsync(browser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/activation", new { active = true }, Token)).StatusCode);
        await TestAuthentication.LoginAsync(browser, email, password);
        var self = (await admin.GetFromJsonAsync<SessionUser>("/api/v1/auth/me", Token))!;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/users/{self.Id}/activation", new { active = false }, Token)).StatusCode);
    }
    [Fact]
    public async Task Session_expires_using_timeprovider_and_logout_revokes_another_session()
    {
        var clock = new MutableClock(); await factory.DisposeAsync(); factory = new(connection, clock: clock);
        var account = await TestAuthentication.ProvisionAsync(factory);
        using var first = factory.CreateClient(); using var second = factory.CreateClient();
        await TestAuthentication.LoginAsync(first, account.User.Email!, account.Password);
        await TestAuthentication.LoginAsync(second, account.User.Email!, account.Password);
        await first.PostAsJsonAsync("/api/v1/auth/logout", new { }, Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        await TestAuthentication.LoginAsync(second, account.User.Email!, account.Password);
        clock.Now = clock.Now.AddMinutes(31);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/auth/me", Token)).StatusCode);
    }
    [Fact]
    public async Task Additive_migration_preserves_existing_logistics_data_and_seed_is_idempotent()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261106161244_InitialDomain", Token);
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync("Development", Token);
        var before = await db.Shipments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Version, x.ShipmentNumber }).ToArrayAsync(Token);
        var history = await db.ShipmentEvents.CountAsync(Token);
        await db.Database.MigrateAsync(Token);
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync("Development", Token);
        Assert.Equal(before, await db.Shipments.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Version, x.ShipmentNumber }).ToArrayAsync(Token));
        Assert.Equal(history, await db.ShipmentEvents.CountAsync(Token));
        Assert.Equal(3, await db.Roles.CountAsync(Token));
    }
    [Fact]
    public async Task Password_change_requires_current_password_and_revokes_other_sessions()
    {
        var account = await TestAuthentication.ProvisionAsync(factory);
        using var current = factory.CreateClient(); using var other = factory.CreateClient();
        await TestAuthentication.LoginAsync(current, account.User.Email!, account.Password);
        await TestAuthentication.LoginAsync(other, account.User.Email!, account.Password);
        var next = TestAuthentication.Password();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await current.PostAsJsonAsync("/api/v1/auth/password", new { currentPassword = TestAuthentication.Password(), newPassword = next }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.PostAsJsonAsync("/api/v1/auth/password", new { currentPassword = account.Password, newPassword = next }, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/auth/me", Token)).StatusCode);
        await TestAuthentication.LoginAsync(other, account.User.Email!, next);
    }

    [Theory]
    [InlineData("shipments")]
    [InlineData("customers")]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    public async Task Dispatcher_writes_without_csrf_are_rejected_without_updates(string resource)
    {
        using var client = await TestAuthentication.ClientAsync(factory);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/" + resource, new { }, Token)).StatusCode);
    }
    [Fact]
    public async Task Weak_passwords_and_invalid_roles_do_not_create_partial_accounts()
    {
        using var client = await TestAuthentication.ClientAsync(factory, FleetRoles.Admin);
        foreach (var value in new[] { new { role = "Unknown", password = TestAuthentication.Password() }, new { role = FleetRoles.Viewer, password = "weak-password" } })
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/v1/users", new { email = "rejected@example.test", displayName = "Rejected", value.role, value.password }, Token)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().Users.CountAsync(Token));
    }
    [Fact]
    public async Task Demo_users_are_development_only_idempotent_and_do_not_reset_credentials_or_activation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>(); await db.Database.MigrateAsync(Token);
        var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        foreach (var role in FleetRoles.All) configuration[$"DemoUsers:{role}Password"] = TestAuthentication.Password();
        var seed = scope.ServiceProvider.GetRequiredService<DevelopmentUsers>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => seed.SeedAsync("Production", Token));
        await seed.SeedAsync("Development", Token);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var viewer = (await users.FindByEmailAsync("viewer@fleetflow.example"))!;
        var hash = viewer.PasswordHash;
        Assert.False(string.Equals(configuration["DemoUsers:ViewerPassword"], hash, StringComparison.Ordinal));
        viewer.IsActive = false; await users.UpdateAsync(viewer);
        await seed.SeedAsync("Development", Token);
        Assert.Equal(3, await db.Users.CountAsync(Token));
        Assert.Equal(hash, viewer.PasswordHash); Assert.False(viewer.IsActive);
    }
    [Fact]
    public async Task Production_cookie_flags_are_httponly_secure_and_strict()
    {
        await factory.DisposeAsync(); factory = new(connection, "Production");
        var account = await TestAuthentication.ProvisionAsync(factory);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await TestAuthentication.RefreshCsrfAsync(client);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.User.Email, password = account.Password }, Token);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("FleetFlow.Session=", StringComparison.Ordinal));
        Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.True(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.True(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("POST", "shipments")]
    [InlineData("PUT", "shipments/11111111-1111-4111-8111-111111111111")]
    [InlineData("POST", "shipments/11111111-1111-4111-8111-111111111111/assign")]
    [InlineData("POST", "shipments/11111111-1111-4111-8111-111111111111/status")]
    [InlineData("POST", "drivers")]
    [InlineData("PUT", "drivers/11111111-1111-4111-8111-111111111111")]
    [InlineData("POST", "vehicles")]
    [InlineData("PUT", "vehicles/11111111-1111-4111-8111-111111111111")]
    [InlineData("POST", "users")]
    [InlineData("PUT", "users/11111111-1111-4111-8111-111111111111/activation")]
    public async Task Viewer_is_denied_on_every_mutation_before_domain_or_body_processing(string method, string path)
    {
        using var client = await TestAuthentication.ClientAsync(factory, FleetRoles.Viewer);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/" + path) { Content = JsonContent.Create(new { }) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request, Token)).StatusCode);
    }
    [Fact]
    public async Task Openapi_documents_authentication_success_contracts_and_csrf_headers()
    {
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Token);
        var paths = document.GetProperty("paths");
        foreach (var route in new[] { "login", "logout", "password" })
        {
            var operation = paths.GetProperty("/api/v1/auth/" + route).GetProperty("post");
            Assert.True(operation.GetProperty("responses").TryGetProperty("200", out _));
            Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        }
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("SessionUser", out _));
    }

    [Fact]
    public async Task Persisted_data_protection_keys_keep_existing_sessions_valid_after_host_restart()
    {
        var account = await TestAuthentication.ProvisionAsync(factory);
        string cookie;
        using (var first = factory.CreateClient())
        {
            await TestAuthentication.RefreshCsrfAsync(first);
            using var response = await first.PostAsJsonAsync("/api/v1/auth/login", new { email = account.User.Email, password = account.Password }, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("FleetFlow.Session=", StringComparison.Ordinal)).Split(';')[0];
        }
        await factory.DisposeAsync(); factory = new(connection);
        using var restored = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        using var current = await restored.GetAsync("/api/v1/auth/me", Token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(account.User.Id, (await current.Content.ReadFromJsonAsync<SessionUser>(Token))!.Id);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
