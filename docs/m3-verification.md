# M3 — Logistics operations frontend

Implemented and verified locally on 2026-11-06. Backend contracts, domain rules, migrations and Docker development bindings are unchanged. No authentication, SignalR, AI, publication or M4 CI work is included. Private planning documents remain outside version control.

## Pages and actual workflows

| Route | Functionality |
| --- | --- |
| `/` | PostgreSQL-backed summary, seven actual KPI counts, status distribution, overdue attention list and recent shipments |
| `/shipments` | Server-side search, status/customer/deadline filters, URL parameters, page size and pagination, resource assignments |
| `/shipments/new` | Customer, full addresses, cargo, decimal weight, pallets and schedule; creates Draft |
| `/shipments/:id` | Route/schedule/cargo, actual timestamps, resources, customer, version, chronological paged event history, dispatch/status confirmations |
| `/shipments/:id/edit` | Full Draft editing; only cargo description for Planned; terminal/in-transit editing blocked |
| `/customers` | Paged directory and creation; no unsupported edit/delete controls |
| `/drivers` | Paged directory, creation/editing and active/inactive status |
| `/vehicles` | Paged directory, creation/editing, payload capacity and availability |

The supported lifecycle is Draft → assign eligible resources → Planned → InTransit → Delivered. Draft and Planned can also be cancelled with confirmation. Status/assignment conflicts remain visible with the backend explanation. Status reasons are recorded by the API. Every successful write refetches related queries, so names/history/KPIs remain server-owned. Successful operations use non-blocking notifications; no timeline events are fabricated locally.

The dashboard total uses `totalNonCancelledShipments`: it includes Delivered and excludes Cancelled. It is labeled **Total shipments**, rather than suggesting that delivered shipments are still active. Overdue and next-24-hour links use the M2 `deadline` filter. The distribution is calculated only from actual status counts; there are no synthetic charts or trends.

## Structure and design decisions

- `src/app`: router and providers. Pages are split into lazy-loaded bundles. The persistent shell retains navigation state; a page boundary handles chunk/render errors.
- `src/features`: dashboard, shipments, customers, drivers and vehicles. Shipment validation, timeline and assignment/status dialogs are independent components.
- `src/components/ui`: shadcn-style Button and accessible Radix Dialog, including focus trapping, Escape dismissal, focus restoration and pending-write dismissal prevention.
- `src/components/shared`: labeled fields with linked errors/hints, skeleton/error/empty states, reusable directory composition, pagination and headers.
- `src/lib`: HTTP transport, cancellation, ProblemDetails, query helpers and dates. TanStack Query is the sole server-state owner; React local state stores only UI state and deliberate form snapshots.
- `src/types`: generated OpenAPI types, public aliases and runtime Zod validation.

The navy sidebar, blue actions, neutral canvas, consistent badges and readable tables form a restrained operations design. Desktop navigation collapses; mobile navigation uses an accessible dialog. Wide data tables scroll within keyboard-focusable regions. Loading, background refetches, empty results, 404 and service errors have dedicated states. Global shipment search supports Ctrl/Cmd+K.

A small reusable directory/form composition avoids three copies of pagination and mutation plumbing without hiding shipment-specific workflows in a generic framework. RHF/Zod provide field feedback; they do not replace server business validation. New dependencies are pinned in the npm lockfile.

## API integration and concurrency

All calls target existing `/api/v1` endpoints. GET requests pass TanStack Query's AbortSignal. Read responses are validated by Zod; safe `ApiError` values retain HTTP status, stable code, trace reference and field errors. HTTP 400/422 errors are shown near matching fields plus a summary. HTTP 404 identifies a missing record; HTTP 409 explains a conflict; 503/network failures provide retry/health feedback. Raw transport exceptions are not shown.

Shipment forms and dialogs pin the original Version. Background refetches cannot substitute a newer Version into unsaved changes. `stale_version` disables submission and offers **Discard edits and reload latest**. Only a successful refresh releases that block, and nothing automatically resubmits. Other business conflicts allow the operator to correct input. Mutation retries are disabled. Successful mutations invalidate shipment lists, detail/history, dashboard and relevant master-data queries. Mutation responses are not used as enriched detail records because M2 only supplies resource names on reads.

Customer/driver/vehicle option queries load bounded pages of 100 and offer explicit **Load more** controls. Ineligible resources remain visible with explanatory disabled labels. Overlap checks run on the server at submission; the UI does not assume that an Available vehicle has no scheduled reservation. Driver/vehicle updates retain the M2 server restrictions for active reservations.

### Contract regeneration

Start the local API, then run from `frontend/`:

```sh
npm run generate:api
npm run typecheck
```

