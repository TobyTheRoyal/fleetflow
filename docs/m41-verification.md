# M4.1 — CI and cross-browser stabilization

Verified locally on 2026-11-07. This follows the historical [M4 report](m4-verification.md). Scope is browser reliability, test infrastructure and CI readiness. Business rules, HTTP contracts and backend implementation are unchanged. M5 is not started. No commit, push, repository-setting change or deployment was performed.

## Firefox: confirmed toolchain regression

The original FleetFlow shipment-detail audit reproduced on Playwright 1.65.0 with a real Firefox content-process `SIGSEGV`/signal 11, not a failed accessibility assertion. The macOS crash report records a null-address access in XUL. The existing Linux trace ends during Axe evaluation; it does not by itself contain a symbolized native stack.

A controlled minimal page, without React, Vite, Docker or PostgreSQL, reproduced the crash in both operation orders: named role lookup then a main-world ARIA-reference read, and the reverse. Both crashed with 1.65.0; both passed with 1.64.1. This matches [Playwright issue 43967](https://github.com/microsoft/playwright/issues/43967), where cross-world ARIA reflection exposes a Firefox defect. The upstream report also links [Mozilla bug 2090055](https://bugzilla.mozilla.org/show_bug.cgi?id=2090055); that linked tracker was not independently accessible during this review. FleetFlow's timeline uses a valid `aria-labelledby` reference; removing that semantic markup would hide the trigger rather than resolve the toolchain problem.

**Fix:** pin the last working stable Playwright release, **1.64.1**, with matching deduplicated `playwright`/`playwright-core` in the lockfile. Use its matching bundled browsers. The normal Axe analysis mode now works; the previous `setLegacyMode()` workaround and its frame restriction are removed. No rules, tags or browser projects are disabled. A committed regression case combines a role locator, ARIA-referenced label, full Axe scan and subsequent click.

This is a temporary compatibility pin, not a claim to have patched Firefox internals. A future upgrade must pass that regression, the entire matrix and repeated cases with matching bundled browsers. Do not install a canary, mix driver/browser versions, patch `node_modules`, change production ARIA markup or remove accessibility coverage to bypass the defect. Reviewed dependency PRs remain enabled.

## WebKit: verified contrast defect; historical pointer cause unresolved

The historical macOS `html intercepts pointer events` failure could not be reproduced in the initial unchanged targeted dialog test or the functional journeys of the complete 1.65.0 baseline. Review found no persistent covering overlay or incorrect portal stacking: the backdrop/content have separate z-indices, explicit trigger references restore focus, and closing a dialog releases background interaction. The historical trace/DOM observations do not establish a unique native-browser or application cause; it would be inaccurate to claim one.

The baseline did reproduce a separate real defect: white submit-button text on `hover:bg-primary/90` produced **4.31:1** contrast on the white dialog canvas, below AA's 4.5:1 requirement. Replace the translucent hover color with the existing palette's opaque darker `blue-700`. The validation-dialog audit now explicitly hovers the enabled submit action before scanning.

A new regression exercises close-button clicks, focus restoration, immediate reopening, Escape dismissal, released body pointer interaction and subsequent sidebar navigation across customers, drivers and vehicles. It uses role/name locators and auto-retrying assertions. No force clicks, dispatched synthetic clicks, CSS suppression or fixed sleeps were added. The existing assignment/status and mobile focus tests remain enabled.

## Isolation and diagnostic fixes

- The default `npm run test:e2e` now selects **all three** browser projects. `test:e2e:chromium` is an explicitly named iteration shortcut; `test:e2e:all` remains an alias for the complete matrix.
- Preserve one worker, fresh contexts, zero retries and browser/test/repeat-owned mutation identities. No test assumes another test has created its resources.
- Docker Compose shell exports previously took precedence over `--env-file`. The helper now explicitly supplies the isolated generated configuration to every Compose child. The full suite passed with deliberately conflicting inherited `API_PORT=5080`, `POSTGRES_PORT=5433` and a different database name; tests still used 5180/5434 and `fleetflow_e2e`.
- Compose commands have an execution deadline. Readiness still uses database health plus bounded API readiness polling; migrations and seed remain explicit initialization commands.
- CI diagnostics record the setup stage, container status and bounded API/PostgreSQL logs, redacting the generated credential before saving. Diagnostics are attempted on setup failure before cleanup and on normal teardown. Existing projects are neither logged nor removed.
- Two fake-Docker infrastructure tests verify environment precedence, redaction, exact cleanup target, temporary-file removal and refusal to touch another test project. They require no Docker daemon and run through `npm run test:infra`, included in `npm run check`.
- CI records the resolved Node/Playwright toolchain and `pw:browser` launch/stderr/exit logs. Bash pipefail preserves the failure status through `tee`. Existing HTML/JUnit, failure traces and screenshots remain uploaded with narrow paths and seven-day retention.

## Actual local results

Environment: macOS 26.2, Apple Silicon, Node 24.22.2, .NET SDK 10.0.403, Docker Desktop and PostgreSQL 18.6. The tests use real ASP.NET Core/PostgreSQL through an isolated Docker Compose stack, not mocked API responses.

| Browser | Matching bundle | Complete suite | Stability run |
| --- | --- | --- | --- |
| Chromium | 156.0.8078.4 / revision 1248 | **19/19 passed** | **20/20 passed** |
| Firefox | 157.0 / revision 1555 | **19/19 passed** | **20/20 passed** |
| WebKit | 27.2 / revision 2370 on this host | **19/19 passed** | **20/20 passed** |

The complete suite passed **57/57** with zero failed/skipped tests and zero retries. The stability run passed **60/60**: shipment detail, ARIA-reference regression, hovered validation dialog and pointer/focus recovery, each repeated five times per browser. A clean `npm ci` and matching browser installation preceded stability verification. These results support the recorded conditions, not a guarantee that no future flake can occur.

| Regression check | Actual result |
| --- | --- |
| Locked .NET restore and complete Release build | Passed; zero build warnings/errors |
| .NET formatting | Passed |
| xUnit with actual PostgreSQL | **176 passed**, 0 failed/skipped: 73 unit and 103 integration |
| EF model/migration consistency | Passed; no pending model changes |
| Frontend lint, format, TypeScript and production build | Passed |
| Vitest/RTL | **57 passed** in 14 files |
| Node infrastructure tests | **2 passed** |
| Reset-helper Python tests | **2 passed** |
| OpenAPI/generated-type agreement | Passed during full E2E initialization |
| Docker API build, PostgreSQL readiness, migration and seed | Passed through real isolated Compose initialization |
| npm and direct/transitive NuGet audits | Zero known findings at verification time |
| Workflow validation | actionlint 1.7.12 passed |
| Public-file and whitespace checks | Passed |
| GitHub-hosted execution | **Not run** |

Ignored local evidence is under `artifacts/m41/full-matrix/` and `artifacts/m41/stability/`, including HTML/JUnit, accessibility attachments, stack logs and browser-run logs. Minimal before/after browser logs and the baseline diagnostics are also retained under `artifacts/m41/`. Coverage remains under `artifacts/backend/` and `frontend/coverage/`; it is not combined into a misleading single percentage. Synthetic traces/logs are not a policy for future authenticated or real-customer data.

## Reproduce

From the repository root, with Docker running:

```sh
nvm use
dotnet tool restore
dotnet restore backend/FleetFlow.slnx --locked-mode
dotnet build backend/FleetFlow.slnx -c Release --no-restore
dotnet format backend/FleetFlow.slnx --verify-no-changes --no-restore
sh scripts/test-backend.sh
python3 -m unittest discover -s scripts/tests

cd frontend
npm ci
npx playwright install --with-deps chromium firefox webkit
npm run check
CHECK_API_CONTRACT=1 npm run test:e2e
npm run test:e2e -- --repeat-each=5 --grep 'shipment detail|ARIA references|validation dialog|dialogs release'
```

Run one E2E invocation at a time and keep 5174/5180/5434 free. Linux browser system dependencies may require sudo; the hosted runner installation step supplies them. No browser-image download or Docker disk cleanup is required by this workflow. Keep sufficient Docker VM space for the API/PostgreSQL images and test databases. Development volumes are not reset by these tests.

## GitHub-hosted readiness and remaining limits

The workflow targets Ubuntu 24.04 with maintained first-party setup actions, the pinned SDK/Node versions, lockfile installs, per-runner Docker isolation and `playwright install --with-deps` for each matrix browser. Pull requests, pushes to main and manual dispatch are configured. Token permissions remain `contents: read`, checkout credential persistence is disabled, no deployment/secrets are required, and diagnostic uploads exclude environment files/private instructions. The [CI guide](ci.md#m41-hosted-verification-procedure) gives the exact manual verification steps after the user reviews, commits and pushes the change.

**No GitHub-hosted run URL or successful hosted result exists for this change.** Linux runner libraries, token/cache/artifact services and runner network behavior remain to be confirmed by that run. Require both quality jobs and all three browser jobs; record the commit/run URL and review artifacts before marking hosted CI verified.

Results cover the listed bundled engines, not every installed stable browser version. The historical WebKit pointer root cause remains unresolved, although the current complete and repeated interaction tests pass. Automated Axe/viewport checks are not screen-reader, real iOS/Safari-device or formal WCAG certification. Browser stderr includes some non-fatal headless/Juggler shutdown messages; assertions are not relaxed to ignore failures. Authentication remains absent and FleetFlow must remain local. Stop at M4.1.
