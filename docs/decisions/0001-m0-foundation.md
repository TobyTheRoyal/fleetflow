# ADR 0001: M0 foundation

Status: accepted.

## Decision

Use a modular monolith with four .NET projects, controller-based HTTP endpoints, EF Core/PostgreSQL, and a React/TypeScript SPA. Keep Domain independent and Application dependent only on Domain. Infrastructure implements persistence. API composes the implementation using built-in dependency injection.

Use React Router for routing and TanStack Query for server state. Vite proxies API requests in local development. PostgreSQL and API run in Compose; frontend development runs on the host. Docker publishes services only on loopback. Use PostgreSQL 18 and stable .NET/EF Core/Npgsql 10 releases.

Use xUnit with Microsoft Testing Platform and Testcontainers against real PostgreSQL. Use Vitest/React Testing Library for frontend states and a Playwright smoke test against the running stack.

## Rationale and consequences

Separate assemblies enforce dependency direction and make .NET responsibilities visible, with a small amount of project configuration overhead. Controllers make HTTP conventions explicit. EF Core already provides change tracking and a unit of work, so generic repositories and extra dispatch frameworks would add indirection without a current need.

The split local setup provides fast frontend reloads and consistent API/database runtimes. It requires Node on the host; a full containerized frontend is deferred. Authentication is absent, so deployment remains local-only. OpenAPI JSON is sufficient for M0; an interactive API UI is optional later.

No business schema or fabricated application data is created. Empty inner assemblies are deliberate until M1. Public documentation is independently authored; private project instructions remain outside version control.

## Development dependency security

The current ESLint and typescript-eslint dependency ranges resolve to minimatch 10, affected by GHSA-g3hv-75wg-m249. A temporary npm override pins minimatch 11.0.1 for these development tools. Both use its supported named API; lint, typecheck, unit tests, and the production build are verified with the override. Reassess it when upstream dependency ranges adopt the patched major. npm audit reports no known vulnerabilities with the pinned lockfile at M0 verification time.
