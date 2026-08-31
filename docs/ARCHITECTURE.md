# Universal Commerce OS Architecture

## Source Inputs

The initial architecture is based on `prompt.md`, `product-concept.md`, and the included POS market research PDF. The research shows a fragmented POS market where the strongest products win through ecosystem depth, offline resilience, payments, vertical workflows, and localization. The product concept narrows that into one commerce core with configurable vertical modules.

## System Shape

Version 1 is a modular monolith, not microservices. The backend is an ASP.NET Core Web API with strict module boundaries inside one deployable service. The web app is a Next.js back office with an isolated POS workspace that can later become a desktop edge shell.

```
apps/web
  Back office
  POS checkout shell

src/backend
  Pos.Api
  Pos.Domain

tests/backend
  Domain invariant tests
```

## Tenant Hierarchy

The hierarchy is:

`Tenant -> Organization -> Business/Brand -> Location -> Terminal`

Warehouses belong to locations in Phase 1. Future enterprise and mall deployments can add cross-location warehouses and aggregate analytics without weakening tenant isolation.

Every business record that can expose merchant data carries `TenantId`. Server-side query boundaries must enforce that scope. Frontend filtering is never considered sufficient tenant security.

## Core Modules

Identity owns users, roles, permissions, password hashing policy, cashier identity, and authorization claims.

Tenancy owns tenants, organizations, locations, terminals, and warehouses.

Catalog owns categories, products, barcodes, tax categories, unit metadata, and searchable product identity.

Customers owns customer profile, phone, email, GSTIN/tax IDs, and future credit/loyalty extension points.

Inventory owns immutable inventory ledger entries and stock balance read models. Stock is not stored directly on product records.

Sales owns sales, sale lines, payment records, sale status, invoice numbering, immutable line snapshots, discounts, totals, and sale completion.

Reporting initially reads from sales, inventory, and catalog models to expose daily KPIs, top products, and low-stock products.

## Transaction Boundaries

Sale completion is the critical aggregate transaction. It must:

1. Validate tenant, location, terminal, sale lines, payments, and tax rules.
2. Calculate immutable sale totals.
3. Persist the completed sale and payments.
4. Write inventory ledger reductions for stock-tracked items.
5. Append outbox events such as `SaleCompleted`, `PaymentRecorded`, and `InventoryReduced`.

The operation should commit atomically in the cloud API. The later local edge runtime will store the same operation shape locally, then sync idempotently.

## Offline Strategy

The product is offline-first by design. Phase 1 prepares the contracts:

- UUID identifiers for every distributed business entity.
- `ClientGeneratedId`, `TerminalId`, `CreatedAtUtc`, `UpdatedAtUtc`, `Version`, and `SyncStatus` where applicable.
- Immutable sales and inventory ledger movements.
- Transactional outbox events.
- Hardware-neutral POS UI and barcode keyboard-wedge flow.

Full local SQLite sync is a later milestone. The current backend exposes models that will survive the move to local edge execution.

## Sync Strategy

Future sync is command/event based. A terminal creates immutable business operations with permanent IDs. The cloud API ingests them idempotently by tenant, terminal, and client-generated ID. Conflict rules belong to individual modules. Completed sales should never be merged by editing historical lines; corrections happen through returns, voids, or adjustment operations.

## Tax Strategy

The tax engine is abstracted from sales. India GST is the first implementation and supports INR, HSN/SAC metadata, inclusive/exclusive tax, CGST, SGST, and IGST split. The core sales engine depends on calculated tax lines, not hard-coded Indian tax assumptions.

## Payment Strategy

Phase 1 supports manual cash, UPI, card, wallet, and account payment records. The domain model stores provider-neutral payment method, amount, reference, status, and metadata. It never stores raw card PAN or CVV data. Future providers such as Razorpay, Pine Labs, Stripe, Adyen, and acquirers plug in behind a payment gateway abstraction.

## Vertical Module Strategy

Retail is the Phase 1 vertical slice. Grocery, restaurant, hotel, pharmacy, liquor, wholesale, and mall modules extend the core through additional entities, screens, policies, and integrations. State-specific excise, prescription, PMS, KDS, fuel pump, and marketplace behavior must not be embedded into Sales or Inventory.

## Deployment Topology

Local development uses the API, web app, and Docker Compose infrastructure for PostgreSQL. Redis is designed as an optional future cache or queue dependency and is not required for basic development.

Production topology starts as:

```
Browser/POS client -> ASP.NET Core API -> PostgreSQL
                         |
                         +-> outbox table
                         +-> optional Redis later
```

Later hybrid topology adds:

```
POS Edge Runtime -> local SQLite -> sync engine -> cloud API -> PostgreSQL
```
