You are continuing the Universal Commerce OS repository.

IMPORTANT:
Do NOT begin Milestone 2 feature expansion yet.

We completed an architecture review of Milestone 1 and found that the current implementation is still a demo/in-memory scaffold.

Your task is:

MILESTONE 1.5 — FOUNDATION HARDENING

Read first:
- product-concept.md
- prompt.md
- docs/ARCHITECTURE.md
- docs/DATA_MODEL.md
- docs/PHASE_1.md
- existing backend/frontend/tests
- existing SQL migration

Preserve the good domain design and current UI direction.

Do not rewrite the project unnecessarily.

==================================================
1. REPLACE IN-MEMORY PERSISTENCE
==================================================

Remove CommerceStore as the primary persistence mechanism.

Implement proper PostgreSQL persistence using:

- EF Core
- Npgsql
- DbContext
- entity configurations
- migrations

Use PostgreSQL as the source of truth.

The API must persist data across restarts.

Create an Infrastructure layer if appropriate, for example:

src/backend/
  Pos.Api/
  Pos.Domain/
  Pos.Application/
  Pos.Infrastructure/

Do not create excessive projects if unnecessary.

==================================================
2. PROPER DATABASE MIGRATIONS
==================================================

Replace or migrate away from the hand-maintained SQL-only approach.

Use EF Core migrations.

Ensure the database includes:

Tenant
Organization
Business
Location
Terminal
Warehouse

Role
User
UserRole/role mapping if appropriate

Category
TaxCategory
Product

Customer

Sale
SaleLine
Payment

InventoryTransaction

AuditLog
OutboxEvent

Include appropriate foreign keys, indexes and tenant-scoped uniqueness.

Important:

Business must exist in the database because the domain hierarchy is:

Tenant
→ Organization
→ Business/Brand
→ Location
→ Terminal

If Location should belong to Business as well as Organization, make the relationship explicit and update documentation accordingly.

==================================================
3. MULTI-TENANCY SECURITY
==================================================

The existing model of trusting:

X-Tenant-Id

from arbitrary client input is NOT acceptable.

Implement a TenantContext abstraction.

Tenant identity must ultimately come from authenticated claims/session/API identity.

For development, an explicit development-only tenant resolver may be used, but production paths must not permit arbitrary tenant impersonation.

Every tenant-owned repository/query must be automatically tenant scoped where practical.

Enforce tenant ownership on writes.

For example, it must be impossible for Tenant A to:

- create stock for Tenant B's product
- sell Tenant B's product
- access Tenant B's customer
- use Tenant B's warehouse
- use Tenant B's terminal

Add tests proving these boundaries.

==================================================
4. AUTHENTICATION + RBAC FOUNDATION
==================================================

Implement real authentication.

Use an established secure password hashing solution.

Never use plaintext/dev placeholder hashes in normal application flow.

Implement:

login
authenticated user identity
roles
permissions
authorization policies

At minimum create permissions such as:

ManageCatalog
ManageInventory
CompleteSale
ApplyDiscount
ViewReports
ManageSettings

Protect endpoints appropriately.

Examples:

POST products → ManageCatalog
POST opening stock → ManageInventory
POST sale completion → CompleteSale
discount beyond normal cashier allowance → future approval extension point
reports → ViewReports

Seed users only in development and hash their passwords securely.

==================================================
5. TRANSACTIONAL SALE COMPLETION
==================================================

Sale completion is the most important transaction.

Implement it as a single database transaction.

Inside one transaction:

validate tenant
validate location
validate terminal
validate warehouse
validate customer if provided
load products
load tax configuration
calculate authoritative prices/taxes server-side
validate stock
validate payments
allocate invoice number
persist Sale
persist SaleLines
persist Payments
append InventoryTransactions
append AuditLog
append OutboxEvents
commit

If any part fails:
ROLLBACK EVERYTHING.

Never allow a sale without its inventory movement or vice versa.

==================================================
6. IDEMPOTENCY
==================================================

ClientGeneratedId must actually work.

Unique scope:

TenantId + TerminalId + ClientGeneratedId

If the same POS submits the same completed transaction twice because of retry/network failure:

DO NOT create a duplicate sale.

Return the existing transaction or an idempotent successful response.

Add tests.

==================================================
7. INVENTORY CORRECTNESS
==================================================

