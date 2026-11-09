# Portfolio demo security and isolation

M7 is local deployment preparation. It adds no hosted service or mobile feature. Normal FleetFlow remains a separate supported mode.

## Modes and public entry

With `Portfolio__Enabled=false` (default), password-based managed Identity accounts and the original `public` logistics tables behave as before. Demo GET/POST endpoints return 404. Development OpenAPI, seed and reset tools remain local-only.

With `Portfolio__Enabled=true`, `/demo` offers two explicit actions. `GET /api/v1/demo` returns actual available places and lease duration; `POST /api/v1/demo/sessions` accepts `{ "mode": "Viewer" }` or `{ "mode": "Dispatcher" }`. POST requires the ordinary antiforgery token from `/api/v1/auth/csrf`. It issues a real Identity cookie, not an authorization bypass. Password login and password changes are unavailable in this mode, existing non-demo cookies are rejected, and no Admin identity is provisioned.

| Visitor | Data access | Writes | AI |
| --- | --- | --- | --- |
| Viewer | Shared fictional read-only sample | None | None by default |
| Dispatcher | One exclusively leased fictional sandbox | Existing logistics workflows, up to 100 unsafe API attempts/lease | Off unless that fixed account ID is individually approved and all M6 checks pass |
| Administrator | No public entry | No public administrative access | No public administrative account |

Defaults are ten Viewer identities and three Dispatcher identities, each with an exclusive maximum 30-minute lease. Settings permit smaller pools/leases, not unbounded expansion. Entry is limited to six attempts/IP/minute with no queue; this can be configured from 1 to 30 for controlled environments. There is no registration, password disclosure or unlimited Identity provisioning. Capacity returns HTTP 409 `demo_capacity`; abuse/write budgets use 429. Write attempts include failed commands and AI requests, making the bound conservative. Read access survives write exhaustion.

## Fixed schema isolation

This is a bounded demonstration pool, not a general tenant platform. Five domain tables live in each fixed schema:

- `public`: original normal-mode logistics; preserved by the additive migration.
- `portfolio_template`: owner-only fictional seed.
- `portfolio_viewer`: a read dataset shared by individually leased Viewer identities.
- `portfolio_dispatch_1` through `portfolio_dispatch_3`: exclusive writable sandboxes.

Identity, lease metadata, Data Protection keys and AI usage stay in `public`. The separate Identity persistence context validates the cookie before the domain context model is selected. Only a matching active account, security stamp, lease UUID, expiration, role and server-owned slot mapping can bind `PortfolioScope`. Client headers/body/URL never select a schema. EF model caching includes the schema. Reads, SQL aggregates, search, assignments, history and insight queries all use that model. Foreign keys point within each schema; uniqueness is naturally scoped. The original domain state machine, resource locks and Version checks are unchanged.

This choice differs from the audit's possible row-level area column: a small fixed schema pool makes foreign-key isolation and privileged reset boundaries explicit while preserving the original normal-mode schema. The trade-off is a required reviewed schema upgrade whenever future domain migrations change the five cloned tables. Startup compares the catalog migration revision and refuses stale initialized schemas. Initialization never silently rebuilds them. Pool-size increases after initialization also require a reviewed maintenance upgrade; lowering active capacity is configurable.

## Leasing, revocation and reset

PostgreSQL serializes slot reservation with an advisory transaction lock. Reservation rotates the Identity security stamp and lease UUID before reuse; no two visitors receive the same active identity. Dispatcher preparation marks the slot unavailable, commits revocation, then calls a narrow maintenance function. No cookie is issued until a clean sandbox is ready. Failed preparation expires its reservation and returns a safe error.

Every authenticated demo REST request holds a shared per-slot advisory lock through response completion and rechecks its lease after acquiring the lock. Reset takes the exclusive counterpart. This prevents an old in-flight GET from reading a new visitor's data. A four-request gate avoids starving the ten-connection runtime pool. **Use a direct PostgreSQL endpoint, never transaction-mode PgBouncer.** Idle sessions hold no database connections.

`portfolio_prepare_slot` is a whitelist-only SECURITY DEFINER function with a fixed search path. It requires the exact pending slot and lease. Only the separate maintenance login can execute it. It truncates exactly that dispatcher's five logistics tables and copies the protected seed with UTC timestamps shifted relative to preparation time. It never touches another sandbox, Identity, Data Protection, normal-mode records or AI usage. PostgreSQL executes replacement atomically. The original append-only ShipmentEvents UPDATE/DELETE trigger is installed unchanged in every sandbox; runtime cannot disable it or truncate tables. There is no reset HTTP endpoint. Maintenance is triggered by an actual new visit, not a cron service.

The Viewer dataset is immutable through all Viewer APIs and seeded at initialization; its deadlines naturally age. It is not reset on every visitor. A reviewed owner maintenance release is required to refresh that common sample without disrupting active leases.

Logout revokes the exact lease; account reuse rotates the stamp. A late logout cannot invalidate the next visitor. Normal-account logout still rotates its security stamp. Cookie validation checks every request. SignalR validates the lease/stamp before delivery and during its idle sweep; notifications route only to the matching schema. Same-lease tabs receive updates, unrelated sandboxes do not. Hints remain best-effort and REST remains authoritative. A sleeping tab disconnects; reconnect validates the current lease and refetches.

The frontend clears query caches and remounts the workspace on account **or lease UUID** changes. Expiry discards demo edits, explains what happened and returns to entry. Normal-account reauthentication still preserves same-user form drafts. There is no guarantee that an already delivered hint can be recalled; payloads contain identifiers only.

## AI isolation and bounded storage

Deterministic analysis uses the scoped domain context. Cache fingerprints also include lease UUID, preventing reuse of another visitor's explanation even when the fixed Identity ID is reused. Technical AI attempts remain central and are never deleted by sandbox reset. The existing rolling 30-day AI ledger retention is unchanged; resetting a sandbox does not reset the current UTC daily budgets. A reused account may have fewer than three attempts remaining. The global limit remains twenty/day, with one global generation in flight, bounded input/output and no automatic retries or background inference.

Owner approval is account-specific, never granted by the public entry. AI is disabled in production templates and browser tests. Fictional data only is a visitor requirement as well as a provider constraint; arbitrary submitted contact/cargo prose is never sent to inference. Storage growth is bounded by the pool, per-lease writes and reuse cleanup; repeated visits can still consume database compute/network quotas. This is abuse resistance, not protection from a determined distributed denial-of-service attack.
