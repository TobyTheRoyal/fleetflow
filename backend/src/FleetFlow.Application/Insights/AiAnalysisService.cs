using System.Text.Json;

namespace FleetFlow.Application.Insights;

public sealed class AiAnalysisService(IInsightQueries queries, IAiUsageStore usage, IAiExplanationProvider provider, AiSettings settings, TimeProvider clock)
{
    public async Task<AiAvailability> AvailabilityAsync(Guid userId, CancellationToken token)
    {
        var budget = await usage.BudgetAsync(userId, token);
        var failure = settings.Unavailable(clock.GetUtcNow());
        if (failure is null && !settings.AllowedUserIds.Contains(userId)) failure = ("ai_not_authorized", "This account is not explicitly authorized for external AI.");
        if (failure is null && budget.UserRemaining == 0) failure = ("ai_user_budget_exhausted", "Your UTC daily AI allowance is exhausted.");
        if (failure is null && budget.GlobalRemaining == 0) failure = ("ai_global_budget_exhausted", "The application's UTC daily AI allowance is exhausted.");
        if (failure is null && budget.InProgress) failure = ("ai_generation_in_progress", "An AI generation is already in progress.");
        if (failure is null && budget.RetryAfterUtc > clock.GetUtcNow()) failure = budget.ProviderBlockCode switch
        {
            "ai_provider_budget_exhausted" => ("ai_provider_budget_exhausted", "Cloudflare's free allocation is exhausted. Further inference is blocked until the next UTC day."),
            "ai_paid_plan_required" => ("ai_paid_plan_required", "Cloudflare requires a paid plan for this request. No upgrade or replacement is permitted; the owner must review free eligibility."),
            _ => ("ai_provider_cooldown", "The provider is cooling down after a failure. Try later; no retry is automatic.")
        };
        return new(failure is null, failure?.Code ?? "ai_available", failure?.Explanation ?? "On-demand AI is available within the configured limits. This is not a provider billing guarantee.",
            settings.Model, settings.VerifiedUntilUtc, budget);
    }

