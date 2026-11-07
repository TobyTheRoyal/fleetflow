# Continuous integration design

`.github/workflows/ci.yml` runs on every pull request, push to `main` and explicit workflow dispatch. There is no deploy/publish step, registry login or deployment secret. Hosted CI execution remains unverified until these changes are pushed and a GitHub-hosted run completes.

## Jobs

| Job | Checks and isolation |
| --- | --- |
| Backend / Release and PostgreSQL | Public-file privacy guard; pinned tool/locked restore; Release build; formatting; model/migration consistency; xUnit/Testcontainers against real PostgreSQL; Coverlet/TRX; NuGet audit |
| Frontend / Types, tests and build | `npm ci`; ESLint/Prettier/TypeScript; Vitest/V8; production build; npm audit |
| E2E / chromium, firefox, webkit | Each Ubuntu 24.04 runner owns a fresh Docker Compose stack, applies migration/seed, waits for readiness, checks generated API drift and runs real browser/accessibility journeys |

The [official Ubuntu 24.04 runner manifest](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md) includes Docker Engine/Compose and Python. Setup actions explicitly select the project Node/.NET versions; the preinstalled Node version is not relied on. Playwright installs its own matching bundles rather than using the runner's system Firefox/Chrome.

Integration fixtures create and start PostgreSQL with Testcontainers; a separate shared GitHub service database would duplicate infrastructure and weaken per-test isolation. E2E uses the same Dockerfile and Compose configuration as local development, with isolated project/ports/volume. No external test service is required.

## Permissions, actions and caching

Workflow-level permission is `contents: read`. Checkout uses `persist-credentials: false`; no `pull_request_target`, untrusted-expression shell interpolation or repository write permissions are introduced. Only trusted browser matrix values enter commands. First-party actions are pinned to maintained major releases: checkout v7, setup-node v7, setup-dotnet v6 and upload-artifact v7. Official source references: [checkout](https://github.com/actions/checkout), [setup-node](https://github.com/actions/setup-node), [setup-dotnet](https://github.com/actions/setup-dotnet), [upload-artifact](https://github.com/actions/upload-artifact).

Node and .NET package caches are keyed from committed lockfiles. Build outputs and `node_modules` are not cached. Installs still use `npm ci` and `--locked-mode`; cached artifacts cannot replace the lockfile checks. Workflow concurrency cancels superseded runs; tests have explicit timeouts and CI has a fallback cleanup for the disposable E2E project.

Artifacts are narrowly scoped to backend XML/TRX, frontend coverage and Playwright reports/test results plus browser-run logs under `artifacts/e2e/`, retained for seven days. No repository-wide archive, `.env`, Docker inspect/config dump, database volume or private instruction folder is uploaded. The E2E stack log records the setup stage and replaces generated credentials before saving API/PostgreSQL diagnostics, including on setup failures. Browser `pw:browser` logs record launches, stderr and process exits; no protocol/body debug logging is enabled. The test command is piped through `tee` with Bash pipefail, so a failure still fails the job. Future authentication will require a fresh review of traces and logs.

The configured major-version pins follow the requested workflow policy; immutable SHA pinning would further reduce supply-chain mutability and can be considered with a managed update policy. Dependabot proposes weekly action/npm/NuGet/Dockerfile updates in reviewed PRs; it never auto-merges them.

## Local validation and hosted limitations

The workflow is checked with [actionlint](https://github.com/rhysd/actionlint), including YAML structure, expressions, action inputs and workflow dependencies. Local check commands and the actual Docker/browser stack are executed independently. This does not reproduce GitHub token handling, cache/artifact service behavior, Ubuntu runner images, protected branches or network policy.

After pushing, inspect all job results, confirm uploaded diagnostics contain no credentials, then configure branch protection/required checks manually in GitHub. No branch protection, repository setting or hosted workflow run is claimed or changed by local implementation.

NuGet audit fails if its JSON report contains any known direct/transitive advisory; npm audit gates moderate-and-higher findings. Package feed/network failures remain failures rather than silently skipping security checks. Dependency feeds change over time. Container OS-package scanning and deployment/runtime security assessment are separate remaining tasks; local npm/NuGet audit is not an image penetration test.

## M4.1 hosted verification procedure

See [the stabilization report](m41-verification.md) for the exact local results and version-pin rationale. Local success is not evidence of a hosted GitHub run.

1. Review the public diff and run `python3 scripts/check-public-files.py`. Commit only reviewed public source/configuration/documentation files; keep environment files and private instructions excluded. No commit or push is performed by the implementation agent.
2. Push the reviewed change to a branch and open a pull request targeting `main`. The `pull_request` trigger starts **FleetFlow quality**. Alternatively, after pushing a reviewed branch, select that branch in Actions → FleetFlow quality → Run workflow. No deployment is configured.
3. Confirm the run belongs to the intended commit. Require Backend, Frontend and all three E2E jobs (chromium, firefox, webkit) to pass; do not accept a missing/cancelled browser job as success.
4. Confirm the toolchain step resolves Playwright, its runner and core to 1.64.1; Node comes from `.nvmrc`, .NET from `global.json`. Each browser job installs its own bundled engine and Linux libraries, then initializes its own Compose PostgreSQL/API/database/volume.
5. Inspect JUnit counts and the HTML reports, generated-contract check, stack/browser logs, screenshots and traces for any failures. Confirm artifacts contain only synthetic test data and no credentials. Record the run URL and commit in a follow-up verification entry.
6. Only after a fully green hosted run, configure required checks/branch protection using appropriate repository permissions. This implementation does not change repository settings.
