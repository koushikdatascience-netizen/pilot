# PILOT POS — MILESTONE 2

Product:

**Pilot POS — One POS for every business.**

You are continuing the existing repository:

`koushikdatascience-netizen/pilot`

Do NOT rebuild the project from scratch.

Read first:

* `product-concept.md`
* `prompt.md`
* `README.md`
* `docs/ARCHITECTURE.md`
* `docs/DATA_MODEL.md`
* `docs/PHASE_1.md`
* all current backend projects
* current EF migrations
* current frontend
* current tests

The existing Milestone 1.5 architecture is generally approved.

Preserve:

* modular monolith
* PostgreSQL + EF Core
* tenant isolation
* bearer authentication
* RBAC
* append-only inventory ledger
* server-authoritative calculations
* transactional sale completion
* UUID identifiers
* decimal money
* GST tax snapshots
* audit log
* transactional outbox
* idempotency
* API-backed POS

Do not introduce microservices.

==================================================
PHASE A — CLOSE REMAINING MILESTONE 1.5 GAPS
============================================

Before adding substantial new features, harden the current sale engine.

## 1. REAL POSTGRESQL INTEGRATION TESTS

The current integration tests use EF InMemory.

That is insufficient for critical POS transaction testing.

Add PostgreSQL-backed integration tests.

Prefer Testcontainers for PostgreSQL if practical.

Otherwise use an isolated Docker PostgreSQL test database.

Tests must exercise actual:

* Npgsql
* EF migrations
* foreign keys
* unique indexes
* serializable transactions
* rollback
* concurrency tokens

Do NOT replace all tests with mocks.

Keep fast unit tests separately.

Required PostgreSQL integration scenarios:

1. login
2. tenant isolation
3. create product
4. opening inventory
5. sale completion
6. payment persistence
7. inventory deduction
8. audit creation
9. outbox creation
10. idempotent sale replay
11. restart/new DbContext still sees persisted sale
12. invalid tenant reference rejection
13. rollback on insufficient stock
14. invoice uniqueness
15. concurrent terminal sale behavior

==================================================
2. TRANSACTION RETRY STRATEGY
=============================

Pilot POS must survive normal database concurrency.

Current sale completion uses SERIALIZABLE isolation.

Keep strong consistency.

Add a bounded retry strategy for transient PostgreSQL concurrency failures such as:

* serialization failure
* deadlock where applicable

Do NOT retry:

* validation errors
* insufficient stock
* forbidden operations
* invalid payments

Use a small bounded retry count.

Log retries with correlation context.

After retry exhaustion return an appropriate conflict/service response instead of raw database details.

==================================================
3. CONCURRENT STOCK PROTECTION
==============================

Prove this scenario:

Stock = 1

Terminal A sells quantity 1.
Terminal B simultaneously sells quantity 1.

Required result:

EXACTLY ONE sale completes.

The other request must receive a clean conflict response.

Inventory must never become negative.

No partial:

* sale
* payment
* invoice
* audit
* inventory transaction

may remain for the rejected transaction.

Write a real PostgreSQL concurrency integration test.

==================================================
4. CONCURRENT INVOICE ALLOCATION
================================

Invoice generation must survive many simultaneous terminals.

Keep location/period scoped numbering.

Example:

KAL-202609-0001
KAL-202609-0002
KAL-202609-0003

Run multiple parallel successful sales.

Prove:

* every invoice unique
* no duplicate sequence
* no permanent transaction failure from sequence creation race
* restart does not reset numbering

Prefer a database-safe approach.

Do not use process-local counters.

==================================================
5. DISCOUNT AUTHORIZATION
=========================

Discount authorization must be enforced server-side.

Introduce useful policy boundaries such as:

* cashier may apply discounts up to configured threshold
* larger discount requires `ApplyDiscount` permission

For now a simple tenant/location setting is acceptable.

Example:

Normal cashier:
maximum manual discount = 10%

Manager with ApplyDiscount:
may exceed cashier threshold up to business-defined maximum.

Never rely only on UI restrictions.

Add tests.

==================================================
6. PAYMENT MODEL IMPROVEMENT
============================

