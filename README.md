# Pilot POS

One POS for every business.

Milestone 2 retail core built as a modular, India-ready commerce platform. Full offline sync is deferred.

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
