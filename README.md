# Universal Commerce OS

Milestone 1 foundation for a modular, offline-first, India-ready commerce/POS platform.

## Verified Backend Commands

```powershell
dotnet build src\backend\Pos.Api\Pos.Api.csproj
dotnet run --project tests\backend\Pos.Domain.Tests\Pos.Domain.Tests.csproj
dotnet run --project src\backend\Pos.Api\Pos.Api.csproj
```

The API listens on `http://localhost:5088` in Development.

## Frontend Commands

```powershell
cd apps\web
npm install
npm test
npm run dev
npm run build
```

In this environment, package installation was blocked by npm/pnpm registry TLS certificate validation. The frontend calculation tests run without installed packages.

## Local Infrastructure

```powershell
docker compose up -d postgres
```

Apply the initial PostgreSQL schema from:

```powershell
src\backend\migrations\001_initial_schema.sql
```

## Important Phase 1 Note

The backend currently uses an in-memory seeded repository so it can build without downloading EF Core/Npgsql packages. The PostgreSQL schema, connection settings, and persistence boundaries are present; wiring EF Core migrations and repositories is the next Milestone 2 persistence task.