Enhance Phase 1 payment handling.

Support:

* Cash
* UPI
* Card
* Wallet
* Credit

Support split tender.

Example:

Invoice = ₹1,000

Cash ₹400
UPI ₹600

For CASH support:

AmountDue
AmountTendered
ChangeDue

Example:

Total = ₹680
Customer gives ₹700
Change = ₹20

Never create negative payment/change values.

Non-cash payments should normally not create change.

Preserve payment snapshots.

No live payment gateway yet.

==================================================
7. CUSTOMER / PLACE OF SUPPLY FOUNDATION
========================================

Do not determine GST state exclusively from GSTIN.

Extend customer/address/localization foundation with appropriate state information.

For India localization maintain:

* customer state code
* seller location state code
* GSTIN if available

Determine:

same state → CGST + SGST
different state → IGST

Do not hardcode India-specific concepts into the universal core where an abstraction is appropriate.

Keep India localization cleanly separated.

==================================================
PHASE B — COMPLETE RETAIL POS ENGINE
====================================

Milestone 2's main target is:

A production-quality retail checkout experience.

Flow:

LOGIN
→ SELECT TERMINAL
→ SCAN/SEARCH
→ CART
→ CUSTOMER
→ DISCOUNTS
→ TAX
→ PAYMENT
→ COMPLETE
→ RECEIPT
→ INVENTORY
→ SALE HISTORY

==================================================
8. POS SESSION / TERMINAL CONTEXT
=================================

The frontend currently assumes the seeded terminal.

Improve terminal/session foundation.

After login establish:

Tenant
Organization
Business
Location
Terminal
Warehouse

The user should operate within a valid terminal context.

Persist safe terminal selection locally when appropriate.

Do not trust arbitrary terminal IDs posted by clients without verifying user/location access.

Prepare for future device registration but do NOT build complex device management yet.

==================================================
9. BARCODE-FIRST CHECKOUT
=========================

Optimize for physical retail.

A USB barcode scanner behaves like keyboard input.

Required:

scan barcode
→ Enter
→ product instantly enters cart
→ input remains focused

Repeated scan:

same product scanned again
→ quantity increments

Unknown barcode:

show clear non-blocking error
→ keep scanner ready

Support:

barcode
SKU
product-name search

POS must be keyboard-first.

Avoid unnecessary animations.

==================================================
10. PRODUCT SEARCH PERFORMANCE
==============================

Current product list loading is acceptable for demos but Pilot POS must eventually handle tens of thousands of products.

Implement scalable API search:

GET /api/v1/products?q=
GET /api/v1/products?barcode=
GET /api/v1/products?sku=

Support pagination.

Create appropriate indexes.

For exact barcode scan use efficient exact lookup rather than downloading the entire catalog.

The UI may cache recent/favorite products.

==================================================
11. CART ENGINE
===============

Support:

* add product
* remove product
* change quantity
* line discount
* sale-level discount if design supports it
* customer selection
* hold transaction
* resume held transaction
* clear cart

Keep server as authority for final totals.

Frontend calculations are estimates only.

==================================================
12. HELD SALES
==============

Implement persisted Held/Draft transactions.

Use cases:

customer forgot wallet
cashier serves next customer
later resume first cart

Required:

Hold Sale
→ persisted server-side
→ inventory is NOT permanently deducted yet unless explicit reservation architecture is introduced later

Resume Sale
→ restore cart
→ complete normally

Support:

Draft
Held
Completed

Do not implement Void/Refund deeply yet unless needed for state-transition correctness.

==================================================
13. RECEIPT MODEL
=================

Build a proper receipt DTO/view.

Include:

Pilot POS branding
business/legal name
location
address
GSTIN
invoice number
date/time
terminal
cashier
customer
items
quantity
rate
discount
taxable amount
CGST
SGST
IGST
total tax
grand total
payment breakdown
amount tendered
change
HSN/SAC where applicable

Frontend should have:

receipt preview
Print button

Add browser print CSS for:

58mm
80mm

Do not integrate native printer drivers yet.

==================================================
14. SALES HISTORY
=================

