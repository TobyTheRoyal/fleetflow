# Neon Free branch verification

Use one temporary child branch to test managed PostgreSQL permissions before initializing production. This procedure requires explicit approval for branch creation and write tests on that branch. Deleting the branch or changing its expiry requires a separate approval.

## Read-only preflight

1. Record the reviewed commit and require all five GitHub quality jobs for that exact SHA. Use the local Production Docker rehearsal for the full regression suite. Do not point Testcontainers, Playwright global setup, the development Compose stack or the GitHub maintenance workflow at Neon for this verification.
2. In the existing organization, verify Free and the number of branches in the existing project. Free includes ten branches per project. A branch shares the project's compute, storage and transfer limits. Check actual remaining usage in Neon before proceeding. Stop if the operation would require an upgrade, paid feature or another project. [Neon Free allowances](https://neon.com/docs/introduction/plans).
3. Record the production branch ID and its direct read-write endpoint ID and hostname from Neon. Verify the parent name is `production`, the database is `neondb`, and the endpoint belongs to that parent. Save a sanitized baseline using [inspect-postgres.sql](../deployment/inspect-postgres.sql) over VerifyFull TLS with required channel binding. Confirm the database owner is `neondb_owner`, the application schema is empty and no FleetFlow migrations or roles exist. Stop if the baseline differs, to avoid cloning unexpected data.

## Create and pin the test target after approval

Use the authenticated Neon CLI for the approved write action. Fill the variables from the preflight record. This command is a future manual step:

```sh
neon branches create --project-id "$NEON_PROJECT_ID" \
  --parent "$PRODUCTION_BRANCH_ID" --name m8-neon-validation \
  --type read_write --no-secrets --no-analytics --output json
```

Use one branch and its single read-write compute. Do not set an automatic expiry. If the create operation times out or returns an ambiguous result, list branches and reconcile by name and parent before any retry. If the name already exists unexpectedly, stop. Never change the default branch or run `neon checkout`, `neon link` or `neon env pull` into the application workspace.

1. Read back the actual test branch ID and its parent, and list the test endpoint with explicit scoping:

   ```sh
   neon branches get "$TEST_BRANCH_ID" --project-id "$NEON_PROJECT_ID" --no-analytics --output json
   neon postgres endpoints list --project-id "$NEON_PROJECT_ID" --branch "$TEST_BRANCH_ID" --no-analytics --output json
   ```

2. Require `parent_id == PRODUCTION_BRANCH_ID`, a distinct branch ID, and a distinct direct read-write endpoint ID and hostname. Require `endpoint.branch_id == TEST_BRANCH_ID`. Abort if any mapping cannot be proven or the production branch is no longer the default. Use the recorded test hostname for the independent initializer confirmation. Matching only the database name is insufficient because both branches have `neondb`.
3. In an external private directory, fetch the **explicit test branch** owner URI directly into a mode-0600 file. Disable shell tracing and keep it out of command arguments, terminal output and artifacts:

   ```sh
   umask 077
   neon connection-string "$TEST_BRANCH_ID" --project-id "$NEON_PROJECT_ID" \
     --database-name neondb --role-name neondb_owner --endpoint-type read_write \
     --no-pooled --ssl verify-full --no-analytics > "$PRIVATE_TEST_DIR/owner.uri"
   ```

   Parse the URI privately and compare its exact hostname to the test endpoint metadata before using it. A child of an unprotected parent can inherit its role passwords. Treat the test owner URI as sensitive as the production URI. Keep it out of the web container. Never rotate a parent credential as part of this rehearsal. [Neon branch isolation and role passwords](https://neon.com/docs/manage/branches).
4. Build separate private initialization and runtime env files using the [placeholder templates](neon-initialization.md), the confirmed test host and distinct random test runtime/maintenance passwords. Use a dedicated rehearsal Data Protection PFX with its own export password. Set `AI_ENABLED=false` and leave provider credentials absent. Verify a separate test-branch catalog session over `VerifyFull` with required channel binding shows the same empty baseline.

## Test only against the verified child endpoint

1. Build and record the image for the exact reviewed commit. With the private test initialization env and certificate directory, run `sh scripts/initialize-portfolio.sh --confirm-database neondb --confirm-host "$TEST_DIRECT_HOST"`. It runs the four EF migrations, provisions fictional demo data and performs SQL role creation. Confirm all four IDs, catalog revision, configured Viewer/Dispatcher slots, database object owners and the trigger on each logistics history table.
2. Open separate sessions using the test runtime and maintenance logins. For nonmutating checks use `pg_has_role`, `has_schema_privilege`, `has_table_privilege`, `has_column_privilege` and `has_function_privilege`. Verify each login inherits only its own group and has no ownership or elevated role attributes. Runtime must read the Viewer schema, write in Dispatcher schemas, insert history and key rows, and have no write access to Viewer data, template, Identity role memberships or migration catalog. Maintenance must have no direct data access and may execute only the fenced reset among FleetFlow routines. Compare to the [grant matrix](neon-initialization.md#roles-and-grants).
3. With the test endpoint confirmed again, test denied operations inside transactions that always roll back. Use a savepoint or a fresh transaction for each expected error. Expect `42501` for runtime DDL, history truncation, disabling the history trigger or calling reset. Expect `42501` for maintenance table writes, direct truncation and calling the copy helper. A maintenance call with a nonmatching lease should fail with `P0002`. Test owner history UPDATE/DELETE rejection on a known fictional event (`23514`) and cross-schema foreign-key rejection (`23503`) only on this branch. If any forbidden operation succeeds, roll back and stop.
4. Run the reviewed image locally in Production mode using the restricted test runtime and maintenance settings only. Bind HTTPS to loopback with a locally trusted certificate and exact local origin. In independent browser contexts acquire a Viewer lease and two Dispatcher leases. Verify Viewer write denial and exercise shipment creation, assignment and history in one Dispatcher sandbox. Write a distinct fictional marker in the other and check that searches, details, dashboard and insights cannot see the first sandbox's data. Fill the remaining Dispatcher slot, release the first lease, and acquire another. Confirm the reused slot starts from seed, its prior cookie is rejected, the unrelated markers and history are preserved, and keys and AI ledger rows remain intact. A synthetic ledger fixture can be inserted by the test owner on the child if needed to demonstrate retention through reset. Keep real inference disabled.
5. With a live test lease and an encrypted test key ring, rerun the identical initialization command against the test host. Verify no change to migration history, role memberships, login validity, active lease, business records, protected key-ring rows or AI ledger. Verify the session survives a local web restart with the same PFX. Record sanitized results without credentials, cookies, raw key XML or real data.
6. Repeat the **read-only** production catalog inspection using the separately recorded production endpoint. Compare project, parent/default ID, endpoint, roles and application schema/migration state with the preflight baseline. Stop the local test web instance to allow idle suspension. Leave the branch in place until explicit deletion approval and record its branch ID for that later action. Production initialization and Render are independent approval gates.
