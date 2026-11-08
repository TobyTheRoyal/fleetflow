# M6 — Operational analytics and optional Cloudflare AI

Verified locally on **2026-11-08**, macOS Apple Silicon, .NET SDK 10.0.403, Node 24.22.2, PostgreSQL 18.6 in Docker and Playwright 1.64.1. No commit, push, deployment, provider account creation, payment activation or live inference call was performed. This report covers M6 only; M7 was not started.

## Scope and specification decisions

The current request replaces the originally proposed inference provider with **Cloudflare Workers AI**, direct official REST from the existing .NET backend. There is no Gemini integration/configuration left in application source, and no required local inference server, new microservice, Worker script or Gateway. The modular monolith, domain rules, logistics REST DTOs, Identity cookies/roles/CSRF/session checks and SignalR transport remain intact. Private project instructions were reviewed outside the repository and were neither changed nor copied into it.

A deliberate security trade-off is controlled-language AI output: the model selects exact verified fact sentences and approved evidence-linked dispatcher suggestions, rather than generating unrestricted prose. It prioritizes a bounded sample, not the whole database. This meets the explanation workflow while making unknown facts rejectable. The optional same-origin image supports later hosting preparation; it does not remove the existing public-deployment blockers.

## Implemented functionality

- SQL-backed, paginated findings: Overdue, InTransitOverdue, DueSoon, PickupWindowOpen, UnassignedDraft and AssignmentRequiresReview. Terminal shipments are excluded; no fabricated location, cause or ETA.
- Real counts, stable severity/deadline/ID ordering, UTC `TimeProvider` boundaries and a coherent snapshot/fingerprint.
- Authenticated `GET /api/v1/insights`, `/summary`, `/ai-availability`; allowlisted/CSRF-protected explicit `POST /api/v1/insights/ai-analysis`.
- Provider abstraction, typed fixed-host Cloudflare HttpClient, exact GLM-4.7-Flash model allowlist, bounded prompts/output, strict schema/fact/action validation and safe failures.
- Persistent UTC user/global budgets, atomic PostgreSQL reservations, one global in-flight lease, failed-attempt accounting, five-minute per-user result cache, provider exhaustion/day boundary and cooldown handling.
- Responsive **AI Insights** page: operational KPIs, prioritized evidence cards, shipment links/scoping, pagination/manual refresh, explicit generation, remaining allowances, stale-result label and disabled/unavailable/error/loading states.
- SignalR invalidates deterministic/availability GETs and reconnect recovery without generating AI or altering an active shipment edit snapshot.
- Additive `20261108214714_AddAiUsageLedger` migration. Existing Development seed behavior is unchanged/idempotent.
- Dedicated browser-test executable hosts the real API/Identity/CSRF/PostgreSQL with only external inference replaced. It refuses real provider credentials and is excluded from both normal application images.

## Actual verification results

| Check | Result |
| --- | --- |
| Locked .NET restore | Passed |
| Complete Release build | Passed, 0 warnings / 0 errors |
| `dotnet format --verify-no-changes --no-restore` | Passed |
| xUnit unit tests | **120 passed**, 0 failed/skipped |
| Real PostgreSQL integration tests | **177 passed**, 0 failed/skipped |
| Total backend tests | **297 passed** |
| EF pending-model verification | No changes since latest migration |
| Migration against populated M5a/b schema | Passed: ledger rolled back/reapplied in isolated database; existing Identity/customer/driver/vehicle/shipment/version/event data retained |
| Existing local Development migration | Passed and idempotent; before/after counts identical: 11 customers, 13 drivers, 9 vehicles, **37 shipments, 125 events**; no reset |
| Frontend ESLint / Prettier / TypeScript | Passed |
| Vitest / RTL | **95 passed in 20 files** |
| Frontend infrastructure helpers | **2 passed** |
| Frontend production build | Passed |
| Chromium full suite | **29 passed** |
| Firefox full suite | **29 passed** |
| WebKit full suite | **29 passed** |
| Full browser total | **87 passed**, 0 failed/skipped; actual Docker-backed authenticated workflows |
| New Insights repeat stability | **18 passed**: 3 cases × 2 repeats × 3 browsers, fresh isolated stack, no test retry |
| Final Insights checks after visual copy cleanup | **9 passed**, all three browsers; includes generation/access/axe |
| Accessibility | Existing WCAG A/AA axe coverage retained; new Insights audit passed in all engines and both repeats |
| API contract drift | Passed against the actual running browser-test API/OpenAPI |
| Docker | Normal API/test-host builds and Compose migration/seed/browser integration passed; optional `Dockerfile.web` production asset/API build passed |
| Same-origin web-image smoke | Root/login/insights/shipment deep links and JS assets 200; protected/unknown API paths return JSON 401 rather than HTML; non-root UID 1654; test container removed |
| npm security audit | 0 reported vulnerabilities |
| NuGet security audit | 0 known package/advisory findings |
| Python helper tests | **4 passed** |
| Workflow validation | `actionlint` 1.7.12 passed; no deploy step added |
| Public-file/credential guard and diff whitespace | Passed, 0 findings |
| GitHub-hosted CI for this revision | **Not executed/verified**; earlier green jobs do not certify M6 |
| Live Cloudflare request/account/billing configuration | **Not exercised or enabled** |

