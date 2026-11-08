# Local setup and recovery

FleetFlow runs as a documented split: PostgreSQL and ASP.NET Core in Docker, Vite on the host. All published ports bind to 127.0.0.1. Identity authentication is enabled; deployment hardening is incomplete. Do not use a public tunnel, LAN bind or public reverse proxy.

## Prerequisites

- Docker Desktop (macOS/Windows with WSL2) or Docker Engine/Compose v2 on Linux, running Linux containers.
- Node 24.22.2 from `.nvmrc` and npm. `nvm install && nvm use` selects it where nvm is installed.
- POSIX shell and OpenSSL. Python 3 is used by audit/coverage helpers.
- .NET SDK 10.0.403 for host C# development/tests, pinned by `global.json`.
- Package/image/browser installation requires network access and trusted certificates. Do not bypass TLS verification to fix a proxy/certificate problem.

macOS Apple Silicon is locally verified. GitHub Actions targets Ubuntu 24.04; the M4.1 hosted baseline was reported green; the M5a changes still require a new hosted run. For Windows use WSL2 and enable Docker integration; native PowerShell equivalents are not currently supplied.

## First run

From the repository root, `sh scripts/dev.sh` performs the following steps:

```sh
sh scripts/setup-env.sh
docker compose -p fleetflow up --build -d --wait
docker compose -p fleetflow run --rm --no-deps api --migrate --seed
sh scripts/demo-users.sh
npm ci --prefix frontend
npm run dev --prefix frontend
```

The environment helper creates `.env` with mode restricted by umask 077 and a random password. It preserves an existing file. `.env.example` documents the variables without containing an actual credential. Docker Compose reads this file; browser assets receive no database credentials.

Open http://127.0.0.1:5173 and sign in. Generated Development passwords are in ignored `.env.demo`; see [account provisioning](authentication.md#local-demo-accounts). Liveness is `/health`; database connectivity is `/health/ready`. The top-bar health menu retains an explicit retry. A normal API start never migrates/seeds automatically: initialization is a separate Development-only CLI operation that exits afterward.

Seed data includes 10 customers, 12 drivers, 8 vehicles and 36 shipments with mixed status/history. Repeated seeding is idempotent; deadlines do not shift with every restart. Use `--migrate` alone for an empty schema, `--seed` only after migration. A non-demo nonempty database is refused rather than overwritten.

## Configuration

Root Compose variables: `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_PORT` (5433) and `API_PORT` (5080). Changing credentials does not change the password already stored in an existing PostgreSQL volume; retain the original credential or perform an intentional reset.

Frontend defaults to same-origin requests through Vite. To change the API port, set `API_PROXY_TARGET=http://127.0.0.1:5081` in the Vite process environment or ignored `frontend/.env.local` (see `frontend/.env.example`). `VITE_API_BASE_URL` is public build configuration; no secrets belong in it. Direct cross-origin browser calls require an explicit backend CORS policy, which this local setup does not enable.

Vite binds to loopback with a strict port. `npm run preview` previews static build output without the API proxy; use `npm run dev` for the documented full local application.

## Host C# development

Stop only the Compose API, retain PostgreSQL, and restore the pinned tools:

```sh
docker compose -p fleetflow stop api
docker compose -p fleetflow up -d postgres
dotnet tool restore
dotnet restore backend/FleetFlow.slnx --locked-mode
```

Configure `ConnectionStrings:FleetFlow` through ASP.NET Core user secrets or a process environment variable. For example, substitute your local values in your own terminal (never commit them):

```sh
export ConnectionStrings__FleetFlow='Host=127.0.0.1;Port=5433;Database=fleetflow;Username=fleetflow;Password=<your-local-password>;GSS Encryption Mode=Disable'
dotnet watch --project backend/src/FleetFlow.Api
```

The launch profile selects Development and localhost:5080. ASP.NET Core does not automatically read Compose's `.env`. Host tools connect to port 5433; API containers connect to `postgres:5432`. EF commands use the same connection configuration:

```sh
dotnet ef database update --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api
dotnet run --project backend/src/FleetFlow.Api -- --seed
```

Production-environment seeding is rejected. Identity remains required in every environment. Production cookies require HTTPS; merely choosing Production does not provide a complete deployment configuration.

## Stop and intentional reset

Ctrl+C stops the foreground Vite server. `docker compose -p fleetflow down` preserves the database volume. Inspect only needed logs, without dumping environment configuration:

```sh
docker compose -p fleetflow ps
docker compose -p fleetflow logs --tail=100 api postgres
curl --fail http://127.0.0.1:5080/health/ready
```

`sh scripts/reset-demo.sh --confirm` explicitly deletes **only the fleetflow development project's volume**, rebuilds services and initializes a fresh migration/seed. It refuses to run without that exact confirmation flag. No reset was performed against the existing developer database during M4 verification.

API containers use a read-only root filesystem with a bounded `/tmp` tmpfs. PostgreSQL persists in its named volume at the PostgreSQL 18 image's `/var/lib/postgresql` location. Direct SQL and privileged database users can bypass application reservation rules; this is not production database access control.

## Common problems

- **White/unavailable browser page:** run Vite, open the http URL, and check `npm run typecheck --prefix frontend`. Opening `index.html` directly does not start the application.
- **Port already used:** inspect the owning process; do not kill an unknown process. Change documented dev ports or stop your own prior server. E2E uses its separate reserved ports.
- **API connected but business pages fail:** verify that migration/seed initialization completed; readiness certifies connectivity, not schema currency.
- **Docker socket on macOS:** `scripts/test-backend.sh` automatically selects Docker Desktop's active context. Manual commands may need `DOCKER_HOST="$(docker context inspect --format '{{.Endpoints.docker.Host}}')"` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.
- **Restricted first-run home for .NET:** an automation sandbox may need a writable `DOTNET_CLI_HOME`. A normal developer terminal usually does not.
- **Dependency/certificate failure:** use the required runtime and configure trusted corporate/system certificates. Do not disable package audit or TLS validation.

## M5b live updates

No new environment secret, schema migration or external service is required. Rebuild the API with `docker compose up -d --build api` and restart `npm run dev` from `frontend/` after updating; Vite must load the new `/hubs` WebSocket proxy. Preserve the existing database volume and `.env.demo`. Use separate browser profiles and different managed Dispatcher accounts to observe automatic updates. The top bar indicates connection state; REST remains usable during reconnect and active queries reconcile in the background. See [the real-time guide](realtime.md). This milestone's hosted execution is separately unverified until its exact revision passes all five workflow jobs.

## M6 operational insights

Apply the additive ledger migration without resetting existing data:

```sh
docker compose -p fleetflow build api
docker compose -p fleetflow run --rm --no-deps api --migrate
docker compose -p fleetflow up -d api
```

Keep `.env` and `.env.demo`; no new seed/reset is needed. The Insights page works without credentials. External inference stays disabled unless the optional [Cloudflare manual verification/configuration](insights.md) is completed. Root `.env` variables are forwarded by Compose; host .NET uses process environment/development secrets instead. No AI key belongs in frontend configuration. [Future same-origin free deployment](../ZERO_COST_DEPLOYMENT.md) remains preparation only.
