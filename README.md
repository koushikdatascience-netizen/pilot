# Pilot POS

One POS for every business.

Retail core, offline preparation and operational hardening for a modular, India-ready commerce platform. Fully offline checkout remains deferred.

## Backend

```powershell
$env:POSTGRES_CONNECTION_STRING='Host=localhost;Port=5432;Database=universal_commerce;Username=commerce;Password=commerce_dev_password'
$env:DEMO_ADMIN_PASSWORD='ChangeMe123!'
$env:AUTH_SIGNING_KEY='development-signing-key-change-me-before-shared-environments'
dotnet build UniversalCommerce.sln -m:1
dotnet test UniversalCommerce.sln -m:1 --no-build
dotnet tool run dotnet-ef database update --no-build --project src\backend\Pos.Infrastructure\Pos.Infrastructure.csproj --startup-project src\backend\Pos.Api\Pos.Api.csproj
dotnet run --project src\backend\Pos.Api\Pos.Api.csproj --no-build --urls http://localhost:5088
```

The API listens on `http://localhost:5088` in Development. OpenAPI is available at `/openapi/v1.json`.

## Frontend

```powershell
cd apps\web
npm ci
npm test
npm run build
npm run dev
```

Open `http://localhost:3000/login`. Development seed credentials are `admin@example.test` or `cashier@example.test` with the configured `DEMO_ADMIN_PASSWORD` (local default: `ChangeMe123!`). Credentials are entered at sign-in, not embedded in the browser bundle.

The Next.js app uses same-origin API requests by default and proxies `/api/v1` to the local API on port 5088. `NEXT_PUBLIC_API_BASE_URL` is an optional build-time override. Select an active terminal before checkout; the last valid selection is remembered per tenant.

Run `npm run test:e2e` after `npx playwright install chromium` and the production build. Playwright starts the API and web server when they are not already running. The test adds opening stock and completes a sale, so use a development/test database. Receipt PDFs and desktop/tablet/mobile screenshots are written under `apps/web/test-results`.

## Local Infrastructure

```powershell
docker compose up -d postgres
```

The committed Docker Compose file provides a local PostgreSQL development database. EF Core migrations live in `src/backend/Pos.Infrastructure/Persistence/Migrations`; the old hand-maintained SQL migration is no longer the schema source of truth.

The PostgreSQL integration project creates and drops isolated databases. Configure `POSTGRES_TEST_ADMIN_CONNECTION_STRING` with a local test administrator that can create databases; its default is `Host=localhost;Port=5432;Database=postgres;Username=postgres`. The CI workflow supplies PostgreSQL 16 through a service container.

Use a randomly generated signing key of at least 32 bytes in shared environments. The documented key and demo passwords are local-development examples.

## Milestone 2

- PostgreSQL persistence through EF Core/Npgsql.
- Signed bearer auth and tenant context from token claims.
- Tenant-scoped product, customer, inventory, sale, and reporting endpoints.
- Serializable sale completion with retry-safe invoice sequence, idempotency, server-side tax/totals, inventory ledger writes, audit logs, and outbox events.
- Server-persisted held sales, resume/complete flow, split tender snapshots, receipt view, and sales history.
- API-backed dashboard and POS frontend with product, customer, stock, reporting, and checkout workflows.

Implementation and verification details: [Milestone 2 report](docs/MILESTONE_2_REPORT.md).

## Milestone 3 Edge Preparation

The .NET SQLite demonstrator caches the catalog, checkpoints a durable change feed, and queues immutable sale requests for server completion. It does not issue offline invoices or connect the browser POS to SQLite.

With the API running, configure a separate database per device and register an active terminal using an administrator account:

```powershell
$env:PILOT_SERVER_URL='http://localhost:5088/'
$env:PILOT_EDGE_DB='D:\pilot-data\edge.db'
$env:PILOT_EDGE_EMAIL='admin@example.test'
$env:PILOT_EDGE_PASSWORD=$env:DEMO_ADMIN_PASSWORD
$env:PILOT_TERMINAL_ID='<active terminal UUID from /api/v1/tenancy/terminals>'
dotnet run --project apps/edge/Pos.Edge -- sync
dotnet run --project apps/edge/Pos.Edge -- status
dotnet run --project apps/edge/Pos.Edge -- enqueue <sale-request.json>
dotnet run --project apps/edge/Pos.Edge -- sync
```

