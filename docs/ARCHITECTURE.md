# Universal Commerce OS Architecture

## Source Inputs

The architecture is based on `prompt.md`, `product-concept.md`, `docs/PHASE_1.md`, and the Milestone 1.5 implementation prompt. The product concept calls for one commerce core that can support many verticals without turning retail, restaurant, hotel, pharmacy, liquor, wholesale, and mall behavior into one tangled POS screen.

## System Shape

Version 1 remains a modular monolith, not microservices. Milestone 1.5 adds real persistence and application boundaries:

```
apps/web
  Next.js back office
  API-backed POS checkout

src/backend
  Pos.Api            HTTP, auth, OpenAPI, ProblemDetails, DI
  Pos.Application    use cases, validation, tenant-scoped workflows
  Pos.Domain         money, tax, sales, inventory, identity abstractions
  Pos.Infrastructure EF Core DbContext, migrations, development seed

tests/backend
  Pos.Domain.Tests
  Pos.Integration.Tests
```

`Program.cs` is now composition and routing only. Business rules live in domain/application services, and database shape lives in infrastructure.

## Tenant And Auth Boundaries

The hierarchy is:

`Tenant -> Organization -> Business/Brand -> Location -> Terminal`

Warehouses belong to locations for Phase 1. Locations also carry a default sales warehouse for POS stock reductions.

Every merchant-owned aggregate carries `TenantId`. API tenant context is derived from authenticated token claims through `ITenantContext`; arbitrary `X-Tenant-Id` headers are not trusted. Role permissions are loaded at login and reloaded on every authenticated request before ASP.NET Core authorization policies run, so existing tokens cannot retain revoked grants.

Milestone 4 adds AdministrationService (ManageUsers) and a read-only AuditService (ViewAudit), with dedicated web views at `/administration`. Identity mutations take a tenant-scoped transaction advisory lock, recheck current administrator permission, validate tenant-owned references and expected versions, then persist changes and sanitized audit metadata together. Role-only changes also advance the optimistic token. A final check prevents removal of the last active ManageUsers holder, including concurrent self-demotions.

User AuthVersion is signed into tokens and checked against PostgreSQL. Password reset and disable/re-enable advance it, invalidating prior sessions. Legacy tokens default to version 1; existing accounts migrate to version 1. Global normalized-email uniqueness matches email-only login; ambiguous existing accounts must be resolved before migration. Authorization role editing is explicit; no external IdP, MFA, self-service recovery or location assignment is added.

## Persistence

PostgreSQL is the system of record. EF Core/Npgsql owns schema generation through migrations under `Pos.Infrastructure/Persistence/Migrations`.

The initial EF migration creates tenants, organizations, businesses, locations, terminals, warehouses, roles, users, role permissions, user roles, categories, tax categories, products, customers, sales, sale lines, payments, inventory transactions, audit logs, outbox events, and invoice sequences. Tenant-scoped uniqueness is enforced for SKU, barcode, device code, roles, invoice numbers, and idempotent sale commands.

Committed app settings do not contain database credentials or token signing secrets. Local development uses environment variables, launch settings, `.env.example`, or Docker Compose values.

## Core Modules

Identity owns users, roles, permissions, PBKDF2 password hashes, cashier/admin identities, token issuance, and authorization claims.

Tenancy owns tenants, organizations, businesses, locations, terminals, and warehouses.

Catalog owns categories, products, barcodes, tax categories, unit metadata, and searchable product identity.

Customers owns customer profile, phone, email, GSTIN/tax IDs, and future credit/loyalty extension points.

Inventory owns immutable inventory ledger entries and stock balance read models. Stock is not stored directly on product records.

Sales owns transactional sale completion, sale lines, payments, invoice sequencing, immutable line snapshots, discounts, GST totals, idempotency, audit, and outbox events.

Reporting reads from sales, inventory, and catalog models to expose daily KPIs, top products, and low-stock products.

## Sale Transaction Boundary

Sale completion is the critical aggregate transaction. It:

1. Validates tenant-owned location, terminal, customer, products, taxes, and warehouse.
2. Aggregates duplicate product lines before stock validation.
3. Calculates server-side price, discount, GST, CGST/SGST/IGST, taxable amount, and grand totals.
4. Requires captured payments to exactly equal the calculated grand total.
5. Allocates an invoice from `InvoiceSequence` scoped by tenant, location, period, and prefix.
6. Persists sale, lines, payments, inventory reductions, audit log, and outbox events in one serializable database transaction.
7. Replays duplicate tenant/terminal/client-generated sale commands idempotently.

Inventory concurrency is guarded by the serializable transaction around stock reads and ledger writes, unique idempotency constraints, and database row-version checks for invoice sequence updates.

Milestone 2 adds transaction-scoped PostgreSQL advisory locks for stock keys, invoice sequences, held-sale transitions, and idempotent commands. Stock decreases and checkout share the same stock lock. Milestone 4 regression testing exposed a mixed-isolation hole: a Serializable checkout snapshot could predate a Read Committed adjustment committed before the stock lock. Adjustments now also use Serializable with bounded retries, so all stock-decreasing writers participate in PostgreSQL conflict detection. Serialization/deadlock failures retry up to five attempts with randomized exponential backoff; validation and permission failures do not retry. Unique failures retry only for the sale idempotency key.

Held/Draft carts store line snapshots without payments or stock deduction. Cart updates preserve sale identity and are allowed only before completion. Completion rechecks stock, terminal status, and discount permission, then records payments, ledger movements, audit, and outbox atomically. Receipt headers and HSN/SAC are captured for new sales; historical missing snapshots are not reconstructed.

