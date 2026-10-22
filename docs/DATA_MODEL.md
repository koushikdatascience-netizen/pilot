# Data Model

## Invariants

- All distributed business identifiers are UUID/GUID values.
- Financial values use `decimal`; floating point values are forbidden for money.
- Product records do not carry stock quantity.
- Historical sale lines store transaction-time snapshots.
- Inventory movements are append-only ledger entries.
- Tenant scope is required for every merchant-owned aggregate and enforced by API queries plus relational constraints.

## Identity And Access

User:

- `Id`, `TenantId`, `DisplayName`, `Email`, `PasswordHash`, `IsActive`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Role:

- `Id`, `TenantId`, `Name`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

UserRole:

- `UserId`, `RoleId`

RolePermission:

- `RoleId`, `Permission`

## Tenancy

Tenant:

- `Id`, `Name`, `Status`, `CreatedAtUtc`

Organization:

- `Id`, `TenantId`, `Name`, `LegalName`, `Gstin`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Business/Brand:

- `Id`, `TenantId`, `OrganizationId`, `Name`, `BusinessType`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Location:

- `Id`, `TenantId`, `OrganizationId`, `BusinessId`, `Name`, `Code`, `Address`, `StateCode`, `DefaultSalesWarehouseId`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Terminal:

- `Id`, `TenantId`, `LocationId`, `Name`, `DeviceCode`, `Status`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Warehouse:

- `Id`, `TenantId`, `LocationId`, `Name`, `Code`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

## Catalog

Category:

- `Id`, `TenantId`, `Name`, `ParentCategoryId`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

TaxCategory:

- `Id`, `TenantId`, `Name`, `CountryCode`, `Rate`, `TaxBehavior`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Product:

- `Id`, `TenantId`, `CategoryId`, `TaxCategoryId`, `Name`, `Sku`, `Barcode`, `HsnSac`
- `UnitOfMeasure`, `PriceAmount`, `Currency`, `IsStockTracked`, `IsActive`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

## Customers

Customer:

- `Id`, `TenantId`, `DisplayName`, `Phone`, `Email`, `Gstin`, `CreditLimit`, `IsWalkIn`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

## Inventory

InventoryTransaction:

- `Id`, `TenantId`, `LocationId`, `WarehouseId`, `ProductId`, `Type`
- `QuantityDelta`, `UnitCost`, `SourceDocumentType`, `SourceDocumentId`, `OccurredAtUtc`, `CreatedByUserId`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

StockBalance:

- `TenantId`, `WarehouseId`, `ProductId`, `QuantityOnHand`
- Read model materialized from ledger movements in queries.

## Sales

Sale:

- `Id`, `TenantId`, `LocationId`, `TerminalId`, `CustomerId`, `InvoiceNumber`, `Status`
- `Currency`, `Subtotal`, `DiscountTotal`, `TaxTotal`, `GrandTotal`, `ClientGeneratedId`
- `CreatedAtUtc`, `UpdatedAtUtc`, `CompletedAtUtc`, `Version`, `SyncStatus`

SaleLine:

- `Id`, `TenantId`, `SaleId`, `ProductId`
- `ProductNameSnapshot`, `SkuSnapshot`, `BarcodeSnapshot`
- `Quantity`, `UnitPrice`, `DiscountAmount`, `TaxableAmount`
- `TaxRate`, `TaxAmount`, `CgstAmount`, `SgstAmount`, `IgstAmount`, `LineTotal`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

Payment:

- `Id`, `TenantId`, `SaleId`, `Method`, `Amount`, `Currency`, `Status`
- `Provider`, `Reference`, `CapturedAtUtc`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

InvoiceSequence:

- `Id`, `TenantId`, `LocationId`, `FinancialPeriod`, `Prefix`, `NextNumber`, `UpdatedAtUtc`, `xmin`

## Audit And Outbox

AuditLog:

- `Id`, `TenantId`, `ActorUserId`, `Action`, `EntityName`, `EntityId`, `TerminalId`
- `OccurredAtUtc`, `BeforeJson`, `AfterJson`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