Full browser coverage includes the complete create/edit/assign/plan/transit/deliver lifecycle, immutable history, overlaps/stale versions, resource management, role/CSRF/login/logout/session recovery, multi-user SignalR/reconnect/retained edits and new insights. Unit/integration tests cover provider errors/invalid output/timeouts, injection canaries omitted from input, user/global races and persisted limits, UTC reset/cache/cooldown/restart, deactivation, approved Viewer AI without logistics write rights and no business-data mutations.

Generated local evidence is ignored under `artifacts/backend`, `frontend/coverage`, `frontend/test-results` and `artifacts/m6`. Browser diagnostics are sanitized before retention/upload. `artifacts/m6/e2e-all-browsers.xml` and `e2e-insights-repeated.xml` retain separate full/repeated results so one run does not overwrite the other.

Coverage is diagnostic, not a target: the integration run exercises roughly 91% overall line coverage across application assemblies (Domain ~96%, API ~96%, Infrastructure ~85%, Application ~94%); frontend component coverage is ~78% lines. The separate unit run references infrastructure/application but is not a substitute for integration coverage. Do not average these independently collected reports or claim exhaustive security testing. Reproduce detailed values with `python3 scripts/coverage-summary.py`.

## Issues found and corrected

1. EF could not translate filtering over positional-record constructor projections. Member-initializer projections preserve SQL translation; real PostgreSQL aggregate/paging tests now pass.
2. The first Insights axe audit found 4.44:1 contrast on secondary text against the workspace background. New Insights text and shared pagination now use the darker slate color. No accessibility rule/browser was disabled.
3. Explicit anonymous browser contexts initially inherited Playwright's configured authenticated storage state. Tests now supply an empty cookie/origin state; the actual app correctly returns 401. Per-browser approved accounts isolate persistent daily AI allowances.
4. Docker Desktop once exposed a transient host-port response before PostgreSQL was usable. The integration fixture now proves a real Npgsql connection and `SELECT 1` through the host port with a bounded readiness poll, rather than retrying failed tests.
5. Visual inspection caught duplicated disabled-state explanation text; the UI now displays the server's explanation once.

## Cost, provider and security limitations

The official Cloudflare model/free-plan/pricing, model publisher license and developer service terms were reviewed. GLM-4.7-Flash is currently available on Workers Free, with 10,000 shared account Neurons/day. No comparable EEA paid-only restriction was found in the reviewed Cloudflare terms. [The AI guide](insights.md) links the official sources and records the manual verification procedure. This is not legal clearance, an EU-only data residency claim or a guarantee that free plans remain unchanged.

External inference remains **disabled by default**. Owner configuration must verify an actual no-card Workers Free account, no paid plans/credits/automatic upgrades, exact model, fictional dataset, intended Austria/EEA use and a review expiry within seven days. No application can prove the account's real billing configuration from these flags. The maximum 3 user / 20 global attempts, 500 output tokens and bounded requests are safeguards, not a guarantee of provider quota or zero invoice under a manually upgraded account. Provider/model changes require owner review; they never trigger a paid fallback.

No real provider output or latency/quality has been measured. Model JSON compatibility follows official documentation and deterministic transport tests, but owner-approved live free-quota verification remains a separate manual step. Provider unavailability, capacity exhaustion, schema changes or expiry leave deterministic analytics available.

Only fictional/minimized logistics facts may leave the backend. Cloudflare processing/metadata/cross-border implications remain subject to current terms; no real personal data is authorized. Existing deployment prerequisites remain: trusted HTTPS proxy/SignalR origin handling, precise hosts, production admin/bootstrap/migrations, key encryption/rotation, least-privilege database access, abuse controls, backups and recovery. No public deployment is authorized by this milestone.

## Local use and future hosting

The existing stack is updated locally. Open `http://127.0.0.1:5173`, sign in with an existing demo account and choose **AI Insights**. Use [setup](setup.md#m6-operational-insights) for an existing clone and [optional Cloudflare setup](insights.md#cloudflare-setup--manual-optional-no-paid-activation) only after manually verifying the account. No external credentials are necessary for deterministic findings, builds or tests.

[ZERO_COST_DEPLOYMENT.md](../ZERO_COST_DEPLOYMENT.md) evaluates a future Render Free same-origin Docker service, Neon Free persistent PostgreSQL root branch and Workers Free inference. It explicitly rejects expiring Render Free PostgreSQL and identifies manual/no-card checks, sleeps/quotas, persistence, security and provider-change risks. No keep-alive automation, hosted resource or billing activation was created.

For hosted verification, review the diff and private-file guard, then push through your normal approved workflow. Confirm **Backend, Frontend and all three E2E jobs** for that exact revision, inspect sanitized artifacts if any fail, and provide no Cloudflare key to CI. No automatic commit/push was performed here.

## Follow-up verification (2026-11-09)

The first hosted M6 run passed the frontend/three browser jobs but exposed a backend timestamp precision defect. See [the CI stabilization and single live Cloudflare verification report](m6-ci-stabilization.md) for the exact fix, deterministic macOS/Linux regression coverage and subsequent owner-approved inference. The original no-live-call status above describes the initial milestone run.
