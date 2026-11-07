# M5a verification — authentication and role-based authorization

Verified locally on 2026-11-07. The project owner reported the five-job M4.1 GitHub-hosted baseline green. **No hosted CI run of these M5a changes has been performed.** No commit, push, deployment or remote repository setting was changed.

Scope: ASP.NET Core Identity, PostgreSQL persistence, cookie sessions, CSRF, roles, managed accounts, password change, frontend session UX and their tests. SignalR, AI, mobile, OAuth providers and public deployment are not included. The original combined M5 milestone is intentionally split according to the explicit M5a request.

## Architecture and access

See [authentication.md](authentication.md) for architecture, endpoints, role matrix, cookie settings, provisioning and framework concepts. Infrastructure owns Identity storage and transactional user administration; HTTP controllers remain orchestration. Domain/Application and logistics business rules are preserved. User DTOs never contain hashes, stamps or tokens. New shipment events use the verified user ID; historical records are untouched.

Admin can read/write logistics and manage users; Dispatcher can read/write logistics; Viewer can only read logistics. All roles can inspect their session, change their password and log out. Anonymous access is limited to health, Development-only OpenAPI, login and CSRF bootstrap. Unsafe requests require a real synchronizer token, including login/logout. API authorization is enforced independently of UI controls.

## Additive database migration

`20261107140050_AddIdentitySessions` adds Identity users/roles/claims/logins/user-role links/user tokens and `DataProtectionKeys`, with Identity keys/indexes/foreign keys and unique normalized email. It seeds the three named roles, not credentials. The existing shipment/customer/driver/vehicle/event schema and sequence are not recreated or reset.

A PostgreSQL integration test migrates only the original M1 schema, seeds logistics, captures shipment IDs/versions/references and history count, applies M5a, repeats the idempotent seed and verifies preservation. The existing constraint, rollback/reapply and migration-model checks remain enabled.

The actual development database was migrated without a reset. Counts before and after were identical:

| Customers | Drivers | Vehicles | Shipments | Shipment events |
| --- | --- | --- | --- | --- |
| 11 | 13 | 9 | 37 | 125 |

Development provisioning created three random-password demo accounts. Repeating it does not reset credentials, activation state or prior password changes. Data Protection keys persist in PostgreSQL; a real-cookie integration test disposes/restarts the API host and verifies that the existing cookie still authenticates.

## Executed checks

Environment: macOS 26.2 / Apple Silicon, .NET SDK 10.0.403, Node 24.22.2, Playwright 1.64.1 and its matching bundles, Docker Desktop, PostgreSQL 18.6. All database integration uses actual PostgreSQL; all E2E application data uses the actual Docker API/database.

| Check | Actual result |
| --- | --- |
| Locked .NET restore | Passed |
| Complete Release build | Passed; zero build warnings/errors |
| .NET format verification | Passed |
| Backend unit tests | **73 passed** |
| PostgreSQL/HTTP integration tests | **138 passed** |
| Complete backend suite | **211 passed**, 0 failed/skipped |
| EF migration/model consistency | Passed; no pending model changes |
| Frontend ESLint, Prettier, TypeScript, production build | Passed |
| Vitest/RTL | **64 passed** in 15 files |
| Node test-infrastructure safety tests | **2 passed** |
| Python reset/redaction safety tests | **3 passed** |
| Generated OpenAPI/client agreement | Passed against the running API |
| Docker API builds, migration/seed and readiness | Passed in isolated E2E stacks and the preserved development stack |
| NuGet direct/transitive audit | Zero known findings at the time of verification |
| npm audit | Zero known findings at the time of verification |
| Workflow syntax/expressions | actionlint 1.7.12 passed |
| Public-file privacy/whitespace checks | Passed |
| GitHub-hosted execution of M5a | **Not run** |

Dependency audits are time-specific, not penetration testing or a container OS-package scan. Full coverage reports continue to include new auth code rather than excluding it to preserve old percentages. Backend reports are under `artifacts/backend/`, frontend V8 reports under `frontend/coverage/`; `python3 scripts/coverage-summary.py` prints per-suite/per-assembly scope. Current integration line/branch coverage is 97.87%/87.79%; Domain unit coverage is 92.69%/85.07%. Frontend component coverage is 73.86% lines and 73.81% branches, including unvisited auth components. The raw unit total includes referenced, unexercised Infrastructure code and is not represented as Domain coverage.

