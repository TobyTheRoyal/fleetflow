using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetFlow.Application.Queries;

namespace FleetFlow.Application.Insights;

public sealed record InsightSummary(DateTimeOffset AsOfUtc, int TotalFindings, int AffectedShipments, int OverdueShipments,
    int DueSoonShipments, int CriticalFindings, int HighFindings, IReadOnlyDictionary<string, int> FindingsByType,
    string SnapshotFingerprint, int AiSampleSize);
public sealed record InsightSnapshot(InsightSummary Summary, IReadOnlyList<OperationalFinding> Findings);
public interface IInsightQueries
{
    Task<PageResult<OperationalFinding>> ListAsync(PageRequest paging, Guid? shipmentId, CancellationToken token);
    Task<InsightSnapshot> SnapshotAsync(CancellationToken token);
}
public enum AiFailureKind { Unavailable, Limit, Conflict, Validation, Forbidden }
public sealed class AiFailure(AiFailureKind kind, string code, string message) : Exception(message)
{
    public AiFailureKind Kind { get; } = kind;
    public string Code { get; } = code;
}
public sealed record AiBudget(int UserLimit, int UserRemaining, int GlobalLimit, int GlobalRemaining, DateTimeOffset ResetsAtUtc, bool InProgress, DateTimeOffset? RetryAfterUtc, string? ProviderBlockCode = null);
public sealed record AiReservation(Guid? RequestId, string? CachedSelection, DateTimeOffset? CachedAtUtc);
public interface IAiUsageStore
{
    Task<AiBudget> BudgetAsync(Guid userId, CancellationToken token);
    Task<AiReservation> ReserveAsync(Guid userId, string fingerprint, string model, CancellationToken token);
    // Returns the canonical persisted completion instant, also used by cached responses.
    Task<DateTimeOffset> CompleteAsync(Guid id, string? selection, string? errorCode, CancellationToken token);
}
public interface IAiExplanationProvider
{
    Task<string> GenerateAsync(AiPrompt prompt, CancellationToken token);
}
public sealed record AiCandidate(string FindingId, FindingType Type, FindingSeverity Severity, FindingEvidence Evidence, SuggestedAction[] AllowedActions);
public sealed record AiPrompt(DateTimeOffset AsOfUtc, IReadOnlyList<AiCandidate> Candidates, IReadOnlyList<string> AllowedSummarySentences);
public sealed record AiChoice([property: JsonRequired] string FindingId, [property: JsonRequired] SuggestedAction Action);
public sealed record AiSelection([property: JsonRequired] IReadOnlyList<AiChoice> Priorities, [property: JsonRequired] IReadOnlyList<string> SummarySentences);
public sealed record AiRecommendation(string FindingId, Guid ShipmentId, string ShipmentNumber, FindingSeverity Severity, string VerifiedExplanation, string SuggestedAction);
public sealed record AiAnalysis(DateTimeOffset GeneratedAtUtc, string SnapshotFingerprint, string Model, bool Cached,
    string Label, string Summary, int SampleSize, int TotalFindings, IReadOnlyList<AiRecommendation> Recommendations, AiBudget Budget);
public sealed record AiAvailability(bool CanGenerate, string Code, string Explanation, string Model, DateTimeOffset? VerificationExpiresAtUtc, AiBudget Budget);

public static class InsightJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };
    public static string Fingerprint(IReadOnlyList<OperationalFinding> findings, IReadOnlyDictionary<string, int> counts, string? contextKey = null) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            ContractVersion = "m7-scoped-summary-v1",
            ContextKey = contextKey,
            Counts = counts.OrderBy(x => x.Key),
            Findings = findings.Select(f => new { f.FindingId, f.Severity, f.Evidence, f.ShipmentNumber })
        }, Options)));
}
