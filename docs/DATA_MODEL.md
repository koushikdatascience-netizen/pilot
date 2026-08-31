# Initial Data Model

## Invariants

- All distributed business identifiers are UUID/GUID values.
- Financial values use `decimal`; floating point values are forbidden for money.
- Product records do not carry stock quantity.
- Historical sale lines store transaction-time snapshots.
- Inventory movements are append-only ledger entries.
- Tenant scope is required for every merchant-owned aggregate.

## Tenancy

Tenant:

- `Id`
- `Name`
- `Status`
- `CreatedAtUtc`

Organization:

- `Id`
- `TenantId`
- `Name`
- `LegalName`
- `Gstin`

Business/Brand:

- `Id`
- `TenantId`
- `OrganizationId`
- `Name`
- `BusinessType`

Location:

- `Id`
- `TenantId`
- `OrganizationId`
- `Name`
- `Code`
- `Address`
- `StateCode`

Terminal:

- `Id`
- `TenantId`
- `LocationId`
- `Name`
- `DeviceCode`
- `Status`

Warehouse:

- `Id`
- `TenantId`
- `LocationId`
- `Name`
- `Code`

## Catalog

Category:

- `Id`
- `TenantId`
- `Name`
- `ParentCategoryId`

TaxCategory:

- `Id`
- `TenantId`
- `Name`
- `CountryCode`
- `Rate`
- `TaxBehavior`

Product:

- `Id`
- `TenantId`
- `CategoryId`
- `TaxCategoryId`
- `Name`
- `Sku`
- `Barcode`
- `HsnSac`
- `UnitOfMeasure`
- `Price`
- `Currency`
- `IsStockTracked`
- `IsActive`

## Customers

Customer:

- `Id`
- `TenantId`
- `DisplayName`
- `Phone`
- `Email`
- `Gstin`
- `CreditLimit`
- `IsWalkIn`

## Inventory

InventoryTransaction:

- `Id`
- `TenantId`
- `LocationId`
- `WarehouseId`
- `ProductId`
- `Type`
- `QuantityDelta`
- `UnitCost`
- `SourceDocumentType`
- `SourceDocumentId`
- `OccurredAtUtc`
- `CreatedByUserId`

StockBalance:

- `TenantId`
- `WarehouseId`
- `ProductId`
- `QuantityOnHand`
- materialized from ledger movements

## Sales

Sale:

- `Id`
- `TenantId`
- `LocationId`
- `TerminalId`
- `CustomerId`
- `InvoiceNumber`
- `Status`
- `Currency`
- `Subtotal`
- `DiscountTotal`
- `TaxTotal`
- `GrandTotal`
- `ClientGeneratedId`
- `CreatedAtUtc`
- `CompletedAtUtc`
- `Version`
- `SyncStatus`

SaleLine:

- `Id`
- `TenantId`
- `SaleId`
- `ProductId`
- `ProductNameSnapshot`
- `SkuSnapshot`
- `BarcodeSnapshot`
- `Quantity`
- `UnitPrice`
- `DiscountAmount`
- `TaxRate`
- `TaxAmount`
- `LineTotal`

Payment:

- `Id`
- `TenantId`
- `SaleId`
- `Method`
- `Amount`
- `Currency`
- `Status`
- `Provider`
- `Reference`
- `CapturedAtUtc`

## Audit And Outbox

AuditLog:

- `Id`
- `TenantId`
- `ActorUserId`
- `Action`
- `EntityName`
- `EntityId`
- `TerminalId`
- `OccurredAtUtc`
- `BeforeJson`
- `AfterJson`

OutboxEvent:

- `Id`
- `TenantId`
- `EventType`
- `PayloadJson`
- `OccurredAtUtc`
- `ProcessedAtUtc`
- `Attempts`

## Index Plan

- Tenant ID on every tenant-owned table.
- Unique product SKU per tenant.
- Unique barcode per tenant where barcode is present.
- Sales by tenant, location, and completed timestamp.
- Invoice number unique per tenant and location.
- Inventory lookup by tenant, warehouse, and product.
- Customer phone by tenant.
