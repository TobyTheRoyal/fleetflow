using FleetFlow.Infrastructure.Portfolio;
using Microsoft.Extensions.Configuration;

namespace FleetFlow.UnitTests;

public sealed class PortfolioSettingsTests
{
    [Theory]
    [InlineData("ViewerSlots", "11")]
    [InlineData("ViewerSlots", "0")]
    [InlineData("DispatcherSlots", "4")]
    [InlineData("LeaseMinutes", "31")]
    [InlineData("LeaseMinutes", "0")]
    [InlineData("MaxWrites", "101")]
    [InlineData("EntryPerMinute", "31")]
    public void Unsupported_pool_and_abuse_limits_fail_closed(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [$"Portfolio:{key}"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => PortfolioSettings.Read(configuration));
    }
    [Fact]
    public void Default_normal_mode_has_no_demo_scope_and_public_mode_starts_denied()
    {
        Assert.Null(new PortfolioScope(new()).Schema);
        Assert.Equal("portfolio_denied", new PortfolioScope(new() { Enabled = true }).Schema);
    }
    [Theory]
    [InlineData("public")]
    [InlineData("portfolio_dispatch_4")]
    [InlineData("portfolio_dispatch_1\"; DROP TABLE")]
    public void Arbitrary_schema_names_are_rejected(string schema) => Assert.Throws<InvalidOperationException>(() => PortfolioScope.ForSchema(schema));
    [Fact]
    public void A_valid_slot_cannot_be_bound_to_another_sandbox()
    {
        var scope = new PortfolioScope(new() { Enabled = true });
        Assert.Throws<InvalidOperationException>(() => scope.Bind(new() { Id = 11, Role = "Dispatcher", SchemaName = "portfolio_dispatch_2" }));
    }
}
