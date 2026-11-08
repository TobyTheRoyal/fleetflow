namespace FleetFlow.Application.Insights;

public sealed class AiSettings
{
    public const string ApprovedModel = "@cf/zai-org/glm-4.7-flash";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "cloudflare";
    public string Model { get; init; } = ApprovedModel;
    public string? AccountId { get; init; }
    public string? ApiToken { get; init; }
    public bool FreeTierVerified { get; init; }
    public bool UsageTermsVerified { get; init; }
    public bool FictionalDataConfirmed { get; init; }
    public string? VerifiedModel { get; init; }
    public DateTimeOffset? VerifiedUntilUtc { get; init; }
    public Guid[] AllowedUserIds { get; init; } = [];
    public int UserDailyLimit { get; init; } = 3;
    public int GlobalDailyLimit { get; init; } = 20;
    public int TimeoutSeconds { get; init; } = 10;
    public int MaxOutputTokens { get; init; } = 500;
    public int MaxInputBytes { get; init; } = 6000;

    public (string Code, string Explanation)? Unavailable(DateTimeOffset now)
    {
        if (!Enabled) return ("ai_disabled", "External AI is disabled. Verified operational findings remain available.");
        if (Provider != "cloudflare") return ("ai_provider_unsupported", "Only the explicitly configured Cloudflare Workers AI provider is supported.");
        if (Model != ApprovedModel) return ("ai_model_unavailable", "This model is not approved for this zero-cost integration. No replacement is selected automatically.");
        if (!FreeTierVerified || VerifiedModel != Model || VerifiedUntilUtc is null || VerifiedUntilUtc <= now || VerifiedUntilUtc > now.AddDays(7))
            return ("ai_free_tier_unverified", "An owner must verify Workers Free, this exact model, quotas and the absence of paid billing/credits, with a review valid for at most seven days.");
        if (!UsageTermsVerified) return ("ai_usage_terms_unverified", "An owner must verify current Cloudflare and model terms for the intended audience. Never enable paid services to bypass this check.");
        if (!FictionalDataConfirmed) return ("ai_fictional_data_unconfirmed", "External AI requires explicit confirmation that the dataset is entirely fictional.");
        if (string.IsNullOrWhiteSpace(ApiToken) || AccountId is null || AccountId.Length != 32 || !AccountId.All(char.IsAsciiHexDigit))
            return ("ai_credentials_missing", "The server requires a valid Cloudflare Account ID and API token.");
        if (UserDailyLimit is < 1 or > 3 || GlobalDailyLimit is < 1 or > 20 || TimeoutSeconds is < 1 or > 15
            || MaxOutputTokens is < 1 or > 500 || MaxInputBytes is < 512 or > 8000)
            return ("ai_configuration_invalid", "AI limits are outside the supported safety bounds.");
        return null;
    }
}
