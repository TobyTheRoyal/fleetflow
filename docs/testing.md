# Testing and coverage

Use the pinned .NET SDK and Node versions; Docker must be running. No test should depend on or reset the development database. Coverage is diagnostic evidence, not a target percentage.

## Backend

```sh
dotnet tool restore
dotnet restore backend/FleetFlow.slnx --locked-mode
dotnet build backend/FleetFlow.slnx -c Release --no-restore
dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore
sh scripts/test-backend.sh
python3 scripts/audit-dotnet.py
```

xUnit v3 uses **Microsoft.Testing.Platform** from `global.json`. `coverlet.collector`, VSTest `--collect` and legacy `.runsettings` commands are not used. The pinned `coverlet.MTP` extension instruments managed code portably; [official MTP usage](https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/Coverlet.MTP.Integration.md) documents its options.

The helper runs the complete Release solution with Cobertura and xUnit TRX reports, using `--results-directory-layout per-module` so concurrent test projects cannot overwrite reports. Files are under `artifacts/backend/<project>/<framework_runtime>/`. macOS automatically uses the active Docker context. Testcontainers creates PostgreSQL 18.6 containers; individual persistence/HTTP tests create fresh databases and clean them up.

Coverage includes application assemblies and excludes EF migration/snapshot files from line metrics; actual migration constraints, rollback/reapply, seed and model agreement remain database tests. The unit suite focuses on Domain; its raw report also shows unexercised Application/Infrastructure assemblies referenced by architecture tests. Do not misrepresent that raw overall rate as domain coverage, or average it with integration coverage. `python3 scripts/coverage-summary.py` prints separate suites and assemblies. There is no arbitrary coverage gate.

Domain cases cover the transition matrix, eligibility/capacity, scheduling, country/address/weight invariants, status locks, UTC and versions. Real PostgreSQL cases cover transactions, deliberate event-insert failure rollback, optimistic concurrency, concurrent driver/vehicle reservations, fleet-update races, constraints, literal SQL search/pagination and snapshot dashboard definitions. Security cases cover safe errors/logs, host filtering, absent cross-origin grants, JSON content types, non-cacheable responses and scoped DI.

Model-only EF verification requires no real credential or open database connection:

```sh
ConnectionStrings__FleetFlow='Host=127.0.0.1;Database=fleetflow_model_check' ASPNETCORE_ENVIRONMENT=Development \
  dotnet ef migrations has-pending-model-changes --project backend/src/FleetFlow.Infrastructure --startup-project backend/src/FleetFlow.Api --configuration Release --no-build
```

## Frontend

```sh
npm ci --prefix frontend
npm run check --prefix frontend
npm audit --prefix frontend --audit-level=moderate
```

`check` executes lint, formatting, TypeScript (including E2E source), Vitest/V8 coverage and a production build. `npm run test --prefix frontend` supports interactive test development. Reports are `frontend/coverage/index.html`, `lcov.info` and `coverage-summary.json`.

All application TypeScript/TSX is included, including unvisited components; test fixtures/tests, generated OpenAPI output and the DOM bootstrap are excluded. Component tests intentionally mock HTTP. They cover actual DTO parsing/errors, cancellation and ambiguous writes, form validation/server errors, permitted actions, stale-version locks including refresh failure, bounded timeline paging, master-data conflicts, query retries/invalidation, layout navigation and error-boundary recovery.

To check the generated contract against a running API without modifying the repository:

```sh
cd frontend
npm run check:api
# Only after an intentional backend contract change:
npm run generate:api
```

`OPENAPI_URL` overrides the local 5080 default. The generator is pinned and isolated from the application's TypeScript 6 because its own CLI currently uses TypeScript 5.

## Browser tests

```sh
cd frontend
npx playwright install --with-deps chromium firefox webkit
CHECK_API_CONTRACT=1 npm run test:e2e
# All three browser engines:
CHECK_API_CONTRACT=1 npm run test:e2e:all
```

The default `test:e2e` script runs Chromium; CI explicitly selects each browser from the matrix on Linux runners. Keep adequate free space in the Docker VM as well as on the host before running integration tests (at least 8 GB is recommended). Host free space does not imply that the Docker VM has space.

On Linux, `--with-deps` installs the browser system libraries and may require sudo; macOS uses the matching native browser binaries. Browser engines are Chromium, Firefox and WebKit, not a certification of every installed Chrome/Safari version or real iOS device.

Global setup builds the actual API image, starts a separate `fleetflow-e2e` Compose project, applies migration/seed, waits for readiness and optionally checks OpenAPI drift. Vite/API/PostgreSQL use loopback **5174/5180/5434**. Random database credentials live in a mode-0600 file in a private temporary directory; they are never arguments, committed files or artifacts. Normal completion/failure removes the stack and its own volume. A second running test project is refused. Run one invocation at a time.

Each mutation test creates its own deterministic resource identities keyed by test, browser and repeat index. Tests do not rely on a prior journey having run. One worker per isolated database keeps dashboard snapshot comparisons deterministic; GitHub's browser jobs each have their own runner and database. A useful repeat-isolation check is:

```sh
npm run test:e2e -- --repeat-each=2 --grep 'create, edit|master-data'
```

A hard interruption can leave a disposable test stack. From `frontend/`, use `node tests/e2e/infra/cleanup.mjs --confirm`; this command names only `fleetflow-e2e`, never `fleetflow`, and does not need the original database password. Temporary directories from hard-killed processes may remain until normal OS cleanup. CI has an always-run fallback cleanup; a forcibly killed runner is disposed by GitHub.

Playwright writes an HTML report, JUnit XML, failure screenshots and failure traces. API diagnostics in CI redact the generated database password before writing a scoped log. Uploaded artifacts contain synthetic test data only; do not use this configuration for real customer data or future authenticated-session traces without reviewing redaction.

### Accessibility scope

Axe checks WCAG 2 A/AA and 2.1 A/AA rules on the actual pages, shipment details and representative validation/assignment/status dialogs, with no disabled contrast rule or blanket exclusions. Keyboard tests verify dialog focus containment/restoration, navigation and page recovery. Responsive checks use 1440, 820, 390 and 320 pixel widths and contain wide tables within their own scroll regions.

The Axe run uses its documented direct in-page execution mode because native blank-page result aggregation crashed Firefox on this macOS host. No WCAG rules are disabled; an explicit no-iframe assertion protects the scope. Cross-origin frame auditing is not provided by that mode and must be revisited if embedded frames are added. The native macOS WebKit driver also showed pointer interception failures despite correct DOM hit testing; the configured Linux CI matrix remains to be verified on hosted runners. A separate exploratory Linux ARM64 run passed 50 of 51 tests, with one Firefox accessibility page crash. See the M4 verification report for the exact limitations.

Automated checks cannot prove full accessibility. Review axe `incomplete` findings, zoom, screen-reader behavior, high contrast modes, touch target sizes and real-device behavior manually before making a conformance claim. No formal WCAG certification is claimed.

## Diagnostic commands

```sh
python3 scripts/coverage-summary.py
python3 scripts/check-public-files.py
git diff --check
```

The public-file check rejects private instruction/configuration filenames and occurrences of the current local password in public Git candidates. It is a focused privacy safeguard, not a complete secret-scanning engine. Historical verification reports remain milestone-specific; use [M4 results](m4-verification.md) for the current checks.
