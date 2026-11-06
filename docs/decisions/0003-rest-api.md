# ADR 0003: REST API and read projections

Status: implemented for M2.

Use controller endpoints under `/api/v1` with dedicated request/response DTOs. API controllers translate HTTP and delegate mutation rules to application/domain services. Application defines framework-independent read models and ILogisticsQueries; Infrastructure implements bounded PostgreSQL projections and aggregates. No EF entity is an HTTP contract. No new dependency package, business schema change, authentication or frontend feature is required.

Reuse M1's shipment operations. Extract their existing transaction implementation into scoped EfTransaction so the new fleet store uses the same commit/rollback/error-translation behavior. FleetService validates through existing domain factories/update methods. Fleet PUT locks the resource row and checks active reservations before committing; concurrent plans use those same locks. Invalid fleet edits roll back tracked changes. Master records do not gain shipment-style versioning in M2; their updates serialize and otherwise use last-write-wins behavior.

Use explicit positive version preconditions for all shipment mutations. Reject unknown request fields and integer JSON enum values to prevent accidental overposting. Shape/model-binding failures are sanitized 400 ValidationProblemDetails; existing semantic failures remain 422, state/resource conflicts 409 and missing resources 404. The server supplies the fixed local demo actor. Offset-qualified timestamps are normalized to UTC.

Shipment reads join customer/driver/vehicle labels in one SQL projection; collections use Count plus bounded Skip/Take. Search is escaped literal ILIKE. Stable ordering is createdAtUtc/id descending. Other collections and event history are also bounded to avoid introducing unbounded list routes. Page size is 1..100; page is 1..1000000. Deadline filters support future clickable KPI cards without adding UI now.

Dashboard totals are conditional SQL aggregates, with recent rows capped at 10. A single TimeProvider instant defines time boundaries. A repeatable-read transaction keeps dashboard counts and recent rows internally consistent. No metrics are stored or calculated by loading all shipments into memory. Lists use normal statement isolation; offset pages may shift under concurrent modifications.

Mutation responses report their committed snapshot and leave optional display names null; GET/list/dashboard enrich labels. This avoids a second post-commit read changing the returned version due to another concurrent mutation. M3 should invalidate/refetch after writes.

First-party OpenAPI remains Development-only. Controller metadata and DTO annotations document routes, validation, codes, filtering, pagination and concurrency; operation transformers attach typed payload examples. The HTTP integration suite submits a generated creation example to detect contract drift. See [API v1 guide](../api/v1.md).

This is local-only, unauthenticated development. Existing loopback bindings are preserved. No cloud deployment or public endpoint exposure is part of M2.
