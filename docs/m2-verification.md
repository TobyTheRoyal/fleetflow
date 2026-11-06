# M2 verification report

Verified on 2026-11-06 with .NET SDK 10.0.403, EF Core 10.0.14, Npgsql provider 10.0.3, PostgreSQL 18.6 and Node 24.22.2 on macOS / Apple Silicon. M2 only. No frontend source, dependency package version, database schema, authentication, SignalR or AI feature was added.

## Delivered API

All 16 specified operations are implemented under `/api/v1`: dashboard summary; shipment list/detail/create/update/assignment/status/history; customer list/create; driver list/create/update; vehicle list/create/update. Health routes remain unchanged.

Controllers accept dedicated request DTOs, return dedicated response DTOs, use built-in model binding and sanitized validation responses, and delegate business operations to Application. Existing M1 shipment services and transaction rules are reused. Shared EfTransaction now also supports fleet operations. Domain updates reuse established factory validation. Fleet mutations lock the same resource rows used for reservations and reject changes that invalidate active assignments.

Application owns framework-independent query interfaces and read models. Infrastructure projects joined read DTOs without tracked entities, filters and pages in SQL, and computes dashboard counts with SQL aggregates. Page size is bounded to 100; recent dashboard rows to 10. Shipment ordering includes an explicit UUID tie-breaker. Tests observe exactly two SELECT commands for a shipment page and two for dashboard summary; there is no per-row entity loading or in-memory KPI aggregation.

The final OpenAPI document has named enum values, correct date-time schemas, camelCase query parameters, positive-version preconditions, DTO validation metadata, representative request/response examples and application/problem+json error responses. Numeric JSON strings and integer JSON enum values are rejected. OpenAPI remains Development-only. See [API v1](api/v1.md) and [ADR 0003](decisions/0003-rest-api.md).

## Actual build and test results

Backend commands ran from the repository root with `DOTNET_CLI_HOME=/private/tmp/fleetflow-dotnet` and telemetry disabled. PostgreSQL integration tests used Docker Desktop's active context via `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.

| Command | Final result |
| --- | --- |
| `dotnet build backend/FleetFlow.slnx --no-restore` | Passed, zero warnings and zero errors |
| `dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test --solution backend/FleetFlow.slnx --no-build --no-restore` | 165 passed, zero failed, zero skipped |
| `dotnet ef migrations has-pending-model-changes --no-build --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api` | No schema changes after M1 migration |
| `dotnet ef migrations list --no-build --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api` | InitialDomain migration remains applied |
| `docker compose build api` | Passed, Release image using locked dependencies |
| `docker compose up -d api` | Final image running on loopback-only published ports |
| `docker compose restart postgres api` | Recovery verified; completed shipment and immutable history preserved |
| `git diff --check` | Passed |

The complete backend suite includes all 119 existing M0/M1 tests and 46 new HTTP cases. Every M2 HTTP test uses a fresh migrated PostgreSQL database; no EF InMemory provider is used. Existing migration tests still apply, repeat, roll back and reapply the migration.

From `frontend/`, with Node 24.22.2 selected:

| Command | Result |
| --- | --- |
| `npm run lint` | Passed |
| `npm run format:check` | Passed |
| `npm run typecheck` | Passed |
| `npm run test -- --run` | 4 passed |
| `npm run build` | Passed |
| `npm run test:e2e` | 1 Chromium test passed against the final API/PostgreSQL stack |

Playwright emitted the inherited NO_COLOR/FORCE_COLOR warning; it did not affect the successful test. Frontend sources and manifests are unchanged.

## HTTP and persistence evidence

A separate live HTTP check against the actual Compose API created entirely fictional customer, driver, vehicle and shipment records. The shipment is `FF-2026-000037`, ID `01a1b1d5-7bf4-7fdf-8f91-2494fc1e2abb`.

Observed lifecycle:

