# M5b verification — authenticated real-time logistics

Verified locally on **2026-11-08**. Scope: SignalR notifications and browser collaboration on top of M0–M5a. No M6/M7, public deployment, commit, push or remote repository modification was performed. Private project instructions remain outside the repository. Existing REST/domain contracts and authentication policies are preserved.

## Implementation and architecture

See [the real-time contract](realtime.md) for payloads, security, connection lifecycle and scaling limits. Application defines a typed notification/transport port; the existing Infrastructure transaction captures changes, commits PostgreSQL state/history, then invokes the API's typed SignalR publisher. No broker, new database schema, additional service or business Hub RPC is introduced.

Notifications cover shipment creation, editable details, assignment, status and cancellation; driver/vehicle creation and changes; and customer creation. Payloads contain only event/entity IDs, entity/type, shipment Version (null for existing unversioned master data) and UTC event time. Shipment hints reuse the persisted history ID. Failed validation, stale versions and actual pre-commit database rollback emit nothing. Delivery errors have an independent bounded budget and cannot undo committed data or make a successful mutation fail.

The official JS client lives in a dedicated realtime feature. It validates/deduplicates hints, invalidates the existing TanStack Query keys, reconnects with backoff and reloads current REST data after reconnection. Disconnected active queries reconcile every 30 seconds; connected clients reconcile every minute to recover silently lost best-effort hints. Editing snapshots retain both text and expected Version; conflicts still require explicit refresh/discard. Pending session checks are cancelled on disposal. A different shared-cookie account locks the old workspace, clears its CSRF cache and requires explicit sign-in before adopting the new account.

## Authentication and transport security

The Hub requires `LogisticsRead` and the existing Identity cookie. Normal HTTP cookie/stamp checks remain; unsafe transport requests also require CSRF. Browser WebSocket upgrades require an allowed Origin, with exact same-origin comparison and four explicit Development-only Vite origins. No wildcard CORS, URL bearer token, localStorage credential or anonymous Hub mode is added. Detailed server Hub errors and client transport logging are disabled.

Established connections are registered with user/stamp/expiry metadata. The server verifies current account activation, stamp, read-role membership and expiry before every broadcast; a five-second sweep also aborts idle revoked connections. Transport expiration is enabled. Logout/password change retain the existing account-wide stamp-revocation semantics. A minimal hint already in flight can race with revocation; authoritative REST requests always reauthenticate. Client session recovery retains forms for the same account.

Synthetic diagnostic redaction now includes negotiation connection tokens and Hub URL `id` values, tested in archives and logs. Existing cookie/password/CSRF and embedded-report sanitization remains. This is not a universal real-data DLP guarantee.

## Executed verification

Environment: macOS / Apple Silicon, .NET SDK 10.0.403, Node 24.22.2, Playwright 1.64.1 with matching Chromium/Firefox/WebKit bundles, Docker Desktop and actual PostgreSQL 18.6. Browser tests use the real Linux Docker API/database through the Vite proxy; browsers themselves run locally on macOS.

| Check | Actual result |
| --- | --- |
| Locked .NET restore | Passed |
| Complete Release build | Passed; **0 warnings, 0 errors** |
| .NET format verification | Passed |
| xUnit unit tests | **73 passed** |
| PostgreSQL / HTTP / SignalR integration tests | **156 passed** |
| Complete backend suite | **229 passed**, 0 failed/skipped |
| EF pending-model verification | Passed; no model changes since the two existing migrations |
| Frontend ESLint / Prettier / TypeScript | Passed |
| Vitest / RTL | **81 passed**, 19 files |
| Node infrastructure tests | **2 passed** |
| Python reset / artifact privacy tests | **4 passed** |
| Production frontend build | Passed |
| OpenAPI / generated-client agreement | Passed against the running Docker API |
| Docker API build / Compose integration | Passed in isolated E2E stacks and the preserved development stack |
| npm / direct-and-transitive NuGet audits | **0 known findings** at verification time |
| Workflow validation | actionlint **1.7.12 passed** |
| Public-file / whitespace checks | Passed |
| GitHub-hosted execution of M5b | **Not run** |

Backend coverage reports remain under `artifacts/backend/`, frontend under `frontend/coverage/`. `python3 scripts/coverage-summary.py` reports integration coverage at 97.14% lines / 87.85% branches and frontend at 77.56% lines / 74.52% branches. Domain-focused unit coverage remains 92.69% / 85.07%; the combined unit assembly total includes unexercised referenced infrastructure and is not represented as Domain coverage. Coverage is descriptive, not a completeness or security guarantee.

## Verification commands

From the repository root (the local run used an isolated `DOTNET_CLI_HOME` and disabled telemetry):

```sh
dotnet restore backend/FleetFlow.slnx --locked-mode
dotnet build backend/FleetFlow.slnx --no-restore -c Release -p:UseSharedCompilation=false
dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore
sh scripts/test-backend.sh
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__FleetFlow='Host=127.0.0.1;Database=fleetflow_model_check' dotnet ef migrations has-pending-model-changes --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api --configuration Release --no-build
python3 scripts/audit-dotnet.py
python3 -m unittest discover -s scripts/tests
python3 scripts/check-public-files.py
python3 scripts/coverage-summary.py
git diff --check
```

