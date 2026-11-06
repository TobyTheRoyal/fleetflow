# ADR 0002: Domain rules and PostgreSQL persistence

Status: accepted for M1.

## Responsibilities

The domain owns validated master data, address values, shipment state transitions, payload eligibility, timestamps, and append-only history. Entities have private setters and factory methods. Application's `ShipmentService` orchestrates commands through the focused `IShipmentStore` persistence port. Infrastructure supplies EF Core, transactions, resource locks, atomic number allocation, migrations, and development seeding. No generic repository, extra unit-of-work framework, or business HTTP endpoint is added.

`ShipmentSnapshot` is an application result DTO. It does not expose EF change tracking. HTTP request/response contracts remain an M2 task. The M0 global exception handler recognizes typed failures with safe codes: validation 422, conflict 409, missing resource 404; unexpected exceptions stay generic 500.

## Business decisions

- Pickup is a single start instant; no separate pickup-window end is introduced.
- Deadlines must be strictly later than pickup. UTC-normalized DateTimeOffset values map to PostgreSQL timestamptz. Named display zones remain a frontend concern.
- Positive weights use decimal(12,2); excessive scale is rejected rather than silently rounded by the domain.
- Driver employee numbers and vehicle plates are trimmed and uppercased before uniqueness checks.
- Address countries use the ISO 3166-1 alpha-2 set. Domain validation and database constraints use the same set.
- Draft fields can be edited. Planned edits are limited to cargo description. All other statuses are locked for editing.
- Assignments require both driver and vehicle in one action. Draft selections do not reserve capacity, but are checked against existing active reservations and revalidated when planning. A changed Draft weight is rechecked against its selected vehicle.
- Reservations use half-open intervals `[pickup, deadline)`; adjacent windows are allowed. Only Planned/InTransit shipments reserve resources. The one-shipment-per-resource assumption is an MVP simplification; multi-shipment trips require a future model.
- InTransit records server time as actual pickup, Delivered records server time as actual delivery. License category is informational, as specified.
- Same-status commands and unchanged assignments/edits return explicit conflicts rather than duplicate history.
- Every accepted shipment mutation increments Version. Created, AssignmentChanged, StatusChanged, and DetailsChanged events are saved with the shipment. Events carry the supplied application actor; M2 must source it from the server-controlled demo context, and later authentication must supply verified identity.

## Concurrency and transactions

Version is an explicit EF concurrency token. An application check catches already-stale client versions; EF's update predicate catches races after loading. DbUpdateConcurrencyException becomes a `stale_version` conflict. A failed command clears tracked pending changes before the scope can be reused.

Versions alone cannot prevent reservations across different shipments. Command transactions acquire PostgreSQL row locks in Driver-then-Vehicle order and requery overlaps under ReadCommitted before reserving. Every future fleet mutation that changes eligibility must use the same coordination protocol. No process-local lock is used. Direct privileged SQL can bypass application-level reservation checks; database constraints still enforce static row validity and foreign keys. Restrict database access in a future deployed environment.

A PostgreSQL sequence allocates human-readable shipment numbers atomically. It is global, with the current UTC year prefixed and a minimum six-digit counter. It does not reset annually and may contain gaps, including after failed transactions.

History is append-only, additionally enforced by a database trigger against UPDATE/DELETE. It is not a cryptographic audit system; a privileged database owner can alter the schema. Migration rollback removes the trigger explicitly.

## Development data

Migration and seeding are explicit CLI operations, never automatic on API startup. Both initialization flags and the seeder reject non-Development environments. Seed generation uses deterministic master/entity IDs and a relative time anchor on the first seed; subsequent calls preserve existing dates and changes. Event IDs are generated normally. A transaction and advisory lock protect concurrent seed runs. A nonempty database without the sample marker is refused instead of overwritten.

The fictional sample contains 10 customers, 12 drivers, 8 vehicles, and 36 shipments spanning all five statuses. All emails use the reserved `.example` namespace. At initial seeding there are at least three overdue and four due-soon active records; those counts naturally change with time. Resetting the local volume is intentionally destructive and must be explicit.

## Local connection configuration

The password-based local Compose connection sets `GSS Encryption Mode=Disable` explicitly. Npgsql defaults to preferring GSS and otherwise probes an optional Kerberos native library missing in the slim runtime image. This local setting avoids an unnecessary native dependency. It does not change SSL settings or configure any public deployment. See [Npgsql connection parameters](https://www.npgsql.org/doc/connection-string-parameters.html).
