# FleetFlow architecture

FleetFlow is an independently designed personal portfolio project for logistics operations. M0 provides infrastructure only. There are no shipment entities, migrations, seed data, business endpoints, authentication, or dashboard metrics yet.

## Backend boundaries

```mermaid
flowchart TD
  Api[FleetFlow.Api] --> Application[FleetFlow.Application]
  Api --> Infrastructure[FleetFlow.Infrastructure]
  Infrastructure --> Application
  Infrastructure --> Domain[FleetFlow.Domain]
  Application --> Domain
  Infrastructure --> PostgreSQL[(PostgreSQL)]
```

Arrows represent project references. API references Infrastructure to compose dependency injection at startup. Controllers handle HTTP; Application will orchestrate use cases; Domain will own business invariants; Infrastructure implements persistence. Domain and Application are intentionally empty assemblies in M0.

Use controllers consistently for application HTTP endpoints. Health endpoints also use a controller. First-party ASP.NET Core OpenAPI is exposed only in Development. No generic repository, mediator framework, event bus, or custom unit-of-work abstraction is introduced. EF Core's DbContext provides change tracking and the persistence unit of work. DTOs will be separate from entities.

The DbContext uses EF Core 10 and the Npgsql 10 provider. A health check exercises its actual PostgreSQL connection; it does not create a schema. M1 introduces versioned migrations and development-only fictional seeds. Never use EnsureCreated as a substitute for migrations. Integration tests use isolated real PostgreSQL 18 containers, independent of the development database.

## HTTP foundation

| Route | Availability | Behavior |
| --- | --- | --- |
| `GET /health` | All environments | Liveness, 200 with `{"status":"Healthy"}`; no database dependency |
| `GET /health/ready` | All environments | Database connectivity, 200 when healthy; safe ProblemDetails with 503 otherwise |
| `GET /openapi/v1.json` | Development only | Generated API contract |

Problems include a machine-readable `code` and request `traceId`. Unexpected exceptions receive a generic 500 response; connection credentials and exception details are never returned. Future business endpoints use `/api/v1` and explicit status/assignment commands. No CORS policy is needed locally: Vite proxies same-origin requests to the API.

## Frontend

React Router and TanStack Query providers are registered at the application boundary. TanStack Query owns the readiness request and retry state. The English M0 screen shows only actual infrastructure connectivity, with loading, error, retry, and success states. No fake shipments or metrics are displayed.

Tailwind CSS and a shadcn/ui-compatible Radix button establish the UI foundation. `components.json` and the `@/` alias support subsequent shadcn components. Features will be organized by dashboard, shipments, customers, drivers, and vehicles when implemented. Forms and generated business API types belong to later milestones.

## Development and versioning

PostgreSQL and the non-root API run in Docker; Vite runs locally. Published ports bind to loopback. PostgreSQL 18 stores data in a named volume mounted at `/var/lib/postgresql`, following the image's version-specific layout. Development has no authentication and is not suitable for public deployment.

`global.json` pins the stable .NET 10 SDK and selects Microsoft Testing Platform for xUnit. Central NuGet versions and committed package lockfiles keep dependencies coherent. Node is declared in `.nvmrc`; npm uses exact direct versions and a committed lockfile. Container tags are explicit; upgrades should update the pins and rerun the same verification commands.

## Next milestone boundary

M1 adds entities, migrations, constraints, seed data, domain/application validation, transactional event history, and concurrency tests. Shipment versions protect stale updates; resource reservations also require database coordination across different shipments. These rules are not implemented in M0. HTTP business endpoints follow in M2, frontend workflows in M3, and CI/portfolio packaging in M4.