Preserve the append-only inventory ledger.

Do NOT add Product.StockQuantity.

Validate:

Product.TenantId
Warehouse.TenantId
Location.TenantId

and relationships.

Do not use one global seeded warehouse.

Determine the warehouse from the sale's location/terminal configuration.

If a location has a configured default sales warehouse, model this explicitly.

Handle duplicate product lines correctly.

Example:

Stock = 5

Sale lines:
Product A qty 3
Product A qty 3

This sale must fail.

Aggregate required stock per product before stock validation.

Use database transaction/concurrency controls so two terminals cannot oversell the final units concurrently.

Document the chosen concurrency strategy.

==================================================
8. FINANCIAL VALIDATION
==================================================

Continue using decimal.

Reject:

negative payment amounts
zero/invalid quantities
negative prices
discount > line gross
invalid tax rates
unsupported currency

Payment amount total must exactly match the sale grand total for current Phase 1 behavior.

Prepare future support for:

split tender
cash change
credit sales

but do not overbuild them now.

==================================================
9. GST FOUNDATION
==================================================

Retain IndiaGstTaxCalculator abstraction.

Do not trust a raw `IntraState` boolean from the POS as the final tax authority.

Calculate GST context using appropriate seller/location/customer state information.

Store enough sale tax snapshot information to reproduce invoices later.

At minimum preserve:

taxable amount
tax rate
total tax
CGST
SGST
IGST

Prefer storing tax breakdown at SaleLine level or a normalized SaleLineTax record.

Historical invoices must remain correct if the product's tax configuration changes later.

==================================================
10. INVOICE NUMBERING
==================================================

Remove in-process `_invoiceSequence`.

Invoice numbers must be concurrency-safe and persistent.

Scope numbering appropriately, likely:

Tenant + Location + financial period/series

Implement a safe invoice sequence mechanism.

Requirements:

- no duplicate invoice numbers
- survives API restart
- concurrent terminals safe
- auditable
- configurable prefix/series later

==================================================
11. AUDIT LOG
==================================================

Persist AuditLog.

Create audit records for at least:

sale completion
inventory adjustment/opening stock
product creation/update
customer creation/update

Design extension points for:

discount
void
refund
role/permission change
cash drawer actions

Do not log secrets.

==================================================
12. OUTBOX
==================================================

Persist OutboxEvent in the same database transaction as the business action.

Events should include at least:

ProductCreated
CustomerCreated
StockAdjusted
SaleCompleted
PaymentRecorded
InventoryReduced

Do not add Kafka/RabbitMQ.

A future worker may publish/process events later.

==================================================
13. REAL OPENAPI
==================================================

Remove the manually maintained OpenApiDocumentFactory if practical.

Use ASP.NET Core's proper OpenAPI generation.

Expose:

/openapi/v1.json

or equivalent Swagger/OpenAPI document.

Make schemas accurate for:

request DTOs
response DTOs
validation errors
enums
ProblemDetails

==================================================
14. DTO + APPLICATION LAYER
==================================================

Do not expose EF entities directly.

Use request/response DTOs.

Move major business logic out of Program.cs.

Program.cs should be composition/routing, not the core POS application service.

Create application services/handlers such as:

CompleteSaleService
InventoryService
ProductService
CustomerService

Avoid giant controllers and giant services.

==================================================
15. VALIDATION
==================================================

Introduce structured validation.

FluentValidation is preferred if package availability permits.

Validate:

required strings
SKU
barcode format where appropriate
GSTIN shape where appropriate
quantities
prices
credit limit
email
tax rates
foreign IDs
tenant ownership

Use ProblemDetails-compatible responses.

Do not expose raw internal exception messages to clients.

==================================================
16. READINESS
==================================================

/health

should prove the process is alive.

/health/ready

must actually check PostgreSQL connectivity.

Do NOT return hardcoded:

database = "postgres-configured"

A broken database should cause readiness failure.

==================================================
17. FRONTEND → REAL API
==================================================

The current POS frontend is hardcoded.

Replace hardcoded product inventory as the authoritative source.

Connect the POS to the backend.

Flow must be:

load products/API
search or scan barcode
add cart
edit quantity
discount
select customer
choose tender
submit sale to API
receive server-authoritative totals/invoice
show receipt
refresh stock

The frontend may calculate estimated totals for instant UX,
but SERVER CALCULATIONS ARE AUTHORITATIVE.

