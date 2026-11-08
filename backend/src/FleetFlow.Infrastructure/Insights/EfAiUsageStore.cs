using FleetFlow.Application.Insights;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Insights;

public sealed class EfAiUsageStore(FleetFlowDbContext db, AiSettings settings, TimeProvider clock) : IAiUsageStore
{
    public async Task<AiBudget> BudgetAsync(Guid userId, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var counts = await db.AiAttempts.AsNoTracking().Where(x => x.DayUtc == day).GroupBy(_ => 1)
            .Select(g => new { Global = g.Count(), User = g.Count(x => x.UserId == userId) }).SingleOrDefaultAsync(token);
        var busy = await db.AiAttempts.AsNoTracking().AnyAsync(x => x.CompletedAtUtc == null && x.LeaseExpiresAtUtc > now, token);
        var retry = await db.AiAttempts.AsNoTracking().Where(x => x.RetryAfterUtc > now).OrderByDescending(x => x.RetryAfterUtc)
            .Select(x => new { x.RetryAfterUtc, x.ErrorCode }).FirstOrDefaultAsync(token);
        return new(settings.UserDailyLimit, Math.Max(0, settings.UserDailyLimit - (counts?.User ?? 0)), settings.GlobalDailyLimit,
            Math.Max(0, settings.GlobalDailyLimit - (counts?.Global ?? 0)), new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), busy, retry?.RetryAfterUtc, retry?.ErrorCode);
    }
    public async Task<AiReservation> ReserveAsync(Guid userId, string fingerprint, string model, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // This lock covers all instances sharing PostgreSQL. It is released before network inference.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741062100)", token);
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        await db.AiAttempts.Where(x => x.DayUtc < day.AddDays(-30)).ExecuteDeleteAsync(token);
        await db.AiAttempts.Where(x => x.CompletedAtUtc == null && x.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.CompletedAtUtc, now).SetProperty(a => a.ErrorCode, "ai_abandoned"), token);
        var cache = await db.AiAttempts.AsNoTracking().Where(x => x.UserId == userId && x.Fingerprint == fingerprint && x.Model == model
            && x.SelectionJson != null && x.CacheExpiresAtUtc > now).OrderByDescending(x => x.StartedAtUtc)
            .Select(x => new { x.SelectionJson, x.CompletedAtUtc }).FirstOrDefaultAsync(token);
        if (cache is not null)
        {
            await transaction.CommitAsync(token);
            return new(null, cache.SelectionJson, cache.CompletedAtUtc);
        }
        var budget = await BudgetAsync(userId, token);
        if (budget.UserRemaining == 0) throw new AiFailure(AiFailureKind.Limit, "ai_user_budget_exhausted", "Your UTC daily AI allowance is exhausted. Deterministic insights remain available.");
        if (budget.GlobalRemaining == 0) throw new AiFailure(AiFailureKind.Limit, "ai_global_budget_exhausted", "The application's UTC daily AI allowance is exhausted. Deterministic insights remain available.");
        // One global in-flight generation is deliberately stricter than one per user.
        if (budget.InProgress) throw new AiFailure(AiFailureKind.Limit, "ai_generation_in_progress", "An AI generation is already running. No additional request was sent.");
        if (budget.RetryAfterUtc > now) throw new AiFailure(AiFailureKind.Unavailable,
            budget.ProviderBlockCode is "ai_provider_budget_exhausted" or "ai_paid_plan_required" ? budget.ProviderBlockCode : "ai_provider_cooldown",
            "The AI provider is unavailable within the free policy. No additional request was sent; deterministic insights remain available.");
        var attempt = new AiAttempt
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DayUtc = day,
            StartedAtUtc = now,
            LeaseExpiresAtUtc = now.AddSeconds(settings.TimeoutSeconds + 30),
            Fingerprint = fingerprint,
            Model = model
        };
        db.AiAttempts.Add(attempt);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        db.Entry(attempt).State = EntityState.Detached;
        return new(attempt.Id, null, null);
    }
    public Task CompleteAsync(Guid id, string? selection, string? errorCode, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var retryAt = errorCode is "ai_provider_budget_exhausted" or "ai_paid_plan_required"
            ? new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero) : now.AddMinutes(1);
        return db.AiAttempts.Where(x => x.Id == id && x.CompletedAtUtc == null).ExecuteUpdateAsync(x => x
            .SetProperty(a => a.CompletedAtUtc, now).SetProperty(a => a.SelectionJson, selection).SetProperty(a => a.ErrorCode, errorCode)
            .SetProperty(a => a.CacheExpiresAtUtc, selection == null ? (DateTimeOffset?)null : now.AddMinutes(5))
            .SetProperty(a => a.RetryAfterUtc, errorCode == null ? (DateTimeOffset?)null : retryAt), token);
    }
}
