using System.Globalization;
using FleetFlow.Application.Insights;
using Microsoft.Extensions.Configuration;

namespace FleetFlow.Infrastructure.Insights;

public static class AiConfiguration
{
    public static AiSettings Read(IConfiguration config)
    {
        int Number(string key, int fallback) => config[key] is not { } value ? fallback : int.TryParse(value, out var n) ? n : -1;
        bool Flag(string key) => bool.TryParse(config[key], out var value) && value;
        var ids = (config["AI_ALLOWED_USER_IDS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var validIds = ids.All(x => Guid.TryParse(x, out _)) ? ids.Select(Guid.Parse).ToArray() : [];
        var until = DateTimeOffset.TryParse(config["AI_FREE_TIER_VERIFIED_UNTIL_UTC"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
            && date.Offset == TimeSpan.Zero ? date : (DateTimeOffset?)null;
        return new()
        {
            Enabled = Flag("AI_ENABLED"),
            Provider = config["AI_PROVIDER"] ?? "cloudflare",
            AccountId = config["CLOUDFLARE_ACCOUNT_ID"],
            ApiToken = config["CLOUDFLARE_API_TOKEN"],
            Model = config["CLOUDFLARE_AI_MODEL"] ?? AiSettings.ApprovedModel,
            FreeTierVerified = Flag("AI_FREE_TIER_VERIFIED"),
            UsageTermsVerified = Flag("AI_USAGE_TERMS_VERIFIED"),
            VerifiedModel = config["AI_FREE_TIER_VERIFIED_MODEL"],
            VerifiedUntilUtc = until,
            FictionalDataConfirmed = Flag("AI_FICTIONAL_DATA_CONFIRMED"),
            AllowedUserIds = validIds,
            UserDailyLimit = Number("AI_USER_DAILY_LIMIT", 3),
            GlobalDailyLimit = Number("AI_GLOBAL_DAILY_LIMIT", 20),
            TimeoutSeconds = Number("AI_TIMEOUT_SECONDS", 10),
            MaxOutputTokens = Number("AI_MAX_OUTPUT_TOKENS", 500),
            MaxInputBytes = Number("AI_MAX_INPUT_BYTES", 6000)
        };
    }
}
