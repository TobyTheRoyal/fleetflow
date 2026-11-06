# FleetFlow

FleetFlow is an independently designed personal portfolio project for logistics and fleet management, built to demonstrate C#/.NET backend architecture, PostgreSQL persistence, React/TypeScript, and automated testing.

**Current milestone: M1 — domain model, persistence, and business logic.** The domain and application services support shipment rules and transactional history against PostgreSQL. Business HTTP endpoints and frontend shipment workflows are not implemented yet. This unauthenticated environment is for local development only.

## Requirements

- .NET SDK **10.0.403** (stable .NET 10; `global.json` allows compatible patch updates).
- Node.js **24.22.2** and npm (use `nvm install && nvm use` if nvm is available).
- Docker Desktop with a running Linux container engine and Docker Compose v2.
- Git; OpenSSL for the local environment setup helper.

Apple Silicon is supported using native ARM64 .NET, Node, and container images. No database server or `psql` installation is needed on the host. Package restores, image pulls, and the first browser install require network access.

## Quickstart

Run these commands from the repository root:

```sh
sh scripts/setup-env.sh
docker compose up --build -d
docker compose ps
curl --fail http://127.0.0.1:5080/health
curl --fail http://127.0.0.1:5080/health/ready
```

The setup helper creates an ignored `.env` with a randomly generated database password and leaves an existing file unchanged. Alternatively, copy `.env.example` to `.env` and replace its password placeholder with `openssl rand -hex 24`. Never commit actual environment files or passwords.

Then start the frontend in another terminal:

```sh
cd frontend
npm ci
npm run dev
```

