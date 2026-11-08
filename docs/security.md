# Security review — local portfolio application

Updated for M5b on 2026-11-08. [Identity/session design](authentication.md) documents the current controls. This is an engineering review with automated checks, not a penetration test or production security certification. FleetFlow must stay local until authentication/authorization and deployment controls are implemented and verified.

## Trust boundary

Assets include customer contacts, addresses, cargo/schedules, driver/vehicle identifiers and event history. Browser input is untrusted; DTO binding/domain validation/database constraints are independent defenses. The React client is not a security boundary. Current demo data is entirely fictional.

Logistics reads require Admin/Dispatcher/Viewer; writes require Admin/Dispatcher. User administration requires Admin. Identity security stamps and active flags are checked on every authenticated request. Newly written shipment events use the verified user ID; historical demo events are preserved. Production cookies require HTTPS, and Development-only OpenAPI/provisioning remain separate from authorization.

## Reviewed controls and findings

| Area | Current control / finding |
| --- | --- |
| SQL and queries | EF parameterization, literal escaped ILIKE search, bounded server-side paging, SQL projections/aggregates; no raw user-interpolated SQL |
| Request validation | Dedicated DTOs, required/length/range checks, named enums, strict JSON numbers, UTC offsets, unknown-field rejection; domain invariants repeated server-side |
| Transaction integrity | Optimistic Version checks, resource locks, atomic state/history and PostgreSQL constraints; failed operations and races covered by real database tests, including EF/Npgsql-wrapped concurrency failures |
| Error responses | Safe ProblemDetails/code/traceId; serializer errors and unexpected stacks/SQL are not reflected |
| Unexpected logs | M4 removes unfiltered exception objects from the application handler's error log; logs contain exception type and trace ID. A canary-data test checks the handler's log and response |
| Sensitive EF output | Sensitive-data logging is not enabled; PostgreSQL detailed error contents are not explicitly enabled. Logging must be reassessed before real data is introduced |
| Browser-origin exposure | No permissive CORS policy; disallowed preflight grants and simple non-JSON command bodies are tested. CORS is not authentication |
| Hosts and bind addresses | AllowedHosts is explicit; unknown Host headers are tested. Compose and documented Vite commands publish only on 127.0.0.1. A hostile network client can spoof a Host header if you expose the port, so host filtering does not authorize access |
| Cache/content handling | API responses, including handled errors, use `Cache-Control: no-store` and `X-Content-Type-Options: nosniff` |
| Development tools | OpenAPI and migration/seed tooling are Development-only; normal startup does not seed or auto-migrate |
| Credentials | Random ignored local password, restricted environment-file permissions, no browser secrets, no real credentials in source, Docker contexts or artifact globs |
| Container | API image runs non-root; M4 adds read-only root, bounded scratch tmpfs, dropped capabilities and no-new-privileges. Actual business writes are tested with this configuration |
| Database role | The image's development user is privileged and owns migration/seed capability. This is explicitly unsuitable as a production application role |
| Dependencies | Pinned versions/lockfiles, npm and direct/transitive NuGet audit, reviewed update configuration. Findings are time-specific; see M4 verification |
| CI | Read-only token permissions, disabled credential persistence, no deployment secrets or public deployment, scoped/redacted artifacts |

The app-handler logging test does not promise that every framework, driver, future component or operator log is automatically redacted. No request-body logging middleware is enabled. Do not turn on EF sensitive logging or dump configuration/environment output in CI. Synthetic failure traces are useful now; authenticated cookies/tokens or real data would require a new artifact policy.

## Local operational requirements

Use the documented same-origin proxy. Do not bind Vite or published API/database ports to 0.0.0.0, forward ports to the Internet, or use a public tunnel. Root `.env` and ignored frontend configuration are local files; `VITE_*` values are public build inputs. PostgreSQL data persists in a named volume. The reset helper requires explicit `--confirm` and names only the development project.

The privacy helper checks public Git candidates for private instruction/configuration filenames and current local credential values. It is not a general secret scanner or a review of remote Git history. No Git history was rewritten or remote repository setting changed.

## Required before public deployment

1. Deployment-specific verification of the implemented authentication, authorization, CSRF, verified actors and permission tests, plus MFA and account recovery where appropriate.
2. Defined identity/session model, secure cookies/token handling, expiration/revocation and applicable CSRF protections.
3. HTTPS, trusted reverse-proxy/forwarded-header policy, explicit allowed origins/hosts and a fresh public-exposure review.
4. Separate least-privilege application and migration database roles; network restrictions, managed secrets and credential rotation.
5. Appropriate request/rate limits, abuse controls and resource budgets based on measured usage.
6. Safe structured observability, access/audit retention, privacy policy and scrubbing of traces/diagnostic artifacts.
7. Backup/restore validation, migration/recovery procedures, operational health checks and incident handling.
8. Container/base-image vulnerability scans, reviewed runtime patching and deployment-specific hardening.
9. Security and accessibility reviews with real deployment assumptions, plus protected CI checks and secret scanning.

M5a adds Identity authentication, roles, CSRF, lockout/rate limits, password change and immediate account/session revocation. M5b adds secured SignalR notifications; AI, public deployment and mobile applications remain absent. Shipment-only Version semantics and absence of create-request idempotency keys remain documented design limits, not hidden security guarantees.

## M5a diagnostic artifacts

Authenticated browser traces can contain passwords and cookies. CI now sanitizes synthetic trace archives, embedded HTML report archives and logs before upload, and skips upload if sanitization fails. Generated fixture credentials and cookie states remain outside artifact paths and are deleted at teardown. This is scoped synthetic-test redaction, not a universal DLP guarantee. Data Protection keys reside unencrypted in the development database; key encryption and least-privilege access are deployment blockers.

## M5b transport review

The Hub requires the logistics read role, uses existing cookie authentication and rejects foreign/null origins. Unsafe transport requests require synchronizer CSRF tokens. WebSocket upgrades require an explicit approved Origin; no wildcard credentialed CORS or URL access tokens are introduced. Clients cannot mutate logistics through Hub methods. Revalidation before delivery and five-second idle sweeps close revoked connections; ticket expiry also closes transports. Payloads contain identifiers/type/version/time only. Detailed server errors and client transport logs are disabled. See [real-time security and limitations](realtime.md#authentication-and-browser-security).

Local single-instance memory, a revocation/send race for in-flight hints, best-effort delivery, absence of connection-level production abuse budgets and deployment-specific transport/proxy testing remain limits. The authoritative REST API and expected-Version checks remain mandatory. No public exposure is approved.

SignalR negotiation connection tokens and Hub URL `id` values are now included in authenticated artifact redaction, with a regression test. They must not be shared as ordinary diagnostic identifiers.
