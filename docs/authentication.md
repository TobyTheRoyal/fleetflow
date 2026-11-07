# Authentication and authorization — M5a

FleetFlow uses ASP.NET Core Identity 10 with EF Core/PostgreSQL. Identity types, stores, administrative transactions and development provisioning live in Infrastructure; API controllers handle HTTP, cookies, CSRF and policies. Domain/Application remain independent of ASP.NET Core. There is no external identity provider, JWT, public registration or custom password cryptography. Identity users are workspace operators; logistics Driver records remain separate resources.

The original M5 milestone combined authentication and SignalR. M5a deliberately implements only authentication/authorization, as requested. Existing logistics DTOs and business rules remain unchanged; API access now requires authentication and shipment events record the verified Identity user ID. Historic events are not rewritten.

## Role matrix

| Operation | Admin | Dispatcher | Viewer |
| --- | --- | --- | --- |
| Dashboard, shipments/history, customers, drivers, vehicles: read | Yes | Yes | Yes |
| Create/edit logistics, assign resources, change shipment status | Yes | Yes | No |
| List/create users; activate/deactivate accounts | Yes | No | No |
| Current session, password change, logout | Own account | Own account | Own account |

`LogisticsRead`, `LogisticsWrite` and `ManageUsers` are server authorization policies. A fallback policy requires authentication for otherwise unannotated endpoints. Health checks, Development-only OpenAPI, CSRF bootstrap and login are explicitly anonymous. No public registration route is mapped. The client hides disallowed actions and guards routes; those are usability controls, not the security boundary.

Users have one role assigned at creation. Role editing, deleting users, invitation email and password-recovery delivery are not implemented. Administrators cannot deactivate themselves; serialized checks preserve at least one active administrator. User creation and role assignment are transactional. Activation changes rotate the security stamp; deactivated users cannot sign in.

## HTTP contract

| Method/path | Access | Result |
| --- | --- | --- |
| `GET /api/v1/auth/csrf` | Anonymous or authenticated | `{ "token": "<request-token>" }` plus HttpOnly antiforgery cookie |
| `POST /api/v1/auth/login` | Anonymous, CSRF, rate limit | Session user and application cookie; generic 401 on invalid/locked/inactive account |
| `GET /api/v1/auth/me` | Authenticated | `{ id, email, displayName, role, active }` |
| `POST /api/v1/auth/logout` | Authenticated, CSRF | `{ "success": true }`; revokes all sessions for that account |
| `POST /api/v1/auth/password` | Authenticated, CSRF, rate limit | Current/new password; rotates stamp, refreshes current session and revokes other sessions |
| `GET /api/v1/users?page=1&pageSize=20` | Admin | Paged user DTOs, maximum 100 per page |
| `POST /api/v1/users` | Admin, CSRF | `{ email, displayName, role, password }`; 201, request password is never returned |
| `PUT /api/v1/users/{id}/activation` | Admin, CSRF | Required `{ "active": true/false }`; updated user DTO |

Login body is `{ "email": "<account-email>", "password": "<your-password>" }`. Password change body is `{ "currentPassword": "<current-password>", "newPassword": "<new-password>" }`. Placeholders are not working credentials. OpenAPI describes actual DTOs, success responses and required CSRF headers; generated frontend types are checked against it.

Every POST/PUT requires a synchronizer request token in `X-CSRF-TOKEN`, paired with its antiforgery cookie. Fetch a new token after login/logout. A token minted for an anonymous identity is not valid for an authenticated mutation. The frontend keeps request tokens only in memory and never retries a failed write automatically. API clients must retain cookies and obtain the current identity's token too.

Errors use safe ProblemDetails and machine-readable codes: 401 `authentication_required`/`invalid_credentials`, 403 `forbidden`, 400 `csrf_invalid`/`invalid_request`, 429 `login_rate_limited`, and existing 409/422 business errors. Login errors do not disclose whether an account exists, is disabled or is locked. Unknown/disabled accounts perform a dummy or existing Identity password-hash verification; this is not a formal constant-time or anti-enumeration guarantee.

## Cookies, expiry and revocation

The application cookie is HttpOnly, host-only, path `/`, SameSite Strict and non-persistent. Its protected authentication ticket has an absolute **30-minute** lifetime, using `TimeProvider`, with no sliding refresh. An explicit login/password change can create a fresh ticket. Identity's own password hasher, role claims and security stamp are used.

Every authenticated request checks the current user/stamp and active flag in PostgreSQL. Logout changes the stamp, revoking other/copied cookies as well as clearing the browser cookie. Password change preserves only the freshly refreshed current session. Deactivation/reactivation revokes old sessions; it never resurrects an old cookie. An operation already in progress is not retroactively rolled back by deactivation. The per-request identity lookup is a deliberate revocation/performance trade-off; logistics query-count tests account for it separately.

