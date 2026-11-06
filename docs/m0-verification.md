# M0 verification report

Verified on 2026-11-06 on macOS 26.2 / Apple Silicon. M0 only; no M1 functionality was implemented.

## Implemented scope

- Four .NET 10 projects with explicit dependency direction and a `.slnx` solution.
- Controller-based liveness and PostgreSQL readiness routes; safe ProblemDetails and global exception handling.
- EF Core/Npgsql connection registration, without entities, migrations, or seed data.
- Development-only first-party OpenAPI.
- React/TypeScript/Vite foundation with Tailwind, a shadcn/ui-compatible button, React Router, and TanStack Query.
- An English infrastructure screen with real readiness, loading, failure, and retry states.
- PostgreSQL/API Compose setup, persistent volume, loopback port bindings, and non-root API image.
- Generated local credentials, ignored environment files, public setup/architecture documentation, dependency pins and lockfiles.
- xUnit architecture checks, real PostgreSQL API integration tests, frontend component tests, and a real-stack Chromium smoke test.

## Commands and final results

Backend commands were run from the repository root with `DOTNET_CLI_HOME=/private/tmp/fleetflow-dotnet` and telemetry disabled. This writable CLI home avoids the automation sandbox's restriction on the default first-run location. Testcontainers used the active Docker context via `DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.

| Command | Final result |
| --- | --- |
| `dotnet tool restore` | Passed; dotnet-ef 10.0.14 restored |
| `dotnet restore backend/FleetFlow.slnx --locked-mode` | Passed for all six projects |
| `dotnet build backend/FleetFlow.slnx --no-restore` | Passed, zero warnings and zero errors |
| `dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test --solution backend/FleetFlow.slnx --no-restore` | Passed: 11 tests, zero skipped; 5 architecture tests and 6 API/PostgreSQL integration tests |
| `dotnet ef --version` | 10.0.14 |
| `dotnet list backend/FleetFlow.slnx package --vulnerable --include-transitive` | No known vulnerable packages reported in any project |

Frontend commands were run from `frontend/` with Node 24.22.2 explicitly selected. The elevated automation shell initially selected Node 23; the supported runtime was selected for the clean install and all final checks.

| Command | Final result |
| --- | --- |
| `npm ci` | Passed using the committed lockfile |
| `npm run lint` | Passed with zero lint warnings |
| `npm run format:check` | Passed |
| `npm run typecheck` | Passed |
| `npm run test -- --run` | Passed: 4 component tests |
| `npm run build` | Passed with Vite 8.3.6 |
| `npm audit --audit-level=moderate` | Zero known vulnerabilities reported |
| `npx playwright install chromium` | Installed ARM64 Chromium |
| `npm run test:e2e` | Passed: 1 Chromium test against the running API and PostgreSQL |

The npm install displayed an optional `fsevents` install-script approval notice. It did not prevent installation, Vite operation, builds, or tests. No global npm policy was changed. Playwright also displayed an inherited `NO_COLOR`/`FORCE_COLOR` environment warning; the browser test passed.

## Runtime checks

- `sh scripts/setup-env.sh` generated an ignored local `.env` with a random password.
- `docker compose config --quiet` passed.
- `docker compose up --build -d` built the Release API image and started both services.
- `docker compose ps` showed PostgreSQL healthy and the API running, published only on `127.0.0.1`.
- `docker compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'` reported accepting connections.
- `curl --fail http://127.0.0.1:5080/health` and `/health/ready` returned 200 and `{"status":"Healthy"}`.
- With only FleetFlow's PostgreSQL service temporarily stopped, `/health/ready` returned 503 with `dependency_unavailable` and a trace identifier; `/health` still returned 200.
- Restarting PostgreSQL restored readiness. Restarting both services preserved `pg_control_system().system_identifier`, verifying reuse of the persistent cluster volume without creating a business schema.
- The Chromium smoke test verified the frontend's actual readiness response, working retry action, and proxied OpenAPI document.
- Desktop (1440×1000) and mobile (390×844) screenshots were visually inspected. No page exceptions or mobile horizontal overflow were found. Temporary screenshots remain outside the repository.

## Issues resolved during verification

The initial unavailable-database test found that adding a custom ProblemDetails code through `ControllerBase.Problem` collided with the global default code and caused a 500. The controller now creates the problem through the factory and assigns the specific code before returning it. Both the automated failure test and live stopped-database check pass with 503.

Initial frontend dependencies included vulnerable test-tool versions. Stable updates removed those findings. A documented temporary minimatch 11.0.1 override addresses the current linter transitive dependency advisory; all frontend checks pass with it. See ADR 0001.

## Privacy, limitations, and next boundary

`git diff --check` passed. Ignore checks and inspection of tracked/untracked public candidate files confirmed that private instructions, actual environment files, and the generated password are excluded. Nothing was committed, pushed, or deployed publicly.

Domain and Application contain no business implementation yet. There are no migrations or seed commands to run in M0. Authentication, shipment rules, business API endpoints, dashboard features, and GitHub Actions remain in their later milestones. PostgreSQL connectivity is verified; business-data persistence awaits M1. The next milestone is M1, subject to a separate instruction.
