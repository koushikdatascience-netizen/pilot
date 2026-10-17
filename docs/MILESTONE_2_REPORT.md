# Pilot POS Milestone 2 Report

Verified locally on 2026-10-17. Retail scope only. Remote GitHub Actions status is not yet verified.

## 1. Architecture

The modular monolith remains: Next.js, Pos.Api, Pos.Application, Pos.Domain, and EF Core/Npgsql infrastructure. Sale completion remains server-authoritative and transactional. No vertical modules, offline SQLite sync, native printing, or payment gateways were added.

## 2. Files

Principal additions:

- `apps/web/app/login/page.tsx`
- `apps/web/components/checkout.tsx`
- `apps/web/components/customer-selector.tsx`
- `apps/web/components/product-manager.tsx`
- `apps/web/components/inventory-manager.tsx`
- `apps/web/components/retail-overview.tsx`
- `apps/web/components/sale-history.tsx`
- `apps/web/playwright.config.ts`
- `apps/web/tests/e2e/checkout.spec.ts`
- `tests/backend/Pos.Postgres.Tests/`
- `src/backend/Pos.Application/Common/PagedResponse.cs`
- `src/backend/Pos.Infrastructure/Persistence/TransactionLocks.cs`
- `.github/workflows/ci.yml`

Updated application services/DTOs for catalog, customers, inventory, reporting, sales, tenancy, and authentication; domain payment/GST calculations; persistence mappings/entities; API routes/security; dashboard/POS pages; API client/styles; solution/tests; package manifests; README/environment example and architecture/data-model docs.

## 3. Migrations

- `20260902052044_Milestone2RetailCore`: discount settings, customer state, tender snapshots.
- `20261015123912_ReceiptSnapshots`: receipt header JSON.
- `20261017103407_TenantRelationalIntegrity`: tenant/identifier alternate keys and composite foreign keys.
- `20261017103842_RetailSnapshotUpgrade`: HSN/SAC snapshot; legacy Account-to-Credit conversion, tender backfill, initial discount settings, obsolete seeded cashier permission removal.

All migrations were exercised against isolated PostgreSQL databases. Development database update confirmed no pending migrations.

## 4. APIs

- `GET /api/v1/tenancy/terminals`: active terminal contexts.
- `GET /api/v1/products?q=&barcode=&sku=&page=&pageSize=&includeInactive=`: paged catalog and exact lookups.
- `PUT /api/v1/products/{id}`: edit.
- `POST /api/v1/products/{id}/activate` and `deactivate`.
- `POST /api/v1/inventory/adjustments`: authorized ledger adjustments.
- `POST /api/v1/sales/hold`: persisted Held/Draft cart.
- `PUT /api/v1/sales/{id}/cart`: update an open cart without changing sale identity.
- `POST /api/v1/sales/{id}/complete-held`: complete/replay an open cart.
- `GET /api/v1/sales/history`: paged invoice/date/customer/terminal/payment filters.
- Existing complete-sale, customer, balances and report routes remain. Sale responses now include payment and receipt snapshots.

The legacy `GET /sales` list remains bounded to 100 entries for compatibility; the paged history endpoint is the back-office read path.

## 5. Concurrency

Checkout uses SERIALIZABLE transactions, sorted transaction-scoped stock advisory locks, idempotency-command locks, concurrency tokens, and unique constraints. Serialization/deadlock failures retry at most five attempts with randomized exponential backoff. Validation, stock and authorization errors do not retry. Idempotency-key unique conflicts are replayable; unrelated unique violations are not retried.

Held completion locks the sale, rechecks its state and stock, and returns the persisted completion on replay. Failed transactions roll back invoice, sale, payments, ledger, audit and outbox together.

## 6. Invoices

Invoice sequences are tenant/location/month scoped, persisted in PostgreSQL, protected by an advisory lock, unique index and xmin concurrency token. Tests sell different products concurrently and confirm eight unique invoices, then start another API host against the same database and verify the next invoice ends in 0009.

## 7. Inventory

Balances are sums of append-only ledger movements. Opening stock, increase, decrease and damage are supported. Decreases and checkout share the same stock-key lock. Adjustments require ManageInventory and append audit/outbox records in the same transaction. Held carts do not deduct inventory.

## 8. Payments

Cash, UPI, Card, Wallet and Credit are manual payment methods. Split tender is supported. AmountDue, AmountTendered and ChangeDue are persisted per tender. Payments must cover the authoritative total exactly; cash tender cannot be below its allocated amount and non-cash tender cannot produce change. Money precision is validated.

## 9. Discounts

Location settings default to 10% for cashiers and 100% for holders of ApplyDiscount. The server checks each line, including held-sale completion. The seeded cashier role no longer has ApplyDiscount. Cart estimates clamp discounts when quantity decreases.

## 10. GST

Customer state code takes precedence over GSTIN. Seller/customer comparison is in the India GST implementation. Same-state transactions snapshot CGST/SGST; interstate transactions snapshot IGST. Product names, prices, tax amounts, HSN/SAC and receipt headers are historical snapshots.

## 11. POS

