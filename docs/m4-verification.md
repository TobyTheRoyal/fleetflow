# M4 verification — 2026-11-07

Historical M4 results are preserved below. The subsequent investigation and current browser/CI status are recorded in [M4.1 verification](m41-verification.md).

This report records executed checks, not intended CI results. Scope is M4 only. M0–M3 endpoints, DTOs, shipment rules and persistence architecture are preserved. No authentication, deployment, AI, SignalR or later milestone is implemented. No commit or push was performed.

## Environment

Local host: macOS / Apple Silicon; .NET SDK 10.0.403, Node 24.22.2, Docker Desktop Linux containers, PostgreSQL 18.6. Browser dependencies are pinned by the frontend lockfile. The normal application remains loopback-only; test projects use separate ports, databases and volumes.

## Build, test and security results

| Check actually executed | Result |
| --- | --- |
| Locked .NET restore | Passed |
| Complete .NET Release build | Passed; zero warnings and errors |
| `dotnet format ... --verify-no-changes --no-restore` | Passed |
| Complete xUnit/Microsoft.Testing.Platform suite | **176 passed, 0 failed, 0 skipped**: 73 unit and 103 real-PostgreSQL integration cases |
| EF migration/model agreement | `has-pending-model-changes` passed; no schema change introduced |
| Actual PostgreSQL migration, constraints, rollback/reapply and seed | Passed in integration tests and fresh E2E initialization |
| Clean `npm ci` | Passed with the pinned host Node runtime |
| ESLint, Prettier, TypeScript | Passed |
| Vitest / React Testing Library | **57 passed** in 14 files |
| Vite production build | Passed |
| Final Chromium E2E/accessibility suite | **17 passed**, including generated API contract agreement |
| Repeat-isolation check | **4 passed**: shipment lifecycle and master-data journeys, each repeated twice with distinct identities |
| API runtime Docker build | Passed; hardened read-only/non-root configuration exercised by real HTTP writes |
| NuGet direct/transitive audit | Zero known package/advisory findings |
| npm audit | Zero known vulnerabilities |
| Public-file privacy guard | Zero findings |
| Reset-script safety tests | 2 passed using fake Docker; development reset was not executed |
| Git diff whitespace check | Passed |
| GitHub Actions configuration | actionlint 1.7.12 passed |
| GitHub-hosted jobs / branch protection | **Not executed or configured locally** |

Dependency audit results are time-specific. These checks do not include a container OS-package vulnerability scan, penetration test or production deployment assessment.

The most important backend regression reproduces EF/Npgsql-wrapped PostgreSQL deadlock and serialization errors through HTTP. Both previously returned 500; both now return the existing safe `409/concurrent_operation`. Assertions verify rollback of state/version/history and exactly one status event on a subsequent explicit successful retry. Existing concurrent assignment, stale Version, event immutability, query-count and domain tests remain green.

## Coverage evidence

| Suite / scope | Lines | Branches |
| --- | --- | --- |
| PostgreSQL integration suite, application assemblies | 98.18% | 87.85% |
| Unit suite, Domain assembly | 92.69% | 85.07% |
| Frontend component suite | 80.25% | 77.21% |

The raw unit report includes unexercised Application/Infrastructure assemblies referenced by architecture tests: its overall line rate is 29.20%, not the Domain rate. The integration report separately records Domain 96.06%, API 99.23%, Infrastructure 97.97% and Application 100% line coverage. These reports are not averaged. Exclusions and interpretation are described in [testing.md](testing.md); there is no arbitrary coverage threshold.

Ignored raw reports are generated under `artifacts/backend/` and `frontend/coverage/`. `python3 scripts/coverage-summary.py` reproduces the summary. The workflow uploads scoped coverage/TRX/browser diagnostics rather than repository archives or environment files.

## Browser verification and limitations

The final Chromium baseline passed all 17 tests: the real database connection, dashboard/list/filter/paging behavior, shipment creation/editing/assignment/full lifecycle/history, stale-version recovery, reservation conflicts, master-data management, ten accessibility cases and responsive navigation/page recovery. A separate `--repeat-each=2` run passed all four executions of the lifecycle and master-data journeys, confirming that their generated identities do not collide between repetitions.

Cross-browser exploration was also performed:

- A Linux ARM64 run against the actual isolated Docker backend/database passed **50 of 51** tests across Chromium, Firefox and WebKit. The remaining Firefox shipment-detail accessibility case crashed the page during Axe evaluation. All functional journeys passed in that run. The exploratory official Playwright image bundled Node 26, outside the project's Node 24 requirement, so this is additional evidence rather than a supported clean-toolchain certification.
- Native macOS Firefox accessibility evaluation also exhibited page crashes. Native WebKit developed pointer-interception failures despite correct DOM hit testing; Chromium did not exhibit them. These results do not establish a fully green native cross-browser matrix.
- A later Linux amd64 exploration exhausted the Docker VM disk and caused test PostgreSQL failures. That run is invalid as application-verification evidence. Only test-owned containers/images and identified build-cache records were removed; the development database volume was preserved. Development PostgreSQL and API readiness were confirmed healthy after recovery.
- The optional browser-container helper was removed rather than adding unverified infrastructure. The committed Playwright configuration and Linux CI matrix retain all three browser projects and the failing audit; no WCAG rule or browser test was disabled to claim success. Hosted Ubuntu execution remains unverified.

Axe checks WCAG 2/2.1 A/AA with contrast enabled and reports incomplete findings for review. Direct in-page analysis is used with an explicit no-iframe assertion; cross-origin frame analysis is outside that mode. Tests also check focus containment/restoration and responsive widths of 1440, 820, 390 and 320 pixels. This is not screen-reader, real-device or formal WCAG conformance certification.

## Actual screenshots and operational checks

Five real seeded-application screenshots were captured and visually inspected: dashboard, shipments, shipment detail/timeline, vehicles and mobile dashboard. They contain fictional data from a freshly migrated disposable database; no developer records were captured. See [screenshots.md](screenshots.md).

The development API image was rebuilt and restarted with the existing data volume preserved. Readiness returned `Healthy`. Screenshot and browser stacks use `fleetflow-e2e`, not `fleetflow`, and normal teardown removes their own volumes. The user-owned Vite server was not stopped. All 31 local Markdown/README image and file targets exist. Startup configuration and cleanup targeting are checked locally; native Windows and GitHub-hosted runner behavior are not certified.

## Remaining release gates

Confirm all hosted CI jobs, especially Firefox/WebKit, on a clean Ubuntu runner before treating the repository as fully cross-browser verified. Review any Axe incomplete findings and perform screen-reader/real-device checks before a conformance claim. Add production identity/authorization, deployment controls, least-privilege database roles and operational recovery only in an explicitly approved later milestone; [security.md](security.md) lists the requirements. FleetFlow remains an unauthenticated local portfolio application.
