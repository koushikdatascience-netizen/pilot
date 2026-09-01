# Universal Commerce OS

Milestone 1.5 foundation for a modular, offline-first, India-ready commerce/POS platform.

## Backend

```powershell
$env:POSTGRES_CONNECTION_STRING='Host=localhost;Port=5432;Database=universal_commerce;Username=commerce;Password=commerce_dev_password'
$env:DEMO_ADMIN_PASSWORD='ChangeMe123!'
$env:AUTH_SIGNING_KEY='development-signing-key-change-me-before-shared-environments'
dotnet build UniversalCommerce.sln -m:1
dotnet test tests\backend\Pos.Domain.Tests\Pos.Domain.Tests.csproj -m:1
dotnet test tests\backend\Pos.Integration.Tests\Pos.Integration.Tests.csproj -m:1
dotnet tool run dotnet-ef database update --no-build --project src\backend\Pos.Infrastructure\Pos.Infrastructure.csproj --startup-project src\backend\Pos.Api\Pos.Api.csproj
dotnet run --project src\backend\Pos.Api\Pos.Api.csproj --no-build --urls http://localhost:5088
```

The API listens on `http://localhost:5088` in Development. OpenAPI is available at `/openapi/v1.json`.

## Frontend

```powershell
cd apps\web
npm install
npm test
npm run build
npm run dev
```

The Next.js app expects `NEXT_PUBLIC_API_BASE_URL=http://localhost:5088`. Demo login values are documented in `.env.example`.

## Local Infrastructure

```powershell
docker compose up -d postgres
```

The committed Docker Compose file provides a local PostgreSQL development database. EF Core migrations live in `src/backend/Pos.Infrastructure/Persistence/Migrations`; the old hand-maintained SQL migration is no longer the schema source of truth.

## Milestone 1.5

- PostgreSQL persistence through EF Core/Npgsql.
- Signed bearer auth and tenant context from token claims.
- Tenant-scoped product, customer, inventory, sale, and reporting endpoints.
- Serializable sale completion with invoice sequence, idempotency, server-side tax/totals, inventory ledger writes, audit logs, and outbox events.
- API-backed dashboard and POS frontend.