Explicit login; active terminal selection remembered per tenant; exact barcode/SKU lookups; debounced name search; repeated scan increments; scanner refocus; clear unknown-barcode feedback; quantity/discount controls; quick customer search/create; held-cart editing/resume; split payments; tender/change display; receipt preview and 58mm/80mm print layouts.

Submission is guarded against repeated clicks. Uncertain network failures retain the command/payload for retry and freeze edits until resolved.

## 12. Back Office

Real revenue, transactions, average order, units sold, top products and payment mix; empty states; paged searchable catalog with editor/status controls; searchable paged sales history and receipt detail; warehouse stock/low-stock view and authorized movement form.

## 13. Tests Added

PostgreSQL tests cover login, tenant-scoped reads/references, raw cross-tenant FK rejection, product lifecycle, customer state flow, payment/HSN/receipt snapshots, opening stock, rollback, idempotency, audit/outbox, cashier discount permission, held-cart update/completion, concurrent held completion, and adjustment-versus-checkout.

Domain tests include GST, discounts/totals, inventory, split payments, cash tender, non-cash change rejection and customer-state precedence. Frontend tests cover inclusive/exclusive tax, discount totals, empty carts, fractional quantities and formatting.

## 14. PostgreSQL Results

11 PostgreSQL integration tests passed. Critical concurrency checks passed:

- Two distinct terminals compete for the final unit: exactly one completion and one conflict.
- Damage adjustment and checkout compete for one unit: exactly one succeeds.
- Parallel held completion captures payment and deducts stock once.
- Eight parallel sales across different products allocate unique invoices.
- A new API host reads persisted invoices and continues numbering.

Tests create/drop isolated databases using the configured PostgreSQL administrator; they do not change process-wide database environment variables. Local PostgreSQL 14 was used; CI config supplies PostgreSQL 16.

## 15. Frontend Results

Five calculation tests passed. Next.js 16.3.8 production build and TypeScript checks passed. One real API/browser golden-path E2E passed. It covers login, barcode, quantity, customer, cash tender, receipt, history, stock decrease, receipt PDF generation, and viewport overflow checks at 1366, 1920, 768 and 390 pixels.

Generated artifacts are ignored under `apps/web/test-results/`: desktop/tablet/mobile PNGs and 58mm/80mm receipt PDFs. npm audit reported zero vulnerabilities after patch updates.

## 16. Backend Results

Solution build passed with zero warnings/errors. Full test run: 15 domain + 2 fast integration + 11 PostgreSQL = 28 passing tests. A focused sale/held regression run was also performed after the final state guard.

## 17. CI

`.github/workflows/ci.yml` restores/builds .NET, runs solution tests against a PostgreSQL service, installs/tests/builds the web app, installs Chromium, and runs checkout E2E. Failures fail the job.

Remote Actions success is not claimed. The local GitHub CLI account reported an invalid stored token. A remote run must be checked after authenticated push.

## 18. Local Commands

From the repository root, with PostgreSQL running:

```powershell
$env:POSTGRES_CONNECTION_STRING='Host=localhost;Port=5432;Database=universal_commerce;Username=commerce;Password=commerce_dev_password'
$env:POSTGRES_TEST_ADMIN_CONNECTION_STRING='Host=localhost;Port=5432;Database=postgres;Username=postgres'
$env:AUTH_SIGNING_KEY=[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:DEMO_ADMIN_PASSWORD='ChangeMe123!'
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet tool restore
dotnet restore UniversalCommerce.sln
dotnet build UniversalCommerce.sln -m:1 --no-restore
dotnet tool run dotnet-ef database update --no-build --project src/backend/Pos.Infrastructure/Pos.Infrastructure.csproj --startup-project src/backend/Pos.Api/Pos.Api.csproj
dotnet test UniversalCommerce.sln -m:1 --no-build
dotnet run --project src/backend/Pos.Api/Pos.Api.csproj --no-build --urls http://localhost:5088
```

In a second terminal:

```powershell
cd apps/web
npm ci
npm test
npm run build
npx playwright install chromium
npm run test:e2e
npm run dev
```

Open `http://localhost:3000/login`. Development admin: `admin@example.test`, configured demo password. E2E creates opening stock and a completed sale; use a development/test database.

## 19. Limits

- Custom HMAC bearer format retained; no external identity provider. Production keys must be generated and supplied through secure configuration.
- Sale permission currently grants access to active terminals across the authenticated tenant; per-user location assignments and device registration remain future work.
- Draft state is supported through the API; the cashier's primary saved-cart action is Hold.
- Old invoices without receipt-header/HSN snapshots display only recorded fields; missing history is not rebuilt from live product data.
- Daily report boundary is UTC.
- Printing uses browser CSS/PDF; physical printer drivers/cash drawers are not integrated.
- Gateways, refunds, full offline recovery/sync and vertical modules remain deferred.
- Remote CI execution is unverified until GitHub authentication is restored.

## 20. Acceptance

Criteria 1-31 have local implementation/verification evidence across migrations, API tests, PostgreSQL concurrency/restart tests and real-browser checkout. Criterion 32 (GitHub Actions passes) remains unverified remotely. Do not interpret the local green run as a confirmed remote CI result.