Upgrade sales history.

Support:

search by invoice
date range
customer
payment method
terminal

Pagination required.

Sale detail screen should show immutable transaction snapshot.

Do NOT rebuild old invoice values from current product records.

==================================================
15. CUSTOMER WORKFLOW
=====================

Inside POS allow quick customer:

search
select
create

Search by:

name
phone

Customer creation should be fast enough for cashier workflow.

Walk-in customer remains supported.

==================================================
16. PRODUCT MANAGEMENT
======================

Make product back office practical.

Required:

list
search
pagination
create
edit
activate/deactivate

Fields:

name
SKU
barcode
category
HSN/SAC
unit
sale price
tax category
stock tracked

Validation server-side.

Do not allow SKU/barcode conflicts inside tenant.

==================================================
17. INVENTORY UI
================

Provide useful stock view:

Product
SKU
Warehouse
On Hand
Low Stock state

Support manual stock adjustment:

AdjustmentIncrease
AdjustmentDecrease
Damage

Opening stock remains available for setup.

Every adjustment must:

* require authorization
* create InventoryTransaction
* create AuditLog
* create OutboxEvent

Never directly overwrite stock.

==================================================
18. DASHBOARD
=============

Improve retail dashboard using real APIs.

Minimum:

Today's Revenue
Transactions
Average Order Value
Items Sold
Top Products
Low Stock
Payment Mix

Do not add fake metrics.

If there is no data, show empty state.

==================================================
19. CORRELATION + OBSERVABILITY
===============================

Add request/correlation IDs.

Structured logs should include where available:

CorrelationId
TenantId
UserId
LocationId
TerminalId
SaleId

Do not log:

passwords
tokens
payment secrets

Keep `/health`
and `/health/ready`.

==================================================
20. SECURITY HARDENING
======================

Review current custom authentication implementation.

For Milestone 2, prefer moving toward standard ASP.NET Core JWT bearer infrastructure if doing so can be done cleanly without destabilizing the application.

At minimum ensure:

* cryptographically strong signing key
* token expiration
* no default production key
* password hash verification
* login rate limiting
* disabled users rejected
* authorization server-side
* tenant claims cannot be supplied by request headers
* no raw exception/database details exposed

Do not implement OAuth/social login yet.

==================================================
21. TENANT RELATIONAL INTEGRITY
===============================

Review cross-tenant relational integrity.

Application validation must ensure related entities all belong to the same tenant.

Pay special attention to:

UserRole
RolePermission
Organization
Business
Location
Terminal
Warehouse
Product
TaxCategory
Category
Customer
Sale

Where useful use composite constraints or explicit application validation.

Do not allow accidental cross-tenant FK relationships.

==================================================
22. PILOT POS BRANDING
======================

Change customer-facing product identity to:

Pilot POS

Tagline:

One POS for every business.

Replace user-facing:

Universal Commerce OS
Universal Commerce
Demo Commerce

where those are product-brand labels.

Do NOT unnecessarily rename internal namespaces such as:

Pos.Domain
Pos.Application
Pos.Infrastructure

Those internal names are fine.

Update:

page metadata
login
back office
POS header
receipt
README product title where appropriate

Keep architecture documents technically descriptive.

==================================================
23. FRONTEND ARCHITECTURE
=========================

The current frontend is growing.

Do not leave the full dashboard or POS as giant single-page components.

Split responsibly into reusable components such as:

PosShell
ProductSearch
ProductGrid
Cart
CartLine
CustomerSelector
PaymentDialog
ReceiptView
SaleHistory
DashboardCards

Use hooks/services for API/state logic.

Do not over-componentize tiny markup.

==================================================
24. UX STANDARD
===============

Pilot POS should feel like a serious commercial POS.

Priorities:

1. speed
2. clarity
3. keyboard efficiency
4. low cognitive load
5. reliability

Target screens:

1366×768
1920×1080
touch tablet

Cashier should not need decorative animations.

Use:

clean typography
compact spacing
clear totals
large payment targets
strong focus states
accessible contrast
loading states
error states
empty states

==================================================
25. TESTING
===========