## Browser results

| Browser | Complete authenticated suite | Repeated Admin/focus cases |
| --- | --- | --- |
| Chromium | **23/23 passed** | **4/4 passed** |
| Firefox | **23/23 passed** | **4/4 passed** |
| WebKit | **23/23 passed** | **4/4 passed** |

Complete run: **69/69**, no skips or retries. The additional repeated run executes Admin provisioning/activation and assignment-dialog focus twice per browser: **12/12 passed**. No authorization bypass, disabled browser project, disabled WCAG rule, force click or fixed sleep was introduced.

New browser scenarios cover successful/failed login, session restoration, HttpOnly/SameSite flags, missing CSRF denial, password change, logout, Viewer UI/API denial, Admin creation/deactivation/reactivation and lost-session recovery with an unsaved shipment edit. Existing complete shipment lifecycle, history, stale-version, reservation conflicts, pagination, management, accessibility and responsive tests now authenticate explicitly. Timed 30-minute expiry is verified through HTTP with TimeProvider; the browser test simulates losing that cookie and verifies the UI recovery.

Axe uses its full analysis mode. It opens an analysis page; the test explicitly brings the application page to the front before subsequent keyboard/focus assertions. The header was adapted to avoid mobile overflow from account controls; actual authenticated desktop/mobile screenshots were regenerated from a fresh fictional seed and inspected.

### Earlier interrupted run

An earlier complete invocation recorded 49 successes, 14 failures and six errors over about 80 minutes, including individual pauses of many minutes. Subsequent requests showed expired cached setup cookies. That interrupted run is not claimed as successful evidence. The exact host scheduling/suspension cause was not fully diagnosed. Fixtures now verify cached sessions and perform a **real** login if expired; the server's 30-minute lifetime is unchanged. The successful complete and repeat runs used command-scoped macOS `caffeinate -i` to prevent idle sleep, without changing persistent system settings. CI Linux runners do not use this macOS utility.

## Security verification

The HTTP suite covers role denial on every mutation category, anonymous reads, real login/current-user/logout, CSRF denial, password policy and transactional provisioning failure, five-attempt lockout, 30-per-minute IP limit with Retry-After, inactive accounts, immediate stamp revocation, password changes preserving only the current session, HTTPS cookie flags, absolute expiry and restart continuity. Existing shipment validation, concurrency, resource locking and atomic history remain green.

CI sanitizes synthetic browser traces, embedded HTML report ZIP data and logs before upload; uploads are gated on successful sanitization. The redaction test exercises cookies/passwords in archives and embedded reports. Generated auth state/credentials are in a restricted temporary directory outside source and artifact globs, removed on teardown. Backend assertions avoid printing real random credentials/cookies on failure. This is not universal protection for future arbitrary credentials, real-customer screenshots or new artifact formats; those require another review.

## Local use and remaining limits

The local API was rebuilt/migrated and demo accounts provisioned without resetting logistics. Open `http://127.0.0.1:5173/`. Read initial passwords locally in ignored `.env.demo`; emails are `admin@fleetflow.example`, `dispatcher@fleetflow.example`, `viewer@fleetflow.example`. See [setup](setup.md) and [account instructions](authentication.md#local-demo-accounts). No actual password is included in this report or committed files.

This remains a local portfolio application. Development HTTP cookies, unencrypted-at-rest database Data Protection keys, privileged development DB access, in-memory/IP-local rate limiting, absent MFA/recovery/invitation delivery and incomplete deployment/operations controls prevent a production-readiness claim. Roles are assigned at creation; changing roles/deleting users is outside this implementation. The account model is separate from logistics drivers. Automated accessibility tests are not a screen-reader/real-device certification.

After user review/commit/push, run **FleetFlow quality** for the exact revision and require Backend, Frontend and all three E2E jobs. Inspect sanitized diagnostics and record the run URL/commit before calling M5a hosted CI verified. No secrets are required by the test workflow, and no deployment step is configured. Stop after M5a.
