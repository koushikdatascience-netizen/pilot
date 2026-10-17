# Pilot POS

One POS for every business.

Retail core and Milestone 3 offline preparation for a modular, India-ready commerce platform. Fully offline checkout remains deferred.

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

The Next.js app expects `NEXT_PUBLIC_API_BASE_URL=http://localhost:5088`. Select an active terminal before checkout; the last valid selection is remembered per tenant.

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
