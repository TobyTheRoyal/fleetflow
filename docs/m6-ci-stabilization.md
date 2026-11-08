# M6 follow-up — CI timestamp precision and one live inference

Verification date: **2026-11-09 (Europe/Vienna)**. This is a targeted M6 follow-up; no new milestone, deployment, commit or push was performed.

## GitHub failure and root cause

The [reported Actions run](https://github.com/TobyTheRoyal/fleetflow/actions/runs/42532315201) was inspected through GitHub's jobs/logs API. Frontend and all three browser jobs succeeded. The Ubuntu 24.04 backend job built/formatted/verified the EF model successfully, then failed one of 297 tests:

`InsightTests.Successful_AI_is_bounded_cached_sanitized_and_never_mutates_business_data`, previously line 165:

```text
Expected: 2026-11-08T22:53:13.6482405+00:00
Actual:   2026-11-08T22:53:13.6482400+00:00
```

.NET represents `DateTimeOffset` in 100 ns ticks; PostgreSQL `timestamptz` has microsecond resolution. The final five ticks were lost during the database round trip. A macOS clock often returning microsecond-aligned values had hidden this defect. This is a persistence/response contract issue, not a reason to loosen the equality assertion. Sources: [Npgsql datetime mapping](https://www.npgsql.org/doc/types/datetime.html), [PostgreSQL timestamp precision](https://www.postgresql.org/docs/current/datatype-datetime.html), [Microsoft tick units](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.ticks?view=net-10.0).

A second issue existed independently: the first response read `TimeProvider` again after completion; cached responses used `CompletedAtUtc`. A real advancing clock could therefore produce different timestamps even without sub-microsecond precision.

## Exact fix

- `EfAiUsageStore` truncates ledger start/completion instants to whole UTC microseconds **before** persistence. Lease/cache/retry instants derived from them remain representable. UTC-day boundaries are not rounded forward.
- `IAiUsageStore.CompleteAsync` now returns the canonical completion instant after successfully persisting it. It refuses to report completion if the attempt was not updated.
- `AiAnalysisService` returns that exact instant for fresh analyses and the corresponding persisted instant for cached analyses. It no longer makes a second generation-time clock read. Database-specific normalization stays in Infrastructure; Application depends only on the semantic completion contract.
- The original integration test is expanded to five deterministic cases: aligned ticks; remainders 1, 5 and 9; and a 17 ms advancing clock with a sub-microsecond remainder. Assertions additionally compare fresh output to the stored completion instant, require UTC/microsecond alignment and compare the serialized fresh/cache timestamp strings exactly. Original bounded/sanitized/cache/no-business-mutation checks remain intact.

No public REST DTO, business rule, package, database schema or migration changed. No test tolerance, assertion removal, browser exclusion or test retry conceals the issue.

## Regression evidence

Before applying the source fix, the new deterministic cases reproduced the issue on macOS: **1 passed / 4 failed**. One advancing-clock failure differed by 17 ms as well as the fractional ticks. After the fix:

| Verification | Actual result |
| --- | --- |
| Affected method, macOS + real PostgreSQL | 5 complete runs × 5 cases = **25 passed**, no failures/skips |
| Affected method, local Linux .NET SDK container + real PostgreSQL | 3 runs × 5 cases = **15 passed**, no failures/skips |
| Complete Release build | Passed, 0 warnings/errors |
| Complete backend tests | **301 passed**: 120 unit + 181 real PostgreSQL integration; 0 failed/skipped |
| .NET format | Passed |
| EF pending-model check | No pending changes; no migration needed |
| Frontend check | Lint, format, TypeScript, **95 Vitest tests**, 2 infrastructure tests and production build passed |
| Browser suite | **87 passed**: Chromium 29, Firefox 29, WebKit 29; 0 failed/skipped, including authentication, live collaboration, insights and axe accessibility |
| Docker | Normal API and same-origin web images built; updated normal Compose API served the authenticated live workflow against the existing database |
| Dependencies | npm: 0 vulnerabilities reported; NuGet: 0 known package/advisory findings |
| Helpers/workflow | 4 Python tests, `actionlint`, public-file guard and whitespace check passed |

The Linux repeat uses the existing compiled test assembly in the official .NET 10.0.403 SDK image, with a read-only mount and isolated PostgreSQL Testcontainers. It is an actual local Linux execution, **not** a new GitHub-hosted run. No production provider credentials enter unit/integration/browser tests.

## Cloudflare configuration and manual authorization

The initial check found that Account ID and API token were present and propagated correctly through `.env` → Compose → the running normal API, but inference, terms/free-tier/fictional-data confirmations, review expiry and user allowlist were not yet enabled. No inference was sent in that state.

The owner explicitly confirmed Workers Free without paid plans/credits, current Austria/EEA model/usage conditions, the fictional dataset and permission to authorize the existing Demo Dispatcher for seven days. Only the ignored local `.env` was updated with those approvals, the real active Dispatcher Identity ID and the explicitly selected free model. Credentials and quota limits were preserved. The normal API container was recreated to read the configuration; no external account/billing setting was changed.

- Provider: `cloudflare`; model: `@cf/zai-org/glm-4.7-flash`.
- The model is still explicitly listed for [Workers Free](https://developers.cloudflare.com/changelog/post/2026-07-28-models-require-workers-paid/). Current [pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/) documents 10,000 shared account Neurons/UTC day. This does not promise permanent free availability or replace account-specific manual verification.
- Review expiry: **2026-11-15 23:25:11 UTC / 2026-11-16 00:25:11 Europe/Vienna**. Review renewal remains manual.
- Limits remain 3 attempts/user/UTC day, 20 globally, one global in-flight generation, 500 output tokens and the existing short timeout/input bounds. No retries, paid upgrades, credits or fallback models/providers were added.
- The local model had originally relied on the correct Compose default. The live-request preflight stopped before any login/inference until the same approved model was also explicitly recorded in `.env`.

No actual Account ID, API token, password, cookie or CSRF token was printed in this follow-up or written to tracked files. The public-file guard checks the current local credentials against public Git candidates.

## Exactly one real Cloudflare inference

One authenticated `POST /api/v1/insights/ai-analysis` was sent through the **normal FleetFlow API**, using the existing fictional logistics dataset and Identity Dispatcher session with a real CSRF token. A private/ignored exclusive marker was written before that POST to prevent accidental reruns. There was no inference retry and no second generation request.

Result:

- **HTTP 200**, fresh/non-cached response from `@cf/zai-org/glm-4.7-flash`.
- Strict server-side output validation passed: grounded summary of six sampled critical in-transit-overdue findings and **three** evidence-linked recommendations.
- Example verified shipment: fictional `FF-2026-000013`; suggested action: verify actual progress with the dispatcher before recording any operational change. No cause, GPS location or ETA was invented.
- Exactly **one** persisted usage attempt, completed without an error code.
- Personal allowance **3 → 2**; global allowance **20 → 19**, for that UTC day. These are observations immediately after the call, not permanent balances.
- The returned generation timestamp exactly matched the stored completion instant.
- Before/after hashes of all logistics business tables matched; no customers, drivers, vehicles, shipments or events changed.
- No provider credentials appeared in the API response. API authentication and CSRF were exercised, not bypassed.

Live provider failure modes were not induced because doing so would require additional real requests. Timeout/rate-limit/invalid-output/paid-required/unavailable handling remains covered by deterministic fake-provider tests in the full backend suite.

Sanitized local evidence is ignored under `artifacts/m6/` (including `cloudflare-live-result.json`) and `/private/tmp/fleetflow-ci-precision-*.log`. No provider key or real inference is needed in CI. External inference is now enabled locally for the explicitly approved demo account until the review expires; only deliberate user actions can spend additional remaining allowance.

## Readiness and limitations

The timestamp fix is ready for the next GitHub CI run based on deterministic macOS/Linux regression evidence and the full local checks. The corrected revision has **not** been committed, pushed or run on GitHub by this task. After your normal review/push, verify all five jobs for that exact revision; rerunning the old revision alone does not apply the fix.

Application budgets do not prove the provider account is free or guarantee quota availability; the owner confirmation remains a manual control. No payment method, billing activation, paid resource, public deployment or next milestone was initiated. Existing public-hosting/security/privacy limitations remain documented in [the AI guide](insights.md) and [zero-cost deployment preparation](../ZERO_COST_DEPLOYMENT.md).