1. POST shipment: Draft, version 1, HTTP 201.
2. Unassigned planning attempt: 409/assignment_required, no change.
3. POST assignment: Draft, version 2.
4. Direct Draft-to-Delivered attempt: 409/invalid_transition, no change.
5. POST Planned: version 3.
6. POST InTransit: version 4, actual pickup set by server.
7. POST Delivered: version 5, actual delivery set by server.
8. Stale version 4 command: 409/stale_version, no change.
9. GET detail/history: Delivered, version 5, exactly five events: Created, AssignmentChanged, and three StatusChanged. Actor is local-dispatcher throughout.
10. Dashboard: delivered count increased from 12 to 13; total non-cancelled count increased by one.
11. Restarted both API and PostgreSQL, then repeated GET detail/history: identical identity, final version and event count.

The live check intentionally leaves this one additional fictional completed shipment and its three master records in the local development database. Existing seed records were not overwritten or reset. No public network exposure or deployment occurred.

Live OpenAPI inspection confirms all 16 operations, the specified query names, named enum values, integer version schema, and ProblemDetails content types. A non-JSON Accept header was also probed for a missing resource: the response remained safe application/problem+json without stack details. Existing health endpoints return Healthy and the current browser smoke flow still works.

## Integration coverage

- Complete shipment lifecycle through HTTP, creation Location, enriched detail and exact immutable events.
- Unassigned planning, invalid/backwards/repeated transitions, terminal Delivered, stale versions and unchanged assignments.
- Inactive drivers, unavailable vehicles, payload overload, failed assigned Draft edits, and permitted Planned description edits.
- Synchronized HTTP races for a shared driver or a shared vehicle: exactly one plan succeeds.
- Synchronized fleet deactivation/maintenance versus planning: incompatible operations cannot both succeed.
- Fleet resource-in-use checks and rollback, normalized unique values, valid master updates and missing resources.
- A deliberately failing PostgreSQL event trigger: HTTP 500 is sanitized, shipment remains Draft/version 2, and no extra event is committed.
- All collection paging, stable timestamp ties, combined filters, literal wildcard/SQL-like search text, empty pages, and SQL query counts.
- Seeded dashboard totals, terminal exclusions, TimeProvider changes, and exact now/24-hour deadline boundaries.
- Malformed bodies, bad UUIDs/enums, missing versions, invalid page ranges, unknown fields/actor overposting, offset-less timestamps, and numeric strings.
- Semantic 422 errors without shipment/event writes; safe machine-readable 400/404/409/422/500 responses.
- Chronological paged history, no event mutation route, and API host restart persistence.
- OpenAPI examples submitted through HTTP, schema/value validation, expected error media types, query parameter spelling, and absence of EF entities from contracts.

## Issues resolved during verification

The live contract inspection found that MVC's default response metadata advertised JSON media types for ProblemDetails, while the actual API returned application/problem+json. The operation transformer now aligns the document with actual error responses, with a regression assertion.

OpenAPI reads HTTP JSON options separately from MVC controller options. Both are now configured consistently for strict numeric JSON and named enums. Tests check the enum allowed-value sets semantically: JSON Schema may represent string-only enum values without an explicit type keyword. This avoids relying on a particular serializer's optional schema formatting. Timestamp format and actual example execution are also checked.

There are no remaining failed builds or tests.

## Boundaries and limitations

M3 has not started: the frontend remains the M0 infrastructure screen. The API is unauthenticated and for local development only; local-dispatcher is a server-controlled demo label, not verified identity.

Existing M1 limits remain: one shipment per overlapping active driver/vehicle reservation, pickup-to-deadline windows, informational license category, no annual shipment sequence reset, and development-only explicit seeding. Planned edits remain limited to cargo description.

All collections are offset-paged, so pages may shift between requests under concurrent writes. No cross-request snapshot/cursor is promised. Dashboard's aggregate/recent rows do share an internal repeatable-read snapshot. Master updates serialize on resource locks and otherwise use last-write-wins; shipment Version is not extended to master records. Mutation display-name labels are intentionally null; read endpoints enrich them, and clients should refetch after writes.

Privacy scans exclude private instruction Markdown, actual environment files and the generated local password from tracked/untracked public candidate files. No commits or pushes were made. No public deployment, additional services or M3 features were introduced. Stop after M2.
