# Continuous integration design

`.github/workflows/ci.yml` runs on every pull request, push to `main` and explicit workflow dispatch. There is no deploy/publish step, registry login or deployment secret. Hosted CI execution remains unverified until these changes are pushed and a GitHub-hosted run completes.

## Jobs

| Job | Checks and isolation |
| --- | --- |
| Backend / Release and PostgreSQL | Public-file privacy guard; pinned tool/locked restore; Release build; formatting; model/migration consistency; xUnit/Testcontainers against real PostgreSQL; Coverlet/TRX; NuGet audit |
| Frontend / Types, tests and build | `npm ci`; ESLint/Prettier/TypeScript; Vitest/V8; production build; npm audit |
| E2E / chromium, firefox, webkit | Each Ubuntu 24.04 runner owns a fresh Docker Compose stack, applies migration/seed, waits for readiness, checks generated API drift and runs real browser/accessibility journeys |

Integration fixtures create and start PostgreSQL with Testcontainers; a separate shared GitHub service database would duplicate infrastructure and weaken per-test isolation. E2E uses the same Dockerfile and Compose configuration as local development, with isolated project/ports/volume. No external test service is required.

## Permissions, actions and caching

Workflow-level permission is `contents: read`. Checkout uses `persist-credentials: false`; no `pull_request_target`, untrusted-expression shell interpolation or repository write permissions are introduced. Only trusted browser matrix values enter commands. First-party actions are pinned to maintained major releases: checkout v7, setup-node v7, setup-dotnet v6 and upload-artifact v7. Official source references: [checkout](https://github.com/actions/checkout), [setup-node](https://github.com/actions/setup-node), [setup-dotnet](https://github.com/actions/setup-dotnet), [upload-artifact](https://github.com/actions/upload-artifact).

Node and .NET package caches are keyed from committed lockfiles. Build outputs and `node_modules` are not cached. Installs still use `npm ci` and `--locked-mode`; cached artifacts cannot replace the lockfile checks. Workflow concurrency cancels superseded runs; tests have explicit timeouts and CI has a fallback cleanup for the disposable E2E project.

Artifacts are narrowly scoped to backend XML/TRX, frontend coverage and Playwright reports/test results, retained for seven days. No repository-wide archive, `.env`, Docker inspect/config dump, database volume or private instruction folder is uploaded. The E2E API log replaces generated credentials before saving. Future authentication will require a fresh review of traces and logs.

The configured major-version pins follow the requested workflow policy; immutable SHA pinning would further reduce supply-chain mutability and can be considered with a managed update policy. Dependabot proposes weekly action/npm/NuGet/Dockerfile updates in reviewed PRs; it never auto-merges them.

## Local validation and hosted limitations

The workflow is checked with [actionlint](https://github.com/rhysd/actionlint), including YAML structure, expressions, action inputs and workflow dependencies. Local check commands and the actual Docker/browser stack are executed independently. This does not reproduce GitHub token handling, cache/artifact service behavior, Ubuntu runner images, protected branches or network policy.

After pushing, inspect all job results, confirm uploaded diagnostics contain no credentials, then configure branch protection/required checks manually in GitHub. No branch protection, repository setting or hosted workflow run is claimed or changed by local implementation.

NuGet audit fails if its JSON report contains any known direct/transitive advisory; npm audit gates moderate-and-higher findings. Package feed/network failures remain failures rather than silently skipping security checks. Dependency feeds change over time. Container OS-package scanning and deployment/runtime security assessment are separate remaining tasks; local npm/NuGet audit is not an image penetration test.
