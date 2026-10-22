# Deployment Runbook

## Boundary

CI tests PostgreSQL integration, edge recovery, web compilation and real-browser checkout/administration before producing `pilot-linux-x64`. The archive contains `api/` (.NET framework-dependent publish), `web/` (Next standalone plus static assets), `efbundle` (self-contained Linux x64 migration runner), a commit/run manifest and this runbook. A SHA-256 companion verifies transport integrity. CI also applies the packaged bundle twice to a separate empty database, starts the published API in Production, verifies readiness and confirms no demo tenant was seeded. Download only from a successful trusted master CI run; checksums alone do not prove provenance.

`Approve Deployment Candidate` accepts a CI run ID, verifies a successful master push from this repository's CI workflow at the exact dispatch commit, then enters the `release` environment. Configure required reviewers, prevent self-review where available, and restrict that environment to master in repository settings. YAML cannot enforce these account-level settings. Without configured reviewers the environment is not a manual approval barrier.

The approval workflow preserves the candidate for handoff. It does not deploy to a host or apply a database migration. No production host, cloud account or deployment credentials have been provided. Do not treat a successful handoff as a live deployment. A host-specific deployment adapter can be attached after its target, service manager and secret store are chosen.

## Target Requirements

- Linux x64, ASP.NET Core 10 runtime, Node.js 22, PostgreSQL 16, HTTPS reverse proxy and service supervision.
- Bind API and Next to loopback (`127.0.0.1:5088` and `127.0.0.1:3000`). Expose only the HTTPS proxy. Proxy `/api/v1/` to the API and all web paths to Next; preserve Authorization, Host and request limits. The packaged Next fallback proxies API paths to localhost:5088 for the documented same-host topology.
- Leave NEXT_PUBLIC_API_BASE_URL unset for same-origin browser requests. Public Next variables are build-time values, not runtime secrets. For a different topology rebuild with an approved public API URL or route through a same-origin gateway.
- Inject POSTGRES_CONNECTION_STRING and a randomly generated AUTH_SIGNING_KEY (at least 32 bytes) through the host's secret store. Never place secrets in artifacts, CLI arguments, logs or source control. Set ASPNETCORE_ENVIRONMENT=Production. Production never migrates or creates demo identities at startup.
- Provision initial production tenant/admin through a separately reviewed bootstrap procedure; demo seeding is Development-only. No public tenant-registration endpoint is provided.
- Restrict audit/identity data, database roles, backups, artifact access and test evidence (screenshots contain demo data). Set request limits, rate limiting and TLS at the edge; trust forwarded headers only from configured proxies. API login limiter otherwise sees the proxy IP, so tune the gateway accordingly.

## Preflight And Rollout

1. Verify CI success, exact commit, artifact provenance, checksum (`sha256sum --check pilot-linux-x64.tar.gz.sha256`) and approved manifest. Stage into a new versioned release directory; do not overwrite the running release.
2. Back up PostgreSQL, confirm restore has been tested, and stop writes/enter a maintenance window. Test the migration bundle against a restored staging copy first, using separate DDL credentials from the application role.
3. Before the operational-hardening upgrade, find ambiguous emails with `SELECT lower("Email"), count(*) FROM users GROUP BY lower("Email") HAVING count(*) > 1;`. Resolve ownership explicitly; the migration intentionally fails rather than silently merging accounts. New ManageUsers/ViewAudit grants are copied once from existing ManageSettings roles. Review these grants before exposing administration.
4. Inject ConnectionStrings__Postgres for the DDL identity, run `./efbundle` from the extracted directory, check success and migration history, then remove that elevated secret from the migration process. Do not pass connection strings with `--connection`. The design-time factory reads ConnectionStrings__Postgres or POSTGRES_CONNECTION_STRING. Application startup does not apply production migrations.
5. Start API with `dotnet api/Pos.Api.dll --urls http://127.0.0.1:5088`. Start Next with `HOSTNAME=127.0.0.1 PORT=3000 node web/server.js`. Supply service-manager environment/secret configuration; run as non-root with least filesystem privileges.
6. Verify API `/health/ready` directly on loopback, HTTPS `/login`, static assets and same-origin API calls. Smoke test a designated non-production merchant account: sign in, role denial, audit filters, terminal selection and a controlled checkout. Never create a financial test sale in a live merchant's books without authorization.
7. Switch the proxy/release pointer, monitor error rate, readiness, audit entries, outbox backlog and sync cursor progress. Confirm old account sessions are invalid after reset/disable and permission revocation takes effect. Record actor, commit, migration, backup and rollout evidence outside the application as operational records.

## Failure And Rollback

Keep the previous release and backup until acceptance. If migration fails, do not switch binaries; inspect diagnostics and keep writes stopped. Never automatically run down migrations on live financial data. For additive-compatible application regressions, stop/drain new workers and restore an explicitly vetted previous binary against the upgraded schema, then smoke test and monitor.

Do not roll back to pre-Milestone-4 authentication code after credential revocation: old code ignores credential versions and trusts token permissions. Prefer a forward fix; rotate the signing key if all sessions must be revoked. A database restore requires reconciliation of all sales accepted since the backup and merchant approval; it is not a routine automated rollback. Preserve immutable sales, ledgers and audit history.

## Local Reproduction

Use README for PostgreSQL/environment setup. Build and test with `dotnet build UniversalCommerce.sln -m:1`, `dotnet test UniversalCommerce.sln -m:1 --no-build`, then `npm ci`, `npm test`, `npm run build`, `npx playwright install chromium`, `npm run test:e2e` in apps/web. Run `dotnet tool restore` and the CI `dotnet-ef migrations bundle` command for an appropriate target runtime. Cross-target bundle generation needs network access/runtime packs. Linux deployment artifacts are verified in Linux CI, not by pretending a Windows executable is Linux-compatible.
