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
