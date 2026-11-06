# M1 verification report

Verified on 2026-11-06 on macOS 26.2 / Apple Silicon with .NET SDK 10.0.403, EF Core 10.0.14, Npgsql provider 10.0.3 and PostgreSQL 18.6. M1 only; no business API endpoints or frontend features were added.

## Implemented changes

- Domain: Customer, Driver, Vehicle, Shipment and immutable ShipmentEvent entities; Address values; shipment/vehicle/event enums; field normalization, UTC timestamps and decimal payload validation.
- Application: ShipmentService and ShipmentSnapshot; focused IShipmentStore boundary for creation, assignment, transitions and permitted edits.
- Infrastructure: explicit EF configurations, UUID primary/foreign keys, restrictive deletes, unique values, numeric precision, check constraints, query indexes, shipment number sequence, and optimistic concurrency token.
- Migration: `20261106161244_InitialDomain`; a PostgreSQL trigger prevents updating or deleting history events.
- Transactions: state/version/event persistence is atomic, including failure and retry handling. Database resource locks prevent conflicting active reservations across requests and API instances.
- Development tools: explicit `--migrate`/`--seed` commands guarded by Development environment; idempotent transactional fictional seed with fixed entity IDs.
- API foundation: typed failures map to safe 404/409/422 ProblemDetails; unexpected errors remain generic 500. No business controllers were added.
- Documentation: README migration/seed/reset instructions, architecture relationships and ADR 0002.

## Actual command results

Backend commands used a writable isolated `DOTNET_CLI_HOME=/private/tmp/fleetflow-dotnet` and disabled telemetry. Tests used Docker Desktop's active context through `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.

| Command | Final result |
| --- | --- |
| `dotnet build backend/FleetFlow.slnx --no-restore` | Passed, zero warnings and zero errors |
| `dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test --solution backend/FleetFlow.slnx --no-build --no-restore` | 119 passed, zero failed, zero skipped |
| `dotnet ef migrations add InitialDomain --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api --output-dir Persistence/Migrations` | Initial migration generated |
| `dotnet ef migrations has-pending-model-changes --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api` | No model changes after migration |
| `dotnet ef migrations script --idempotent --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api --output /private/tmp/fleetflow-m1-migration.sql` | Script generated successfully outside repository |
| `docker compose build api` | Release API image built with locked dependencies |
| `docker compose config --quiet` | Passed |
| `docker compose run --rm --no-deps api --migrate --seed` | Migration applied and sample data created; repetition reported database up to date |
| `docker compose run --rm --no-deps api --seed` | Passed without duplicating or changing existing sample data |
| `docker compose up -d api` | Updated API running |
| `docker compose restart postgres api` | Services recovered; seeded data and history preserved |
| `git diff --check` | Passed |

The EF commands used the ignored local connection configuration, never an embedded password. Migration execution against fresh PostgreSQL databases is part of the integration suite. An empty-database down-to-zero/up round trip and pending-model checks also pass.

Frontend regression commands ran from `frontend/` with Node 24.22.2. No frontend source or dependency manifests changed:

| Command | Result |
| --- | --- |
| `npm run lint` | Passed |
| `npm run typecheck` | Passed |
| `npm run test -- --run` | 4 passed |
| `npm run build` | Passed |
| `npm run test:e2e` | 1 Chromium test passed against the rebuilt API and actual PostgreSQL |

Playwright emitted the same inherited NO_COLOR/FORCE_COLOR environment warning as M0; the test passed.

## Coverage and database evidence

The 119 backend tests cover the full 25-pair transition matrix, required/invalid shipment data, address/country rules, decimal bounds, capacity, inactive/unavailable resources, assignment locks, duplicate/no-op commands, versioning, allowed edits, actual timestamps, actor validation, UTC normalization, deadline boundaries and terminal status exclusions. Architecture guards continue to enforce the project boundaries.

PostgreSQL integration cases cover:

- Actual migrations, repeated migration, rollback/reapply and model/schema agreement.
- Happy-path creation/assignment/planning/pickup/delivery and exact history/version counts.
- Stale client requests and directly competing EF updates, with no lost update or duplicate event.
- Synchronized parallel planning races for either a shared driver or a shared vehicle: exactly one succeeds.
- Half-open adjacent windows, cancelled reservation release, and revalidation before planning/pickup.
- Capacity validation again when editing an assigned Draft.
- A database trigger that deliberately fails status-event insertion: state/version/history roll back, and retry works in the same service scope after removing the test fault.
- Foreign keys, unique plates/employee numbers/shipment numbers, weight/pallet/schedule/country/version/status/assignment checks, immutable history UPDATE/DELETE rejection, and concurrent sequence allocation.
- Development-only seed guards, refusal to overwrite unrelated data, initial sample correctness and idempotency without moving dates on later runs.
- All existing M0 health/OpenAPI behavior and safe business failure mapping.

Local PostgreSQL queries after repeated initialization and restart returned:

| Data | Count |
| --- | --- |
| Customers | 10 |
| Drivers | 12 |
| Vehicles | 8 |
| Shipments | 36 |
| Shipment events | 120 |
| Draft / Planned / InTransit / Delivered / Cancelled | 6 / 6 / 6 / 12 / 6 |
| Overdue / due within 24 hours at verification | 9 / 9 |

The applied migration is present in `__EFMigrationsHistory`. `/health` and `/health/ready` return 200. The OpenAPI paths remain exactly `/health` and `/health/ready`, confirming the M2 boundary. Production OpenAPI absence remains covered by integration tests.

## Issues resolved during verification

- Constraint test statements were changed to fixed SQL with parameterized identifiers rather than interpolated SQL; no analyzer suppression or disabled check was introduced.
- One MSBuild worker terminated unexpectedly during an early run. A single-worker build succeeded; subsequent normal builds and the final complete test run also passed.
- The initial Docker initialization succeeded but printed a missing optional Kerberos library notice. The local password-based Compose/host connection now explicitly disables GSS negotiation; reinitialization and runtime checks pass without that notice. SSL settings are unchanged. The first empty-database migration also logged its initial migration-history lookup before creating that table; migration execution itself succeeded.

## Limits and next boundary

There are no outstanding failed builds or tests. This is still local development without authentication. Shipment operations are application services, not public HTTP routes; M2 supplies the REST layer and public transport contracts. The frontend intentionally remains the M0 infrastructure screen.

Reservations are one driver/vehicle per overlapping active shipment, using `[pickup, deadline)` intervals. Draft selections do not reserve resources; planning always rechecks. Planned edits are limited to cargo description. License category is informational. Future fleet-update services must coordinate eligibility changes using the same database locks. Privileged direct SQL can bypass application reservation logic; this is not an authentication or database-access-control implementation.

Shipment numbers use a global sequence with a UTC-year prefix; gaps are expected and numbering does not reset annually. Sample dates are anchored at initial seed/reset, not silently shifted on startup. Event IDs are normally generated, while sample entity IDs remain fixed. A non-demo nonempty database is refused by the seeder.

Private instruction documents remain outside the repository. Public candidate files were checked for private document names, actual environment files, and the generated local password; none were included. No commits, pushes, public deployment, authentication, SignalR, AI or extra services were introduced. Stop at M1; M2 requires a separate instruction.