`enqueue` and `status` work without a network after bootstrap. The JSON file uses the existing complete-sale contract: `locationId`, `terminalId`, permanent `clientGeneratedId`, optional `customerId`, `lines` (productId/quantity/discountAmount), and `payments` (method/amount/amountTendered/reference). Keep the same UUID and payload when retrying. Prices, tax, stock and authorization are checked by the server at upload time; requests can become conflicts rather than completed sales.

`status` reports pending, leased/in-flight, conflict and synced counts plus the committed cursor. Conflicts stay retained for operator review, with no automatic rewrite or retry. This preparation milestone does not include a conflict-resolution UI. A process crash releases an upload through its two-minute lease; transient retries use bounded backoff. Run `sync` again to resume; the CLI is not a daemon.

The SQLite file contains catalog and sale data, is not encrypted, and requires OS access controls and secure backups. Passwords and bearer tokens are not persisted by the client. Customer profiles are not cached, but queued identifiers and completed sale responses can contain personal information. Registration is one device per terminal; replacement/unregistration and device-specific credentials are deferred. Do not delete a database containing unresolved requests.

Scope and verification: [Milestone 3 scope](docs/MILESTONE_3.md), [Milestone 3 report](docs/MILESTONE_3_REPORT.md).

## Milestone 4 Operations

Open `/administration` after signing in. ManageUsers grants user/role administration; ViewAudit grants paged, read-only audit review. Existing ManageSettings roles receive both permissions once through the upgrade migration; later role edits are not overwritten by startup seeding. The seeded development administrator receives all permissions; cashiers receive neither administrative permission.

Users support name/status/role edits, creation and administrator password resets. New/reset passwords require 12-128 characters. Password resets and status changes invalidate existing sessions; permissions are reloaded from PostgreSQL on every authenticated request. A tenant-scoped identity lock and last-active-administrator check prevent accidental lockout. Stale edits return 409 and require refresh. Users/roles are retained rather than hard-deleted. Audit changes include safe before/after metadata, never passwords or password hashes.

`npm run start` starts the built standalone web runtime and stages its static files. CI tests that runtime before packaging Linux API/web output and a self-contained EF migration bundle. EF tools require an explicitly configured database connection; they no longer silently choose a demo database.

See [Milestone 4 scope](docs/MILESTONE_4.md), [verification report](docs/MILESTONE_4_REPORT.md) and [deployment runbook](docs/DEPLOYMENT.md). Production deployment remains an operator handoff until a target host, secrets and release-environment reviewers are configured; the repository does not provision or silently deploy production infrastructure.

## Milestone 5 Retail Operations

Open an original receipt from sales history to record a partial return or full completed-sale void. Held carts can be voided from the POS. ProcessReturns and VoidSales are separate permissions. Completed originals stay unchanged; separate correction receipts retain the reason, original line amounts, restock decision and manual refund allocation. A completed sale with an earlier correction cannot be voided. Refunds cannot exceed the remaining original tender; electronic refund records require an operator reference. These records do not execute payment-provider refunds or submit statutory credit notes.

Open `/cash-drawer` with ManageCashDrawer to open a terminal session, record paid-in/out and close against counted cash. Existing checkout remains usable without an open drawer; cash refunds require an open processing-terminal drawer with enough expected cash. Cash movements use the amount due, not tendered cash including change. Session closure freezes expected cash and variance. No physical drawer driver is included.

The migration grants the three new permissions once to existing ManageSettings roles, not cashiers. Rejected requests leave no financial/stock changes; uncertain browser requests retain their UUID and payload in session storage for retry, including after a reload in the same tab. Do not clear session storage or close the tab with an unresolved command. Today's dashboard distinguishes gross sales, corrections recorded today and net revenue, including returns of earlier invoices.

See [Milestone 5 scope](docs/MILESTONE_5.md) and [verification report](docs/MILESTONE_5_REPORT.md).
