# Controlled deployment and recovery

No cloud resource has been created by M7. Commands below describe local rehearsal or a **future separately approved** release. Do not run them against an external account before reviewing free plans, secrets, backup and the exact commit's hosted CI.

## Local Production rehearsal

Prerequisites: Docker Compose, Python 3 and OpenSSL. This standalone stack uses a new database/volume, HTTPS and separate restricted database logins; it never reads the root development `.env` or Cloudflare token.

```sh
python3 scripts/prepare-local-portfolio.py
docker compose --env-file .portfolio-local/.env -f compose.portfolio.yaml build web
docker compose --env-file .portfolio-local/.env -f compose.portfolio.yaml up -d --wait postgres
docker compose --env-file .portfolio-local/.env -f compose.portfolio.yaml run --rm initialize
docker compose --env-file .portfolio-local/.env -f compose.portfolio.yaml up -d web
```

Open `https://localhost:5443/demo`. The generated certificate is a local self-signed development certificate: explicitly trust `.portfolio-local/server.crt` in your local test browser/OS, or use it as the CA for command-line checks. Never disable certificate verification in production. The preparation helper creates external persistent local files only when explicitly invoked, retains them on rerun and refuses partial overwrite. It never creates a certificate during application startup. Protect this ignored directory; do not publish it. Run `python3 scripts/verify-local-portfolio.py --confirm-local-restart` for an explicit HTTPS/role/CSRF/persistence smoke test, including a web-container restart and no inference. After installing the three Playwright browsers, `cd frontend` and run `node scripts/portfolio-production-smoke.mjs --accept-local-test-certificate` for the additional built-SPA/CSP/SignalR browser checks. This browser-only flag applies exclusively to the self-signed localhost rehearsal; it does not change server TLS policy. Stop with the same Compose command plus `down` (without `--volumes`) to retain data, keys and certificate.

## Production configuration

Use `deployment/production.env.example` as a **template**, not as deployable credentials. Set Production, exact `AllowedHosts` and exact HTTPS `Deployment__PublicOrigin`. The service serves React and API from one origin using `Dockerfile.web` and the host's `PORT`. Browser API base remains empty. No CORS grant is needed. The image runs as the .NET `app` user and can read Render runtime secret files through group 1000. No secret is copied into an image; do not add secret ARG instructions or broad COPY commands.

### Render Free HTTPS edge

Enable `Deployment__RenderFreeEdgeHttps=true` only on a **Render Free Docker Web Service**. Use one service in Frankfurt and the existing Neon database. Render redirects public HTTP to HTTPS, terminates TLS and forwards over internal HTTP. Its public port cannot be reached directly from the internet, and Free web services cannot receive private-network traffic. The opt-in asserts this deployment boundary. Keep the service on Free. Render's runtime metadata identifies a web service and its assigned public URL, but does not attest the compute plan. [Web Services](https://render.com/docs/web-services), [Private Network](https://render.com/docs/private-network), [default environment variables](https://render.com/docs/environment-variables).

- Set `ASPNETCORE_ENVIRONMENT=Production`, `SERVE_SPA=true`, `PORT=10000`, `Portfolio__Enabled=true` and `AI_ENABLED=false`. Use `Dockerfile.web` and the `/health` probe.
- Copy the actual assigned HTTPS URL into `Deployment__PublicOrigin` and its sole hostname into `AllowedHosts`. The app checks them against Render's `RENDER_EXTERNAL_URL` and `RENDER_EXTERNAL_HOSTNAME`, and requires `RENDER=true` and `RENDER_SERVICE_TYPE=web`. Leave those platform variables to Render. Inconsistent or missing metadata fails startup. The mode accepts only the assigned `onrender.com` hostname.
- Leave `Deployment__TrustedProxies__N`, `Deployment__TrustedNetworks__N` and `ASPNETCORE_FORWARDEDHEADERS_ENABLED` unset in this mode. FleetFlow rejects a mismatched request Host, discards forwarded headers, and sets the HTTPS scheme before HSTS, authentication, CSRF and SignalR. The public edge performs HTTPS redirection, so the internal HTTP hop does not cause a redirect loop.
- Demo-entry and login rate limits use the transport peer. Visitors can share a Render proxy address and its rate-limit bucket. Forwarded client IPs cannot change the bucket. Lease counts, per-lease write budgets and Identity lockouts continue to limit abuse.
- Use the existing runtime and maintenance Neon connections and Data Protection certificate. Render starts an initial deployment when creating a Web Service even with future auto-deploys off. If the assigned hostname only appears after creation, leave the Neon credentials unset on the initial build. The app requires the real origin and runtime secrets to start. Once configured, deploy the reviewed commit manually. On the first approved launch, verify HTTP redirects at the edge, HTTPS and HSTS, host handling, secure cookies, CSRF and same-origin SignalR.