Tenant-owned entity relationships use composite tenant/identifier foreign keys. User-role assignments are validated on save and role loading is tenant-scoped. Members with sale permission currently have tenant-wide active-terminal access.

The web app uses an explicit sign-in route and session-scoped bearer token. Login is rate-limited; disabled users and tenants are rejected on authenticated requests. Request correlation scopes are included in JSON logs. The custom HMAC bearer format is retained for this milestone.

## Retail Correction And Drawer Transactions

Milestone 5 adds SaleCorrectionService and CashDrawerService, with independent ProcessReturns, VoidSales and ManageCashDrawer policies. Original completed sales, payment captures and negative inventory entries remain immutable. Separate corrections reference the original lines, preserve historical discounts/tax, and optionally append positive SaleReturn ledger entries to the original warehouse. Effective returned/voided status is derived without rewriting completed invoices. Held/Draft cancellation alone transitions the original open status to Voided; an expected-status guard prevents a concurrent completion from turning a cart cancellation into a monetary reversal.

Corrections use Serializable transactions, per-command/per-sale advisory locks, payload hashes and bounded concurrency retries. Cumulative decimal allocation divides original gross by returned quantity, tax within gross and central/state tax within tax; this avoids negative partial components and makes full returns exact. Captured AmountDue less earlier refunds caps each tender. Inventory, refunds, drawer capture, audits and outbox events commit together. Electronic tender refunds are operator records, not gateway calls.

Drawer sessions are terminal-scoped with a partial unique index allowing one open session. All opening, closing, paid-in/out and sale/refund capture share a terminal advisory lock. Movements also advance the session concurrency token, protecting against a transaction snapshot older than closure. Expected cash is opening float plus signed movements; closing stores expected/count/variance without later edits. No-session checkout remains valid, but cash refunds require sufficient cash in an open processing-terminal session. The browser freezes uncertain command UUIDs/payloads in session storage for replay after network loss. It is not offline refund processing or a durable cross-device work queue.

Reporting keeps gross invoices and tender captures separate from correction totals/refund mix. Net daily revenue subtracts corrections recorded today, regardless of original invoice date.

## Offline And Sync Strategy

The product is offline-first by design. Milestone 1.5 keeps the future edge path intact:

- UUID identifiers for distributed business entities.
- `ClientGeneratedId`, `TerminalId`, timestamps, version tokens, and `SyncStatus` where applicable.
- Immutable sales and inventory ledger movements.
- Transactional outbox events.
- Hardware-neutral POS UI and barcode keyboard-wedge flow.

Milestone 3 adds `Pos.Sync.Contracts` and `apps/edge/Pos.Edge`, a .NET SQLite demonstrator. Registration binds a permanent local UUID to one active tenant-owned terminal. ManageSettings authorizes registration; CompleteSale authorizes snapshot/feed/ack/upload. Existing tenant membership and bearer policies still apply; device identity is not a separate authentication credential.

The background worker publishes bounded outbox batches into `sync_changes`, atomically marking publication. A per-tenant PostgreSQL advisory lock serializes publishers so committed sequence cursors cannot skip another publisher's uncommitted events. Device acknowledgements are separate, monotonic and bounded by delivered checkpoints. Bootstrap reads catalog and feed cursor from one repeatable-read snapshot. Product versions prevent older delayed events from overwriting newer bootstrap values.

SQLite stores the binding, catalog, inbox, checkpoint and immutable sale-command queue. Cache changes and cursor commit together before acknowledgement. Uploads lease a command for two minutes and retain its UUID/payload across retries and restarts. The original complete-sale service remains authoritative, including idempotency, permissions, pricing, GST, stock and invoice allocation. Permanent validation/stock/permission failures are retained as conflicts; there is no automatic financial merge.

Browser integration, offline invoices, encrypted local storage, device credentials, replacement/revocation, feed retention, and conflict-resolution UI remain later work. Customer profiles are not cached; non-catalog events retain only inbox metadata locally. Completed sales are immutable; corrections should be represented as returns, voids, or adjustment operations.

## Tax Strategy

The tax engine is abstracted from sales. India GST is the first implementation and supports INR, HSN/SAC metadata, inclusive/exclusive tax, CGST, SGST, and IGST split. The persisted sale line stores transaction-time taxable amount and tax component snapshots.

## Payment Strategy

Phase 1 supports manual cash, UPI, card, wallet, and account payment records. The domain model stores provider-neutral payment method, amount, reference, status, and metadata. It never stores raw card PAN or CVV data.

## Deployment Topology

Local development uses:

```
Next.js app -> ASP.NET Core API -> PostgreSQL
```

Production starts as:

```
Browser/POS client -> ASP.NET Core API -> PostgreSQL
                         |
                         +-> outbox_events
```

Later hybrid topology adds:

```
POS Edge Runtime -> local SQLite -> sync engine -> cloud API -> PostgreSQL
```

Milestone 4 CI packages a tested Linux API publish, standalone web runtime and self-contained EF migration bundle. The browser defaults to same-origin API requests. A dispatch workflow checks trusted CI provenance and enters the configurable `release` environment before preserving the approved candidate. It is a handoff pipeline, not an implemented production-host deployment. See `docs/DEPLOYMENT.md` for prerequisites, reviewer configuration, explicit migration credentials and security-aware rollback constraints.