    public async Task<AiAnalysis> GenerateAsync(Guid userId, string expectedFingerprint, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        if (settings.Unavailable(now) is { } disabled) throw new AiFailure(AiFailureKind.Unavailable, disabled.Code, disabled.Explanation);
        if (!settings.AllowedUserIds.Contains(userId)) throw new AiFailure(AiFailureKind.Forbidden, "ai_not_authorized", "This account is not authorized for external AI.");
        var snapshot = await queries.SnapshotAsync(token);
        if (!string.Equals(snapshot.Summary.SnapshotFingerprint, expectedFingerprint, StringComparison.Ordinal))
            throw new AiFailure(AiFailureKind.Conflict, "insights_changed", "Operational findings changed. Refresh the overview before generating a new analysis.");
        if (snapshot.Findings.Count == 0) throw new AiFailure(AiFailureKind.Validation, "ai_no_findings", "There are no current operational findings to analyze.");
        var prompt = new AiPrompt(snapshot.Summary.AsOfUtc, snapshot.Findings.Select(f =>
            new AiCandidate(f.FindingId, f.Type, f.Severity, f.Evidence, OperationalRules.Actions(f.Type))).ToArray(), SummaryStatements(snapshot.Findings));
        if (JsonSerializer.SerializeToUtf8Bytes(prompt, InsightJson.Options).Length > settings.MaxInputBytes)
            throw new AiFailure(AiFailureKind.Validation, "ai_input_too_large", "The bounded analysis input exceeds its configured limit.");
        var reservation = await usage.ReserveAsync(userId, expectedFingerprint, settings.Model, token);
        AiSelection selection;
        DateTimeOffset generatedAtUtc;
        if (reservation.CachedSelection is { } cached)
        {
            selection = Validate(cached, snapshot.Findings);
            generatedAtUtc = reservation.CachedAtUtc ?? throw new InvalidOperationException("A cached analysis requires its persisted completion timestamp.");
        }
        else
        {
            try
            {
                if (settings.Unavailable(clock.GetUtcNow()) is { } changed) throw new AiFailure(AiFailureKind.Unavailable, changed.Code, changed.Explanation);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds), clock);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var output = await provider.GenerateAsync(prompt, linked.Token).WaitAsync(linked.Token);
                selection = Validate(output, snapshot.Findings);
                generatedAtUtc = await usage.CompleteAsync(reservation.RequestId!.Value, JsonSerializer.Serialize(selection, InsightJson.Options), null, CancellationToken.None);
            }
            catch (Exception exception)
            {
                var failure = exception as AiFailure ?? (exception is OperationCanceledException
                    ? new AiFailure(AiFailureKind.Unavailable, "ai_timeout", "AI generation timed out or was cancelled. The attempt remains counted; no retry was sent.")
                    : new AiFailure(AiFailureKind.Unavailable, "ai_provider_unavailable", "AI is temporarily unavailable. Operational findings remain available."));
                await usage.CompleteAsync(reservation.RequestId!.Value, null, failure.Code, CancellationToken.None);
                throw failure;
            }
        }
        var recommendations = selection.Priorities.Select(choice =>
        {
            var fact = snapshot.Findings.Single(f => f.FindingId == choice.FindingId);
            return new AiRecommendation(fact.FindingId, fact.ShipmentId, fact.ShipmentNumber, fact.Severity, fact.Explanation, OperationalRules.ActionText(choice.Action));
        }).ToArray();
        return new(generatedAtUtc, expectedFingerprint, settings.Model, reservation.CachedSelection is not null,
            "AI-assembled summary and priorities; validated fact statements and approved suggestions",
            string.Join(" ", selection.SummarySentences) + " Delay causes and arrival times are unknown.",
            snapshot.Findings.Count, snapshot.Summary.TotalFindings, recommendations, await usage.BudgetAsync(userId, token));
    }

    public static string[] SummaryStatements(IReadOnlyList<OperationalFinding> findings) => findings
        .GroupBy(f => f.Type).OrderByDescending(g => g.Max(f => f.Severity)).ThenBy(g => g.Key)
        .Select(g => $"The analysis sample contains {g.Count()} verified {(g.Count() == 1 ? "finding" : "findings")} for {g.Key switch
        {
            FindingType.Overdue => "past delivery deadlines without recorded completion",
            FindingType.InTransitOverdue => "shipments recorded in transit past their deadlines",
            FindingType.DueSoon => "delivery deadlines within the next 24 hours",
            FindingType.PickupWindowOpen => "started pickup windows with status still Planned",
            FindingType.UnassignedDraft => "Draft shipments needing resource assignment",
            _ => "selected resources requiring eligibility review"
        }}.").ToArray();

    public static AiSelection Validate(string json, IReadOnlyList<OperationalFinding> findings)
    {
        try
        {
            if (json.Length > 6000) throw new JsonException();
            // JSON duplicate keys are rejected too; last-key-wins is ambiguous at a trust boundary.
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateKeys(document.RootElement);
            var output = JsonSerializer.Deserialize<AiSelection>(json, InsightJson.Options) ?? throw new JsonException();
            if (output.Priorities is null || output.Priorities.Count is < 1 or > 3 || output.Priorities.Select(c => c.FindingId).Distinct().Count() != output.Priorities.Count)
                throw new JsonException();
            var statements = SummaryStatements(findings);
            if (output.SummarySentences is null || output.SummarySentences.Count is < 1 or > 2
                || output.SummarySentences.Distinct().Count() != output.SummarySentences.Count
                || output.SummarySentences.Any(s => !statements.Contains(s)) || !output.SummarySentences.Contains(statements[0]))
                throw new JsonException();
            var severity = findings.Max(f => f.Severity);
            foreach (var choice in output.Priorities)
            {
                var finding = findings.SingleOrDefault(f => f.FindingId == choice.FindingId);
                if (finding is null || !OperationalRules.Actions(finding.Type).Contains(choice.Action) || finding.Severity > severity)
                    throw new JsonException();
                if (choice == output.Priorities[0] && finding.Severity != severity) throw new JsonException();
                severity = finding.Severity;
            }
            return output;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw new AiFailure(AiFailureKind.Unavailable, "ai_invalid_output", "The model returned an invalid or unsupported analysis. Its text was not displayed.");
        }
    }
    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item);
    }
}