`OPENAPI_URL` can override the default `http://127.0.0.1:5080/openapi/v1.json`. The pinned `openapi-typescript@7.13.0` generator runs in an isolated npm environment because its own tooling currently requires TypeScript 5; the application retains its existing TypeScript 6. No forced peer-dependency override or application downgrade is used. Initial generation needs npm network access. Generated output is excluded from manual lint/format, and is checked by TypeScript. Response schemas remain explicit and are covered by contract/HTTP tests.

### Configuration and time handling

The default empty `VITE_API_BASE_URL` uses same-origin Vite proxies for `/api`, `/health` and `/openapi`. `API_PROXY_TARGET` defaults to loopback port 5080; process environment or an ignored frontend `.env.local` can override it. See `frontend/.env.example`. Browser configuration is public: never place secrets in `VITE_*` values. Keep the default same-origin setup locally; direct requests to a different origin would require an explicit backend CORS policy, which M3 does not add. Vite preview is a static build preview and does not provide this API proxy.

Schedule inputs and displayed times explicitly use **Europe/Vienna (CET/CEST)** regardless of browser timezone. Temporal converts input to UTC and rejects DST gaps/ambiguities rather than choosing an instant silently. Unchanged edit values preserve the exact original timestamp, including seconds; Planned updates preserve all immutable fields. Weight inputs permit two decimals; the backend/PostgreSQL decimal definition remains authoritative.

## Verification

Commands run from `frontend/`:

```sh
npm run lint
npm run format:check
npm run typecheck
npm run test -- --run
npm run build
npm run test:e2e
```

| Check | Actual result |
| --- | --- |
| `npm run generate:api` | Passed against the local Development OpenAPI contract |
| `npm run lint` | Passed, zero warnings |
| `npm run format:check` | Passed |
| `npm run typecheck` | Passed, includes E2E TypeScript |
| `npm run test -- --run` | 47 passed, zero failed |
| `npm run build` | Passed; lazy-loaded pages, no chunk-size warning |
| `npm run test:e2e` | 6 passed against isolated Docker/PostgreSQL |
| `.NET build` | Passed, zero warnings/errors |
| Backend regression suite | 165 passed, zero failed/skipped |
| Browser layout inspection | 28 page/viewport combinations at widths 1440, 820, 390 and 320; no page errors or document overflow |
| `git diff --check` | Passed |

The browser QA captures were temporary local inspection files, not published assets. Tests use fixture HTTP responses only in Vitest, never in the application. Browser tests use the actual M2 API and PostgreSQL 18.6.

Playwright starts a separate `fleetflow-e2e` Compose project with a generated password in a mode-0600 temporary file outside the repository. It applies the existing EF migration and development seed. Vite/API/PostgreSQL use loopback ports **5174/5180/5434**, separate from development. Each test creates its own deterministic customer/driver/vehicle identities; reservation tests release their reservations. The runner tears down only its own stack and volume, including normal setup failures. Run one invocation at a time. It refuses an already running test project; hard interruption may require the explicit cleanup described in the README.

Browser coverage includes real dashboard/list data, URL filters and paging, creation and Draft/Planned edits, assignment, the complete lifecycle, exact persisted event counts, reload persistence, stale-write rejection without overwrite, real overlapping reservations with no partial events, customer creation, driver/vehicle creation/editing and mobile dialog keyboard/focus/overflow checks. The original real API/PostgreSQL smoke test remains.

Backend regression commands from the repository root:

```sh
dotnet build backend/FleetFlow.slnx --no-restore
dotnet test --solution backend/FleetFlow.slnx --no-build --no-restore
```

Docker Desktop's active context was supplied through `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock` on macOS. All 165 backend tests passed against actual PostgreSQL test containers; build had zero warnings/errors. Migration, seed, transactional history and M0–M2 behavior remain covered. Backend source and migration files have no M3 edits.

## Known boundaries

- Local unauthenticated demo; UI role text is informational, not an access-control system. Keep all services on loopback.
- English UI, en-GB formatting and a fixed explicit Vienna timezone; user-selectable timezone/localization is not implemented.
- Automatic updates use normal query refetch/invalidation, without live push. Concurrency checks remain authoritative when data changes elsewhere.
- Only shipments have the existing Version contract. Master data uses the existing M2 update semantics, without invented concurrency tokens.
- Master directories and selectors are paged; server-side master-data search does not exist in M2 and is not invented.
- E2E verification targets Chromium; a mobile viewport is exercised, but no claim of full Safari/Firefox or external assistive-technology certification is made.
- HTTP field mapping uses the available API error fields/codes. Aggregate business conflicts remain in a clear summary where the contract does not identify one field.
- No delete/customer-edit/export/map controls are presented because those APIs are unsupported.
