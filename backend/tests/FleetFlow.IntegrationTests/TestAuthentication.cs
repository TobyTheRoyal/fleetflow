using System.Net.Http.Json;
using System.Security.Cryptography;
using FleetFlow.Api.Controllers;
using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FleetFlow.IntegrationTests;

internal static class TestAuthentication
{
    public static string Password() => "Ff!7" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
    public static async Task<(ApplicationUser User, string Password)> ProvisionAsync(FleetFlowApiFactory factory, string role = FleetRoles.Dispatcher)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FleetFlowDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = Password();
        var email = $"{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = "Test Dispatcher", CreatedAtUtc = DateTimeOffset.UtcNow };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (user, password);
    }
    public static async Task<HttpClient> ClientAsync(FleetFlowApiFactory factory, string role = FleetRoles.Dispatcher)
    {
        var account = await ProvisionAsync(factory, role);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await LoginAsync(client, account.User.Email!, account.Password);
        return client;
    }
    public static async Task RefreshCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
    }
    public static async Task LoginAsync(HttpClient client, string email, string password)
    {
        await RefreshCsrfAsync(client);
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await RefreshCsrfAsync(client);
    }
}
