# M7 frontend CI stabilization

Reviewed 2026-11-09 against commit `4827fc8afa3c24296f8cc421e70562d045715661` and [hosted quality run 42648373939](https://github.com/TobyTheRoyal/fleetflow/actions/runs/42648373939).

## Failure and root cause

The Frontend job failed during Vitest in `demo.test.tsx`, at the assertion expecting the Demo Dispatcher workspace after successful entry. The other 104 tests passed. Backend and all three hosted E2E jobs succeeded.

The positive login test's API mock returned a valid session from POST `/api/v1/demo/sessions`, but continued returning 401 from GET `/api/v1/auth/me` afterward. Signing in clears the old TanStack Query cache. A subsequent session query could therefore receive the mock's contradictory 401 and correctly expire the newly authenticated demo. Whether the workspace assertion observed the brief signed-in state depended on scheduling. The same failure was reproduced locally before editing.

## Fix

The mock now tracks authentication: anonymous `/auth/me` returns 401, successful entry changes its state, and subsequent `/auth/me` returns the authenticated session. Unexpected requests fail explicitly. Each test refreshes its fictional lease deadline.

The original one-explicit-entry/no-AI assertions remain. The success case additionally forces a session-query refetch, verifies that it happened, and asserts that the workspace remains visible without a false expiration message. The separate real-401 expiration test remains unchanged. No application authentication code, security policy, timeout or test exclusion was changed.

## Actual verification

- Before fix: `npm run check` reproduced **104 passed / 1 failed** locally, matching hosted CI.
- Targeted demo file: **8/8**, repeated **20 times** (**160 test executions**, no failures).
- macOS / Node 24.22.2: complete `npm run check` passed: ESLint, Prettier, TypeScript, **105 Vitest tests in 21 files**, **2 Node infrastructure tests**, production build.
- Fresh local Linux / `node:24.22.2-bookworm-slim`: `npm ci --no-audit`, complete `npm run check` and `npm audit --audit-level=moderate` passed with the same test counts; **0 vulnerabilities**. This is a local Linux reproduction, not GitHub-hosted execution.
- Native npm audit also reported **0 vulnerabilities**. Its initial sandboxed attempt could not resolve the registry; the network-enabled retry succeeded.

The first ad hoc Linux container used a non-executable temporary directory, preventing the existing infrastructure tests from launching their temporary fake-Docker executable. The test harness was rerun with executable temporary storage, as available on hosted runners. Application container security settings were unchanged.

Only the frontend test fixture and this verification documentation changed. Backend/browser suites were not rerun locally for this test-only correction; their success above refers to the inspected hosted baseline. No real inference, cloud provisioning, deployment, commit or push was performed. Hosted confirmation of the corrected test remains pending an owner-reviewed commit/push.
