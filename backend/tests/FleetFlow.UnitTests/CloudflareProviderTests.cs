using System.Net;
using System.Text.Json;
using FleetFlow.Application.Insights;
using FleetFlow.Domain.Shipments;
using FleetFlow.Infrastructure.Insights;
using Microsoft.Extensions.Configuration;

namespace FleetFlow.UnitTests;

public sealed class CloudflareProviderTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static AiSettings Settings(bool enabled = true, string model = AiSettings.ApprovedModel, bool verified = true) => new()
    {
        Enabled = enabled,
        Model = model,
        UsageTermsVerified = true,
        AccountId = new string('a', 32),
        ApiToken = "synthetic-test-token-never-sent",
        FreeTierVerified = verified,
        FictionalDataConfirmed = true,
        VerifiedModel = model,
        VerifiedUntilUtc = Clock.GetUtcNow().AddDays(1)
    };
    private static AiPrompt Prompt() => new(Clock.GetUtcNow(), [new("finding-1", FindingType.Overdue, FindingSeverity.High,
        new(ShipmentStatus.Planned, 2, Clock.GetUtcNow().AddDays(-1), Clock.GetUtcNow().AddHours(-1), true, true, true, true), [SuggestedAction.VerifyProgress])], ["Verified finding summary."]);
    private static string Body(string text = "{\"priorities\":[]}", string model = AiSettings.ApprovedModel, string finish = "stop") => JsonSerializer.Serialize(new
    {
        success = true,
        result = new { model, choices = new[] { new { finish_reason = finish, message = new { content = text } } } }
    });
    [Fact]
    public async Task Documented_REST_request_is_single_bounded_no_tools_and_token_is_header_only()
    {
        var handler = new Handler(HttpStatusCode.OK, Body());
        var provider = new CloudflareExplanationProvider(new HttpClient(handler), Settings(), Clock);
        await provider.GenerateAsync(Prompt(), TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/" + new string('a', 32) + "/ai/run/" + AiSettings.ApprovedModel, handler.Url);
        Assert.DoesNotContain("synthetic-test-token", handler.Url!);
        Assert.DoesNotContain("synthetic-test-token", handler.RequestBody!);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(json.RootElement.TryGetProperty("tools", out _));
        Assert.False(json.RootElement.TryGetProperty("gateway", out _));
        Assert.Equal(500, json.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(json.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.False(json.RootElement.GetProperty("store").GetBoolean());
        Assert.True(handler.HasTokenHeader);
    }
    [Theory]
    [InlineData(429, 3036, "ai_provider_budget_exhausted")]
    [InlineData(429, 3040, "ai_provider_rate_limited")]
    [InlineData(403, 5035, "ai_paid_plan_required")]
    [InlineData(404, 0, "ai_model_unavailable")]
    [InlineData(400, 0, "ai_model_unavailable")]
    [InlineData(500, 0, "ai_provider_unavailable")]
    [InlineData(403, 0, "ai_provider_unavailable")]
    [InlineData(302, 0, "ai_provider_unavailable")]
    public async Task HTTP_errors_never_retry_fallback_or_return_provider_details(int status, int code, string expected)
    {
        var body = JsonSerializer.Serialize(new { success = false, errors = new[] { new { code, message = "PRIVATE ERROR TOKEN CANARY" } } });
        var handler = new Handler((HttpStatusCode)status, body);
        var provider = new CloudflareExplanationProvider(new HttpClient(handler), Settings(), Clock);
        var error = await Assert.ThrowsAsync<AiFailure>(() => provider.GenerateAsync(Prompt(), TestContext.Current.CancellationToken));
        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("CANARY", error.Message);
        Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData("@cf/zai-org/glm-5.2", "stop", "ai_model_changed")]
    [InlineData(AiSettings.ApprovedModel, "length", "ai_invalid_output")]
    [InlineData(AiSettings.ApprovedModel, "content_filter", "ai_invalid_output")]
    public async Task Model_changes_and_truncated_outputs_are_rejected(string model, string finish, string code)
    {
        var handler = new Handler(HttpStatusCode.OK, Body(model: model, finish: finish));
        var provider = new CloudflareExplanationProvider(new HttpClient(handler), Settings(), Clock);
        Assert.Equal(code, (await Assert.ThrowsAsync<AiFailure>(() => provider.GenerateAsync(Prompt(), TestContext.Current.CancellationToken))).Code);
    }
    [Fact]
    public async Task Disabled_unverified_and_unapproved_models_send_zero_requests()
    {
        var handler = new Handler(HttpStatusCode.OK, Body());
        foreach (var settings in new[] { Settings(false), Settings(verified: false), Settings(model: "@cf/zai-org/glm-5.2") })
        {
            var provider = new CloudflareExplanationProvider(new HttpClient(handler, false), settings, Clock);
            await Assert.ThrowsAsync<AiFailure>(() => provider.GenerateAsync(Prompt(), TestContext.Current.CancellationToken));
        }
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task Oversized_response_is_rejected_before_deserialization()
    {
        var handler = new Handler(HttpStatusCode.OK, new string('x', 40000));
        var provider = new CloudflareExplanationProvider(new HttpClient(handler), Settings(), Clock);
        Assert.Equal("ai_invalid_output", (await Assert.ThrowsAsync<AiFailure>(() => provider.GenerateAsync(Prompt(), TestContext.Current.CancellationToken))).Code);
    }
    [Fact]
    public void Runtime_configuration_is_explicit_and_has_no_development_only_public_block()
    {
        var now = Clock.GetUtcNow();
        var values = new Dictionary<string, string?>
        {
            ["AI_ENABLED"] = "true",
            ["AI_PROVIDER"] = "cloudflare",
            ["CLOUDFLARE_ACCOUNT_ID"] = new string('a', 32),
            ["CLOUDFLARE_API_TOKEN"] = "synthetic-test-token",
            ["AI_FREE_TIER_VERIFIED"] = "true",
            ["AI_FREE_TIER_VERIFIED_MODEL"] = AiSettings.ApprovedModel,
            ["AI_FREE_TIER_VERIFIED_UNTIL_UTC"] = now.AddDays(1).ToString("O"),
            ["AI_USAGE_TERMS_VERIFIED"] = "true",
            ["AI_FICTIONAL_DATA_CONFIRMED"] = "true"
        };
        var settings = AiConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        Assert.Null(settings.Unavailable(now));
        Assert.Equal(AiSettings.ApprovedModel, settings.Model);
        Assert.Equal(3, settings.UserDailyLimit);
        Assert.Equal(20, settings.GlobalDailyLimit);
    }
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? Url, RequestBody;
        public bool HasTokenHeader;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Url = request.RequestUri!.AbsoluteUri; RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            HasTokenHeader = request.Headers.Authorization?.Scheme == "Bearer";
            return new(status) { Content = new StringContent(body) };
        }
    }
}