Data Protection keys persist in `DataProtectionKeys` with a fixed application discriminator, allowing cookie verification across API restarts. The database key XML is **not encrypted at rest in this local setup**. A deployment must protect the key ring with a separately managed certificate/key and least-privilege access; losing/compromising the database/key ring has security consequences. Do not export keys into logs or artifacts.

Production-environment cookies require Secure/HTTPS. Documented Development uses loopback HTTP, so its cookies can travel over that local HTTP connection; do not reuse that exception for a public deployment. Vite proxies `/api` to the API, keeping browser requests same-origin. Keep `VITE_API_BASE_URL` empty. Cross-origin credentialed hosting, wildcard CORS and untrusted forwarded headers are not enabled.

## Password and abuse controls

- Identity password hashing/salts/versioned hash verification; no handwritten hashing.
- 12–128 characters through the API, at least six distinct characters, uppercase, lowercase, digit and symbol.
- Five failed password checks lock new login/password-check attempts for 15 minutes. Existing active sessions are not implicitly treated as deactivated accounts.
- Login/password endpoints share a fixed-window limit of 30 requests per minute per remote IP, no queue, 429 with Retry-After. The local reverse proxy shares an IP; trusted proxy/distributed abuse-control design remains a deployment task.
- Admin-provisioned initial passwords must be shared securely. Users can change their own password from the header's key action. There is no emailed reset/recovery flow or MFA yet.

## Local demo accounts

For first startup, `sh scripts/dev.sh` initializes the database, fictional logistics seed and accounts. For an existing local stack:

```sh
docker compose -p fleetflow build api
sh scripts/demo-users.sh
docker compose -p fleetflow up -d --no-deps api
```

`demo-users.sh` generates independent random passwords into ignored **`.env.demo`**, with restrictive permissions, and invokes the Development-only `--migrate --seed-users` command. It prints account emails/file location, not passwords. Read the file locally in your editor; never commit, paste into reports or upload it.

Accounts: `admin@fleetflow.example`, `dispatcher@fleetflow.example`, `viewer@fleetflow.example`. There are no fixed demo/production passwords. Existing accounts, activation state and password changes are preserved by repeated provisioning. `.env.demo` records initial passwords; it does not track later password changes. A deliberate full demo reset is destructive and requires `sh scripts/reset-demo.sh --confirm`; it also reprovisions accounts.

## Frontend session behavior

A dedicated auth feature restores `/auth/me`, implements login/logout/password changes and supplies current-user/role context. TanStack Query owns restoration and server data; query caches and mounted form state are discarded when switching accounts or signing out. A 401 locks the workspace behind a sign-in dialog. The same account can resume an unsaved form in that tab; a different account gets fresh state. A 403 remains an authorization error. Unsaved drafts are not persisted in browser storage, and no access token is stored in localStorage/sessionStorage.

## Verification and deployment boundary

Real Identity cookies and CSRF are used by HTTP and E2E tests; no test authentication scheme bypass is installed. Tests cover expiry with a controllable clock, role denial, disabled sessions, lockout/rate limits, password changes, migration/data preservation and the shipment lifecycle. See [M5a verification](m5a-verification.md).

E2E credentials/states are generated in a mode-restricted temporary directory outside the repository and removed at teardown. Fixtures check cached sessions and use the real login again if expired, rather than extending the server lifetime. CI sanitizes synthetic traces, embedded HTML report archives and run logs before upload; upload is gated on successful sanitization. Review this policy again before introducing real customer data, arbitrary password formats, authenticated screenshots with sensitive fields or third-party diagnostics.

Authentication does not constitute deployment approval. TLS/proxy policies, protected key-ring encryption, least-privilege DB roles, MFA/recovery, production administrator bootstrap, distributed rate limiting, backup/restore, observability/retention and deployment testing remain required. No public deployment or SignalR/AI/mobile implementation is included.

## ASP.NET Core concepts

`UserManager` owns user/password/stamp operations, `SignInManager` issues framework cookie principals, and roles become protected claims checked by authorization policies. A security stamp invalidates previously issued tickets without keeping a custom session-token table. Antiforgery is separate from authentication: an authenticated cookie alone does not prove that a browser user intended a mutation. Data Protection authenticates/encrypts the cookie payload; password hashing protects stored passwords and serves a different purpose. EF migrations evolve schema independently of login/startup, and the DI-scoped Identity store shares the existing transaction-capable DbContext.