Do not trust totals sent by the browser.

The frontend should send:

product ID
quantity
discount request
customer
payment tender
client-generated transaction ID

The server determines authoritative:

price
tax
tax breakdown
totals
invoice
inventory mutation

==================================================
18. BACK OFFICE
==================================================

Make Products functional through API.

Minimum:

list products
create product

Make Customers functional:

list
create

Make Inventory functional:

balances
opening stock / adjustment

Dashboard should consume:

GET /reports/today

Do not build Purchases, Suppliers, Hotel, Restaurant, Pharmacy, Liquor etc. yet.

==================================================
19. TESTING
==================================================

Use a proper test framework.

Prefer xUnit.

Replace the custom console-based domain test runner if appropriate.

Required unit tests:

Money
inclusive GST
exclusive GST
CGST/SGST
IGST
sale totals
discount limits
payment totals
duplicate product line stock handling
inventory balance

Required integration tests:

authentication
tenant isolation
create product
create customer
opening stock
complete sale
sale persists
inventory deducts
outbox created
audit created
duplicate ClientGeneratedId is idempotent
insufficient inventory rejects entire transaction
invalid tenant references reject
invoice uniqueness

Use a disposable PostgreSQL testing strategy where practical.

==================================================
20. DEVELOPMENT SEED
==================================================

Keep the Phase 1 demo data concept.

Seed only in Development.

Demo:

Demo Retail Pvt Ltd
Kalyani Main Store
Front Counter 1
Main Store Warehouse

Products:

Rice 5kg
Milk 1L
Premium T-Shirt
Shampoo 180ml

walk-in customer
one named customer
GST categories
opening inventory

Seed must be idempotent.

==================================================
21. CONFIGURATION
==================================================

Move secrets/passwords out of appsettings.json.

Use environment variables.

.env.example may show safe placeholders.

Do not commit real credentials.

Docker Compose may provide development values.

==================================================
22. FRONTEND DEPENDENCY QUALITY
==================================================

Do not use `"latest"` for all production dependencies.

Pin sensible versions.

Use:

Next.js
React
TypeScript

Add TanStack Query and React Hook Form/Zod if actually needed.

Avoid unnecessary libraries.

==================================================
23. RUN EVERYTHING
==================================================

Before finishing:

run backend restore
run backend build
run backend tests

start PostgreSQL
apply EF migrations

run frontend install
run frontend tests
run frontend build

If possible run the API and perform a smoke test.

Fix failures instead of only documenting them.

==================================================
MILESTONE 1.5 ACCEPTANCE TEST
==================================================

The following must work end-to-end:

1. PostgreSQL starts.

2. Migrations apply.

3. Development seed exists.

4. User logs in.

5. User retrieves products.

6. User creates a product.

7. User retrieves stock.

8. POS loads real products.

9. Barcode/SKU can add product.

10. POS submits sale.

11. Server validates stock.

12. Server calculates GST.

13. Server calculates total.

14. Payment is recorded.

15. Sale is persisted.

16. Inventory ledger is reduced.

17. Audit log is created.

18. Outbox records are created.

19. Invoice number is persistent and unique.

20. Re-submitting same ClientGeneratedId does not duplicate sale.

21. Restart API.

22. Sale and inventory remain present.

23. Another tenant cannot access those records.

24. Frontend production build succeeds.

25. Backend tests and integration tests pass.

==================================================
DO NOT BUILD YET
==================================================

Do NOT add:

restaurant KDS
hotel PMS
pharmacy workflow
liquor excise
Swiggy
Zomato
AI assistant
mobile app
Tauri
SQLite sync
Kafka
RabbitMQ
microservices
Kubernetes
ClickHouse
advanced ERP
purchases
supplier management

unless required purely as a tiny extension point.

==================================================
FINAL REPORT
==================================================

When finished report:

1. Files/folders added or changed
2. Architecture changes
3. Database schema/migrations
4. Authentication design
5. Tenant isolation design
6. Sale transaction design
7. Inventory concurrency strategy
8. Invoice sequence strategy
9. API endpoints
10. Frontend integration
11. Tests added
12. Exact test/build output
13. Exact local run commands
14. Known limitations
15. Whether every Milestone 1.5 acceptance criterion passed

Do not call the task complete if builds or tests fail.