Required unit tests:

* inclusive GST
* exclusive GST
* CGST/SGST
* IGST
* discount calculations
* discount authorization
* split payments
* cash change
* cart totals
* inventory quantities

Required PostgreSQL integration tests:

* authentication
* permission enforcement
* tenant isolation
* product CRUD
* customer flow
* opening stock
* adjustment
* sale completion
* transaction rollback
* idempotency
* audit
* outbox
* invoice uniqueness
* parallel invoice allocation
* parallel last-item sale
* held sale
* resume and complete held sale

Frontend tests for critical calculations/workflows where useful.

==================================================
26. E2E TEST
============

Add a Playwright E2E test once the application is stable.

Scenario:

login
→ POS
→ scan/search Milk 1L
→ add quantity
→ choose customer
→ cash payment
→ enter tendered amount
→ complete sale
→ receipt appears
→ verify invoice number
→ go to sales history
→ verify sale
→ go to inventory
→ verify stock decreased

Do NOT spend excessive time building a huge E2E suite yet.

One strong golden-path E2E is enough.

==================================================
27. CI
======

Add GitHub Actions CI.

On pull request/push run:

backend restore
backend build
domain tests
PostgreSQL integration tests
frontend npm ci
frontend tests
frontend build

Use a PostgreSQL service container where needed.

No deployment workflow yet unless trivial.

A broken build must fail CI.

==================================================
DO NOT BUILD YET
================

Do NOT start:

Restaurant KDS
Hotel PMS
Pharmacy
Liquor Excise
Swiggy
Zomato
AI assistant
Mobile application
Tauri desktop package
Offline SQLite sync
Kafka
RabbitMQ
Microservices
Kubernetes
ClickHouse
Accounting ERP
Purchase management
Supplier management
Advanced refund workflow
Self checkout

We will build these after the retail core is excellent.

==================================================
MILESTONE 2 ACCEPTANCE FLOW
===========================

Pilot POS must demonstrate:

1. Start PostgreSQL.
2. Apply migrations.
3. Start API.
4. Start web application.
5. Login.
6. Establish terminal context.
7. Find product by barcode.
8. Add product to cart.
9. Add/change quantity.
10. Select/create customer.
11. Apply authorized discount.
12. Select Cash/UPI/Card or split tender.
13. Complete sale.
14. Server calculates authoritative GST/totals.
15. Persistent unique invoice created.
16. Payment persisted.
17. Inventory ledger reduced.
18. Audit created.
19. Outbox created.
20. Receipt displayed and printable.
21. Sale visible in history.
22. Stock reflects reduction.
23. Retry same ClientGeneratedId → no duplicate.
24. Two terminals cannot oversell final unit.
25. Concurrent invoice generation remains unique.
26. Restart API → everything remains.
27. Tenant B cannot read Tenant A data.
28. Backend build passes.
29. All backend tests pass.
30. Frontend tests pass.
31. Frontend production build passes.
32. GitHub Actions CI passes.

==================================================
FINAL EXECUTION RULE
====================

Do not merely write architecture documents.

Actually implement the milestone.

Work in this order:

1. inspect current repo
2. harden PostgreSQL tests/concurrency
3. update migrations
4. improve application services
5. improve APIs
6. improve frontend POS
7. improve back office
8. add tests
9. add CI
10. run everything
11. fix failures
12. update documentation

Do not declare completion while builds/tests are failing.

==================================================
FINAL REPORT
============

At completion report:

1. architecture changes
2. files added/changed
3. database migrations
4. API endpoints added/changed
5. concurrency strategy
6. invoice strategy
7. inventory strategy
8. payment model
9. discount authorization model
10. GST/place-of-supply changes
11. POS UX changes
12. back-office changes
13. tests added
14. PostgreSQL concurrency test results
15. frontend test/build results
16. backend build/test results
17. GitHub Actions workflow
18. exact local run commands
19. known limitations
20. which Milestone 2 acceptance criteria passed

Commit the completed implementation with a clear commit message such as:

`Complete Pilot POS milestone 2 retail core`

Do not start vertical modules beyond Retail.
