# Milestone 3: Offline Preparation Report

Implementation and local verification: 2026-10-17.

## Scope

Implemented the next roadmap milestone in `PHASE_1.md` and ADR 0005. No separate Milestone 3 prompt existed; the bounded implementation scope is recorded in `MILESTONE_3.md`. This is edge preparation, not fully offline checkout. Milestone 4 and business verticals were not started.

## Architecture And Files

- `src/backend/Pos.Sync.Contracts`: version-1 registration, snapshot, catalog, change-page and acknowledgement contracts.
- `src/backend/Pos.Application/Sync`: tenant-scoped registration/sync service and transactional outbox publisher.
- `src/backend/Pos.Api/OutboxWorker.cs`: scoped publisher every two seconds, bounded to 20 tenants and 100 events per tenant per pass, retry on a subsequent pass after failure. Disabled in Testing to keep publication deterministic.
- `apps/edge/Pos.Edge`: .NET CLI/library backed by Microsoft.Data.Sqlite, permanent binding, catalog/inbox/checkpoint, command queue and upload/pull coordinator.
- `tests/backend/Pos.Postgres.Tests/SyncFlowTests.cs` and `tests/edge/Pos.Edge.Tests/EdgeRecoveryTests.cs`: real PostgreSQL and SQLite coverage.
- Solution/project references, API composition/routes, product-event version capture, EF mappings/entities/migration/snapshot, README, architecture/data-model/ADR documentation updated. Browser behavior and retail business rules were not changed.

## Database

Migration `20261017110655_OfflinePreparation` adds `terminal_devices` and `sync_changes`. Tenant-composite foreign keys and tenant-scoped uniqueness enforce registration and event identity. PostgreSQL allocates sequence values; gaps are valid. Existing sale/stock rows remain untouched. Fresh migrations passed against isolated PostgreSQL databases and the development API migration path passed in E2E.

SQLite uses WAL and parameterized commands. Catalog updates, inbox receipt and cursor commit in one transaction. Each upload has an immutable UUID/payload, due time, two-minute lease and attempt token. Expired lease owners cannot overwrite newer results. Completed server responses and unresolved conflicts remain on disk across restarts.

## API And Permissions

- `POST /api/v1/sync/devices`: register device/terminal; ManageSettings required. Exact registration replay is idempotent; silent terminal/device reassignment is rejected.
- `GET /api/v1/sync/devices/{deviceId}/snapshot`: consistent catalog/tax bootstrap and feed checkpoint.
- `GET /api/v1/sync/devices/{deviceId}/changes?after=&limit=`: ordered, tenant-scoped feed; page size 1-500; negative/future cursors rejected.
- `POST /api/v1/sync/devices/{deviceId}/ack`: monotonic acknowledgement bounded by offered checkpoints.
- `POST /api/v1/sync/devices/{deviceId}/sales`: registered-terminal upload through the existing complete-sale service.

Snapshot/feed/ack/upload require CompleteSale, active terminal and tenant-owned registration. Bearer authentication and active tenant/user checks remain enforced. A registered UUID is not an independent credential.

## Consistency And Conflict Rules

Outbox publication and its processed marker commit together under a tenant publication lock. Multiple publishers cannot expose later sequence values before an earlier publication commits. Bootstrap catalog and cursor share one repeatable-read snapshot; offered checkpoint updates and acknowledgements share a device lock. Product event versions match saved versions, and stale events cannot replace newer cached products.

Acknowledgement follows durable local page commit. Lost acknowledgements resume from the stored cursor. Lost upload responses replay the same command, relying on server idempotency rather than generating a replacement UUID. HTTP 401/408/429 and server/network failures retain Pending state with bounded retry backoff; validation, stock and permission conflicts retain Conflict state for review. No automatic repricing, payment rewriting or financial merge occurs.

The cloud remains authoritative for stock, discounts, taxes, payments and invoice allocation. A queued request is not a completed sale or offline invoice. Changed prices/taxes/stock can cause upload rejection.

## Verification

- Solution build: zero warnings, zero errors.
- 15 domain tests, 2 fast integration tests, 16 PostgreSQL tests and 13 SQLite edge tests passed: 46 total.
- PostgreSQL coverage adds registration replay/conflict/RBAC, cross-tenant isolation, parallel publication/no duplicate feed records, rollback exclusion, page ordering, acknowledgement bounds/monotonicity, snapshot/event versions, and real SQLite-to-server upload/replay with exactly one sale.
- SQLite coverage adds restart durability, atomic rollback, stale-version protection, immutable command IDs, crash/lease recovery, lost upload response, stock conflict retention, credential-field rejection, transient HTTP retry/backoff, lost acknowledgement/restart and exclusion of customer payload from the local inbox.
- Five frontend calculation tests passed.
- Next.js production build and TypeScript checks passed.
- Real-browser checkout E2E passed: login, checkout, persisted invoice/history, stock reduction, receipt/PDF and viewport checks.
- CLI `status` and authenticated `sync` smoke checks passed against the running development API: device registration, four cached products and durable cursor 27. Local commands and environment configuration are in README. Local SQLite data remains ignored under `.local/`.

The existing GitHub Actions workflow automatically includes the added projects through the solution, using PostgreSQL 16 and a real Chromium checkout test. Remote verification is recorded below after the implementation push.

## Limits And Follow-Up

- No browser-to-edge integration, fully offline invoicing, stock cache/authority, local tax engine, payment capture integration or native hardware packaging.
- No conflict-resolution UI or automatic correction/retry of permanent conflicts. The CLI exposes state counts; retained requests require operator investigation.
- SQLite is not encrypted. Protect file permissions and backups. Passwords/tokens are held only in memory; customer profiles are not cached, but queued identifiers and saved sale responses can contain personal data.
- One device per terminal. Device replacement/unregistration, separate device credentials and per-user location assignment are deferred.
- Bootstrap is unpaged. Feed/inbox/queue retention, compaction, poison-event handling and production monitoring are deferred. Tax metadata is bootstrap-only until tax-edit APIs/events are introduced.
- Publication retries do not increment the persisted attempts counter on rollback. The counter measures successful publication, not failed delivery telemetry.
- CLI sync is bounded (50 uploads, 100 feed pages per invocation), not a resident scheduler. Invoke again to drain backlog or recover due retries.
- Run local .NET commands with `-m:1`; an unrestricted parallel invocation left orphaned Windows build workers, which were stopped. The serial build and tests passed.

## Remote CI

Implementation commit `e79342a` was pushed to `master`. [GitHub Actions run 39028252264](https://github.com/koushikdatascience-netizen/pilot/actions/runs/39028252264) passed .NET restore/build/all solution tests against PostgreSQL 16, frontend install/tests/production build, Chromium installation and real-browser checkout E2E. No CI failures remained.
