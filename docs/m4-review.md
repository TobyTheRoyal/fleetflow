# M4 review findings and decisions

Reviewed the API controllers/DTOs, domain/application services, persistence/queries/migrations, existing tests, React feature/layout/form/query code, runtime configuration and M0–M3 verification reports. Scope is M4; wire DTOs, endpoints and business rules remain intact. No large architectural rewrite was justified.

## Findings fixed

| Priority | Finding and impact | Resolution / evidence |
| --- | --- | --- |
| High | `SaveChanges` deadlock/serialization errors were wrapped by EF/Npgsql and escaped the existing raw-provider conflict handler as HTTP 500 | A real PostgreSQL trigger reproduces both SQL states through HTTP. The transaction handler recognizes provider exceptions through wrapper chains and returns existing `409/concurrent_operation`. Tests assert unchanged state/version/history and exactly one event after explicit retry |
| Medium | Application unexpected-error logs attached raw exception objects, which can contain sensitive content | Log only exception type and trace ID in the app handler; canary-value test asserts response/log exclusion. Framework/driver logging remains a separate pre-production review |
| Medium | Empty out-of-range shipment/master pages claimed no records existed | Explain that the page is empty and offer return to page one; component and actual-browser recovery tests |
| Medium | A network failure after a write encouraged a blind retry even though the commit outcome can be unknown | Explicit unknown-write-outcome guidance for transport/incompatible success responses; no automatic write retry; transport tests and documentation of missing create idempotency keys |
| Medium | Several text colors narrowly missed WCAG AA contrast on the actual canvas/table backgrounds | Darken only the affected muted/link/schedule/status text while preserving design direction; automated contrast checks remain enabled |
| Medium | The brand's accessible name did not include all visible label content | Natural expanded label plus a meaningful collapsed icon name, without concealing the visible brand text |
| Medium | Native WebKit does not consistently focus a clicked trigger; remembering only `document.activeElement` lost dialog return focus | Explicit trigger refs for navigation, master-data and shipment dialogs, with a main-content fallback when the original action disappears |
| Medium | Test-created master records used browser-shared unique values and could fall outside the visible sorted page | Resource identities include test/browser/repeat index; UI-created values sort into the visible page. Each mutation journey owns its resources |
| Medium | No hosted quality configuration or coverage output existed | Least-privilege CI jobs, locked restores, real PostgreSQL fixtures, API drift checks, scoped artifacts, dependency audits and Cobertura/V8 reports |
| Low | Route navigation did not update the document title or move desktop sidebar keyboard focus into the new page | Contextual titles and desktop navigation focus; tests verify behavior without stealing focus on query-only changes |
| Low | Shipment list lacked pickup context alongside its delivery deadline | Add the actual pickup timestamp to the full shipment table's schedule cell; no fabricated field or new API |
| Low | Local setup/reset steps were spread across reports and a reset could target an inherited Compose project accidentally | Portable foreground dev helper and explicit-confirmation reset with fixed `fleetflow` project; fake-Docker tests prove refusal and exact target |
| Low | Screenshot reproduction could accidentally capture a developer database | Isolated migration/seed capture helper and actual inspected PNGs, with documented viewport/timezone and cleanup |

## Existing design retained

- Scoped EF DbContext/application stores and singleton TimeProvider are appropriate and now explicitly checked.
- Shipment operations keep version checks, resource locks, transactions and event persistence together; API controllers remain HTTP-only orchestration.
- Read queries use asynchronous projections/AsNoTracking; SQL filtering/paging and dashboard aggregation remain bounded. Existing query-count tests guard against N+1 behavior; no unmeasured throughput/latency claim or speculative index was added.
- Domain factories/guards, dedicated DTOs, strict JSON and database constraints provide layered validation. EF entities remain private to persistence.
- Query cancellation, runtime response validation, explicit edit snapshots and query invalidation remain the frontend integration model. No global store, generic repository framework, mediator/event bus or new infrastructure tier was added.
- The existing English design and fixed, explicit Vienna display timezone are retained.

## Remaining trade-offs

See `security.md` for required production controls. Authentication, verified actor identity, least-privilege runtime DB roles and deployment remain future work. Offset paging may shift under concurrent writes; master records have no Version contract; create mutations have no idempotency key. Some aggregate backend validation codes cannot identify one precise field without a future contract refinement.

Automated accessibility is not screen-reader/real-device certification. Native macOS browser-driver issues must be distinguished from application regressions; the hosted Linux CI matrix still needs verification. An exploratory ARM64 Linux run passed 50/51 tests, with one Firefox accessibility page crash. Hosted Actions cache/artifact/token behavior still needs an actual GitHub run. Coverage metrics are reported by suite/assembly rather than disguised as a single blended percentage.

Larger React forms can be split further if their responsibilities grow, and repeated style values could be consolidated into semantic design tokens. Neither justified a risky broad refactor during this quality pass. Future changes should preserve the existing behavioral tests.