OutboxEvent:

- `Id`, `TenantId`, `EventType`, `PayloadJson`, `OccurredAtUtc`, `ProcessedAtUtc`, `Attempts`
- `CreatedAtUtc`, `UpdatedAtUtc`, `Version`

## Index Plan

- Tenant ID on every tenant-owned table.
- Unique product SKU per tenant.
- Unique barcode per tenant where barcode is present.
- Unique terminal device code per tenant.
- Unique role name per tenant.
- Unique idempotent sale key by tenant, terminal, and client-generated ID.
- Unique invoice number per tenant and location.
- Unique invoice sequence by tenant, location, financial period, and prefix.
- Sales by tenant, location, and completed timestamp.
- Inventory lookup by tenant, warehouse, and product.
- Customer phone by tenant.

## Milestone 2 Additions

- Location: cashier/manager maximum discount ratios.
- Customer: state code, independent of GSTIN.
- Payment: AmountDue, AmountTendered, ChangeDue; Credit replaces the former Account method.
- Sale: immutable receipt-header JSON captured at completion.
- SaleLine: HSN/SAC snapshot for new transactions.
- Tenant/Id alternate keys and composite foreign keys across tenant-owned relationships.
- Held/Draft sales use the same aggregate; their temporary numbers are replaced by real invoices on completion.
- Upgrade migration backfills legacy payment amounts and initial discount settings without inventing missing historical receipt details.

## Milestone 3 Additions

- TerminalDevice: tenant-owned Id, TerminalId, DeviceId, OfferedSequence, AcknowledgedSequence, LastSeenAtUtc and standard timestamps/version. Unique tenant/device and tenant/terminal pairs prevent silent rebinding. Composite tenant/terminal FK prevents cross-tenant references.
- SyncChange: tenant-owned Id, identity Sequence, EventId, SchemaVersion, EventType and JSON payload. Tenant/sequence and tenant/event are unique; a composite FK references the original outbox event. Sequence gaps are valid. Rows are durable feed records, not an event-sourced business ledger.
- Outbox ProcessedAtUtc means successfully published to the durable feed, not acknowledged by every device. Attempts counts successful publication attempts; failed transactions roll back both publication and this counter.
- SQLite metadata: permanent device UUID, immutable tenant/terminal/server binding and committed cursor.
- SQLite products/taxes: version-aware product JSON and bootstrap tax JSON. No stock authority or customer-profile cache.
- SQLite inbox: event UUID and unique sequence, catalog payload or non-catalog metadata only.
- SQLite sale_queue: immutable UUID/payload, Pending/InFlight/Conflict/Synced state, attempts, retry time, lease expiry, server completion response and error classification. Attempt tokens prevent expired lease owners from replacing newer results.
- Migration: `20261017110655_OfflinePreparation`. No existing sale or ledger rows are rewritten.

## Milestone 4 Additions

- User.AuthVersion: credential/session generation, initialized to 1 for existing accounts. Reset and status transitions increment it. It is distinct from the ordinary optimistic Version token and is not exposed in public user responses.
- Permission enum appends ManageUsers and ViewAudit without renumbering prior values; storage remains string-valued. The migration copies these grants once from existing ManageSettings roles.
- A migration-owned PostgreSQL expression index, `IX_users_NormalizedEmail` on `lower("Email")`, guarantees global email identity for the email-only login contract. It intentionally fails on ambiguous existing accounts instead of merging them. EF's ordinary tenant/email index remains.
- Audit index `(TenantId, OccurredAtUtc, Id)` supports tenant-scoped ordered review. Identity audits retain before/after names, role IDs/permission names and status, but exclude password/hash/token material. PasswordReset records only session revocation metadata.
- Existing user/role Version values protect stale editor submissions. Identity transactions additionally serialize tenant administrators and enforce an active ManageUsers holder after changes. No hard-delete or audit-edit API is introduced.
- Migration: `20261021100216_OperationalHardening`. Financial records and edge data formats remain unchanged.