### Other production hosts

Keep `Deployment__RenderFreeEdgeHttps` off. For TLS termination configure verified, narrow upstream addresses in `Deployment__TrustedProxies__N` or `Deployment__TrustedNetworks__N`. No proxy is trusted by default. Wildcard networks are rejected, and X-Forwarded-Host is ignored. Leave `ASPNETCORE_FORWARDEDHEADERS_ENABLED` unset. HTTPS redirection excludes the minimal health paths for platform probes. Test Host rejection and spoofed headers before public access.

Production requires PostgreSQL `SSL Mode=VerifyFull` (certificate chain **and hostname**), direct host, minimum pool zero, maximum runtime pool ten, maximum maintenance pool two, and Keepalive zero. The Neon templates require SCRAM channel binding and disable pooling for the migration owner. Use the platform CA store or the [Neon-documented public CA](https://neon.com/docs/connect/connect-securely) for a minimal client image without it. A local rehearsal uses its own test CA. No pooler hostname is accepted because session advisory locks require direct connections. No keep-alive service is required. See the [Neon preflight and grants runbook](neon-initialization.md).

Production headers include HSTS (30 days), CSP with same-origin scripts and exact WebSocket origin, no framing, nosniff, restrictive permissions/referrer policy. Radix requires inline styles; the CSP allows style-only `unsafe-inline`, not inline scripts. Hashed `/assets` files cache immutably; HTML, authenticated API and errors use no-store. Structured application failures omit exception contents; EF command logs are reduced in Production. Do not enable sensitive data, configuration dumps, HTTP body/header or provider transport logging.

## Data Protection certificates

Create and back up a durable RSA certificate/private key **outside the repository** before initialization. For example, from a protected external directory, use OpenSSL `req -x509 -newkey rsa:3072 -keyout protection.key -out protection.crt -days 730` (interactive passphrase) and `pkcs12 -export -inkey protection.key -in protection.crt -out protection.pfx` (interactive export password). Use a stable descriptive subject. This certificate protects keys, not the site's TLS certificate. Keep both encrypted private key/export and passwords separately protected.

Set `DataProtection__CertificatePath` and password through runtime secrets. A `.b64` path accepts base64-encoded PFX as a Render plaintext secret file; encode locally without printing it to terminal/logs and upload manually. For example `openssl base64 -A -in protection.pfx -out protection.pfx.b64`. Render exposes it under `/etc/secrets`. Back up the exact private key material: losing it makes persisted cookies/CSRF and other protected data unrecoverable. The app refuses an absent/private-key-less certificate; it never generates an ephemeral replacement. PostgreSQL stores encrypted Data Protection XML, with runtime SELECT/INSERT only. Public-demo startup rejects unencrypted legacy key rows. Do not copy a development key ring into the public database.

For rotation, first back up database and old certificate. Supply the new PFX as active, retain the old encrypted PFX/private key in `DataProtection__PreviousCertificates__0` (additional indices supported), and use a common export password for that configured set. Restart and test an old valid session. New keys use the active certificate; old keys require their previous private key until retired. Certificate replacement alone does not immediately rotate every existing Data Protection key. Rehearse rotation/restore before removing any old certificate. Do not delete persisted keys to fix authentication.

## Explicit schema initialization and roles

Use an existing persistent database with an owner/migration login unavailable to the web runtime. Initial provisioning requires CREATE ROLE and ownership/DDL for the application database. On Neon create runtime/maintenance logins through SQL, not the Console/API shortcuts that confer `neon_superuser` membership ([official role documentation](https://neon.com/docs/manage/roles)). M7's explicit provisioner uses SQL and refuses privileged existing roles. It does not create a Neon account/project/database or purchase anything.

The owner creates the additive EF migration, protected template, fixed schemas, functions and bounded passwordless demo identities. Permission groups are NOLOGIN. Runtime can perform logistics operations and existing Identity stamp updates, but cannot create users/roles, change role membership, truncate tables, own schema objects or execute reset functions. Maintenance has only usage and the narrow function execution grant. PostgreSQL function ownership remains with migration ownership; never grant owner membership to either runtime login.

Copy production settings to a protected external initialization file, then apply `deployment/initialization.env.example` overrides: FleetFlow connection uses the owner; PortfolioRuntime and PortfolioMaintenance contain separate generated login credentials (at least 24 characters). Do not give the owner connection to the running service. With a reviewed locally built image:

```sh
export FLEETFLOW_RELEASE_IMAGE=YOUR_REVIEWED_LOCAL_TAG_OR_DIGEST
export FLEETFLOW_INITIALIZATION_ENV=/absolute/private/initialization.env
export FLEETFLOW_CERTIFICATE_DIRECTORY=/absolute/private/runtime-certificates
sh scripts/initialize-portfolio.sh --confirm-database EXACT_DATABASE_NAME --confirm-host EXACT_DIRECT_HOST
```

This explicit CLI invokes `--portfolio-init`. It checks the exact direct host and database, applies EF migrations, initializes seed/schema/account data transactionally, then provisions the two logins together in a separate transaction. Repeating it retains active leases, credentials, keys and usage; existing passwords are never overwritten. A failed role step can be repeated after inspecting the cause. A failed initial seed/DDL rolls back its initialization transaction. EF migrations have their own normal migration transaction semantics. Normal Development `--seed`/`--seed-users` remain prohibited in Production.

`.github/workflows/portfolio-initialize.yml` is an **optional manual schema-maintenance workflow**. Before using it, create a protected `portfolio-maintenance` GitHub Environment with reviewers and environment-scoped `INITIALIZATION_ENV` and `DATA_PROTECTION_PFX_BASE64` secrets. Its manual main-branch trigger requires a full reviewed SHA, successful hosted quality workflow for that SHA and exact database and direct host confirmation. It builds without secrets and mounts them only for the one-shot CLI. Hosted execution of this workflow remains unverified.

## One reviewed release and rollback

Before release, stop accepting new demos and let/revoke existing leases, verify a restorable backup, review migration SQL and test against a disposable copy. Require all five hosted quality jobs for the exact commit. Apply the explicit owner procedure, verify catalog/schema compatibility, then separately approve a **manual** Render deployment of that reviewed commit. Disable Render automatic deploys. Set the runtime settings and secret file manually; never install migration credentials in the web service. Test HTTPS, cookies/CSRF, two sandbox sessions, WebSockets, restart persistence, quota fallbacks and all browsers before sharing the URL. AI stays off until a separate provider/account review.

The M7 EF migration is additive and preserves original public logistics/Identity/AI data. Once portfolio slots/schemas exist its Down migration deliberately refuses downgrade. **Never roll back to pre-isolation M6 binaries against an initialized demo database.** Render's image rollback is not a database rollback. Prefer a forward fix; use a reviewed backup restore to an isolated database for disaster recovery, then revoke/inspect restored leases before opening traffic. Future domain migrations need a reviewed matching fixed-schema upgrade; startup/initializer fail closed when the catalog revision differs.

## Backup and recovery

Use PostgreSQL 18-compatible `pg_dump`/`pg_restore` tools and a protected `PGPASSFILE`/service file rather than passwords in command arguments. Export a custom-format dump of the whole database (all public and portfolio schemas, Identity, AI ledger, leases, Data Protection keys, functions and migration/catalog metadata). Keep an encrypted independent copy plus certificate backups and separately protected role-creation instructions. Schema dumps do not include role passwords. Free-provider restore windows are short and are not an independent backup. Schedule no paid cron or keep-alive service; perform manual backups before releases and periodically while maintaining the demo.

Restore into an isolated persistent database using the migration owner; recreate the two SQL logins/groups and grants, supply the original active/previous certificates, and verify encrypted key decryption and data counts. Expire all restored demo leases and rotate their stamps through reviewed owner maintenance before accepting visitors. Preserve the AI ledger so a restore does not accidentally grant extra daily budget; if the backup is old, keep external AI disabled until the next UTC day and review account usage. Test normal immutable-history triggers and runtime denials. Only then approve runtime connection changes. Losing both backup or private keys can require a fresh demo and loss of sessions; no paid recovery service is assumed.