Open [FleetFlow](http://127.0.0.1:5173). The connection card checks the real API and PostgreSQL. A healthy connection is reported only after the dependency check succeeds.

| Service | Default address |
| --- | --- |
| Frontend | <http://127.0.0.1:5173> |
| API liveness | <http://127.0.0.1:5080/health> |
| API/database readiness | <http://127.0.0.1:5080/health/ready> |
| Development OpenAPI JSON | <http://127.0.0.1:5080/openapi/v1.json> |
| PostgreSQL | `127.0.0.1:5433` |

Vite proxies `/health`, `/api`, and `/openapi` to the API. If you change `API_PORT` in `.env`, start Vite with the corresponding target, for example `API_PROXY_TARGET=http://127.0.0.1:5081 npm run dev`. These are public addresses, not secrets. `.env` is for Compose; the browser receives no database credentials.

Liveness returns `{"status":"Healthy"}` without requiring the database. Readiness returns the same payload when connected, or HTTP 503 with safe ProblemDetails containing `code: "dependency_unavailable"` and `traceId`. OpenAPI is unavailable outside Development.

## Running the API directly for C# development

Run only PostgreSQL in Compose, then start the API on the host. Stop a running Compose API first to free its port:

```sh
docker compose stop api
docker compose up -d postgres
dotnet tool restore
dotnet restore backend/FleetFlow.slnx --locked-mode
```

In a local development shell, load the generated environment and export the ASP.NET Core connection setting:

```sh
set -a
. ./.env
set +a
export ConnectionStrings__FleetFlow="Host=127.0.0.1;Port=${POSTGRES_PORT};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};Timeout=5;Command Timeout=5;GSS Encryption Mode=Disable"
dotnet watch --project backend/src/FleetFlow.Api
```

The launch profile selects Development and `http://localhost:5080`. ASP.NET Core does not automatically read Compose's `.env`. For a persistent host-only setup, user secrets are another supported configuration source; do not put credentials in `appsettings.json`. Container communication uses `postgres:5432`, whereas host tools use the published localhost port.

## Build, formatting, and tests

From the repository root:

```sh
dotnet tool restore
dotnet restore backend/FleetFlow.slnx --locked-mode
dotnet build backend/FleetFlow.slnx --no-restore
dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore
dotnet test --solution backend/FleetFlow.slnx --no-restore
```

The solution uses xUnit and Microsoft Testing Platform. Unit tests guard architecture boundaries, input validation, assignment eligibility, transitions, and deadline classification. Integration tests start an isolated PostgreSQL 18 container through Testcontainers; Docker must be running. They do not use, reset, or depend on the development database. They cover migrations, constraints, fictional seeds, atomic shipment/history writes, concurrent reservations, stale updates, and all M0 health/OpenAPI behavior. Each M1 test uses a fresh isolated database.

If Testcontainers cannot find Docker Desktop's socket on macOS, use the active Docker context:

```sh
export DOCKER_HOST="$(docker context inspect --format '{{.Endpoints.docker.Host}}')"
export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
dotnet test --solution backend/FleetFlow.slnx --no-restore
```

If an automation sandbox blocks .NET's first-run files, set `DOTNET_CLI_HOME` to a writable temporary directory before running these commands. A normal terminal generally does not require this workaround.

From `frontend/`:

```sh
npm ci
npm run lint
npm run format:check
npm run typecheck
npm run test -- --run
npm run build
```

Vitest/React Testing Library cover connection loading, success, failure/retry, and invalid API responses. Frontend tests mock HTTP intentionally; the browser smoke test uses the actual API and PostgreSQL:

```sh
# First start PostgreSQL and API using the quickstart.
cd frontend
npx playwright install chromium
npm run test:e2e
```

Playwright starts Vite automatically if it is not already running. Use `npm run format` or `dotnet format backend/FleetFlow.slnx` to apply formatting.

## Stop, inspect, and reset

```sh
docker compose logs --tail=100 api postgres
docker compose exec postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
docker compose down
```

`docker compose down` preserves PostgreSQL's named volume. Stop Vite with Ctrl+C. To **delete all local FleetFlow database contents**, explicitly run:

```sh
docker compose down --volumes
docker compose up --build -d
```

M1 schema/data initialization is explicit. After building the API image, apply the migration and optionally seed fictional development data:

```sh
docker compose run --rm --no-deps api --migrate --seed
```

PostgreSQL must already be running (`docker compose up -d postgres`). Use `--migrate` alone for an empty schema. Use `--seed` alone only after migration. These CLI operations exit after completion and reject environments other than Development. A normal API start never migrates or seeds automatically.

For a host-run API, export `ConnectionStrings__FleetFlow` as shown above, then run:

```sh
dotnet ef database update --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api
dotnet run --project backend/src/FleetFlow.Api -- --seed
```

The launch profile selects Development for `dotnet run`. The seed is idempotent: rerunning preserves existing sample IDs, dates, versions, and history. It adds 10 fictional customers, 12 drivers, 8 vehicles, 36 shipments, and their history. Initial overdue/due-soon records are relative to the first seed; they are not moved at every restart. The seeder refuses an existing non-demo database instead of overwriting it. To regenerate relative sample dates, intentionally reset the local volume, restart services, and run initialization again. No real customer or driver data is included. Changing the database username/password in `.env` does not change credentials already stored in an existing volume; retain the original values or intentionally reset your local database.

## Troubleshooting

- Docker errors: check `docker version` and `docker compose version`; start Docker Desktop if needed.
- Port conflicts: change `POSTGRES_PORT`/`API_PORT` in `.env`; update the Vite proxy target if the API port changes.
- Connection error: inspect `docker compose ps` and logs, then request `/health/ready`; `/health` alone does not certify PostgreSQL connectivity.
- NuGet/npm/image download errors: check network, proxy, and trusted certificate configuration. Do not disable TLS validation.
- Node engine errors: use the version declared in `.nvmrc`, including in automated terminals.
- `npm run preview` serves static build output and does not provide the development API proxy; use `npm run dev` for the documented local application setup.

## Structure and roadmap

See [architecture](docs/architecture.md), [foundation decisions](docs/decisions/0001-m0-foundation.md), the [M0 verification report](docs/m0-verification.md), and [M1 domain/persistence decisions](docs/decisions/0002-domain-persistence.md), and the [M1 verification report](docs/m1-verification.md).

- **M0:** environment, four backend layers, health checks, frontend foundation, tests.
- **M1:** domain rules, relational schema, migrations, fictional seeds, transactions, concurrency.
- **M2:** business REST APIs, filtering, dashboard aggregation, API integration coverage.
- **M3:** database-backed frontend workflows and end-to-end business tests.
- **M4:** expanded quality checks, GitHub Actions CI, documentation, screenshots, portfolio packaging.
- Optional later: established authentication, SignalR, scoped AI assistance, and mobile exploration.

GitHub Actions, screenshots, a business ER diagram, public deployment, and demo credentials are not provided in M0. No business HTTP endpoints, frontend shipment features, authentication, AI, paid APIs, or additional services have been introduced.

Licensed under [MIT](LICENSE).