From `frontend/`, with Node 24.22.2 selected:

```sh
npm run check
npm audit --audit-level=moderate
CI=true CHECK_API_CONTRACT=1 npm run test:e2e
# Separate fresh-stack invocations, preserving the real login limit:
CI=true CHECK_API_CONTRACT=1 npx playwright test realtime.spec.ts --project=chromium --repeat-each=2
CI=true CHECK_API_CONTRACT=1 npx playwright test realtime.spec.ts --project=firefox --repeat-each=2
CI=true CHECK_API_CONTRACT=1 npx playwright test realtime.spec.ts --project=webkit --repeat-each=2
```

The macOS long-running test commands additionally used command-scoped `caffeinate -i` to avoid host idle sleep, without changing persistent settings. Linux CI does not need it. A local actionlint 1.7.12 binary validated the workflow. The development stack update used `docker compose -p fleetflow --env-file .env -f compose.yaml up -d --build api`; the immediate readiness probe raced startup, and a subsequent readiness check succeeded. No reset was run. Sanitized full/repeat JUnit and HTML reports are retained locally under ignored `artifacts/e2e/m5b-*` paths.

## Complete browser regression

| Browser | Full suite | Additional real-time stability runs |
| --- | --- | --- |
| Chromium | **26/26 passed** | **6/6 passed** |
| Firefox | **26/26 passed** | **6/6 passed** |
| WebKit | **26/26 passed** | **6/6 passed** |

Full result: **78/78**, no failures, skips or retries. Additional stability result: **18/18** (all three new cases repeated twice per browser), also without skips or retries. Each browser repetition used its own fresh Docker/API/database stack and authentication limit window; no rate-limit bypass was used. This includes the existing 39 accessibility/responsive cases across projects, authentication/account administration, real shipment creation/editing/assignment/lifecycle/history, overlaps, filters/pagination, management and Version conflict recovery. All accessibility rules and browser projects remain enabled. No force clicks, fixed sleeps or weakened authorization were introduced.

The three new scenarios per browser verify:

1. Two separately provisioned Dispatchers connect in independent contexts, open the same assigned shipment, and receive live status and persisted timeline changes. B's active text survives A's competing transition; B receives 409 and deliberately refreshes. B's WebSocket is interrupted and its browser goes offline while A delivers; reconnect catches up without page reload. Logout cleans up live access.
2. Deactivating an already-connected Viewer produces the session recovery dialog and denies subsequent API reads.
3. Replacing a browser profile's shared cookie with another account locks the old workspace; fresh CSRF and explicit reauthentication safely adopt the new account and establish a new connection.

The live-update assertion completes within ten seconds, below the 30-second fallback, without a navigation/reload to produce the update. Transport routing deliberately interrupts the actual proxied WebSocket; persistence and authentication remain real.

Earlier development failures are not counted as successful evidence: the .NET client helper initially lacked a cookie jar; a fixed test clock in the past conflicted with SignalR's real transport expiry clock; and a browser assertion searched a background accessibility landmark intentionally hidden by the expiry modal. The helpers/assertions were corrected, without changing production expiry, disabling browser projects or weakening the modal. Earlier complete 75-case matrices passed before the final account-change scenario was added.

## Preserved local development data and UI

The development API was rebuilt without resetting its PostgreSQL volume. Counts before/after are identical:

| Customers | Drivers | Vehicles | Shipments | History events |
| --- | --- | --- | --- | --- |
| 11 | 13 | 9 | 37 | 125 |

No migration or demo password reset is needed. Readiness returned HTTP 200; anonymous Hub negotiation returned 401. Real demo-cookie login and a Connected indicator were verified through the running UI at `http://127.0.0.1:5173/`. Private desktop/mobile screenshots were captured and visually inspected; credentials were neither printed nor published. Existing demo accounts and private `.env.demo` remain intact. Separate Dispatcher accounts can be created through the Admin Users screen for a two-user demonstration. See [setup](setup.md) and [account provisioning](authentication.md#local-demo-accounts).

## CI readiness and limitations

The existing five jobs automatically include the new backend/frontend/multi-user tests; no additional service, workflow permission or secret is required. Workflow validation and local builds/tests are successful. **No GitHub-hosted M5b run was triggered or verified.** A previous green baseline does not verify this revision, Ubuntu browser behavior, hosted caches/artifacts or runner network policy.

After review, the owner can commit/push only the intended public files and open a PR (or select the pushed revision in workflow_dispatch). Require Backend, Frontend and E2E chromium/firefox/webkit for that exact SHA; inspect 26 browser tests per project, sanitized diagnostics and generated-contract checks. Record the run URL and commit before describing M5b as hosted CI verified. No deployment step is configured.

Delivery and session tracking remain single-instance/in-memory; no durable outbox, replay, exactly-once socket guarantee or horizontal scale-out is claimed. REST reconciliation recovers current state, not every missed notification. Deployment hardening, protected key-ring storage, least-privilege DB credentials, production proxy/TLS/abuse controls and distributed revocation remain outside M5b. Native browsers plus Docker API verification is not a hosted Linux browser result. Automated accessibility checks are not screen-reader or real-device certification.

Stop after M5b. Future milestones require explicit instruction.
