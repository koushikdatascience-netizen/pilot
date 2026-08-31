You are the principal software architect and senior full-stack engineer for this project.

We are building a production-grade, commercial **Universal Commerce OS / POS platform** intended to eventually support:

* Retail shops
* Grocery / supermarkets
* Restaurants / cafés / QSR
* Hotels
* Pharmacy
* Liquor / FL shops
* Multi-store chains
* Mall / multi-tenant businesses
* Wholesale / distribution
* Additional vertical packs later

## SOURCE OF TRUTH

Before writing or modifying application code, inspect the entire repository.

You MUST carefully read:

1. `project_concept.md`
2. The POS market/research PDF included in this repository
3. Any other existing architecture, requirements, README, markdown, database, or configuration files

Treat those documents as the primary business/product requirements.

Do not blindly implement everything in the research report. Use it as market and architectural guidance.

---

# PRIMARY ARCHITECTURAL PRINCIPLE

This is NOT a collection of separate POS applications.

We are building:

**ONE COMMERCE CORE + CONFIGURABLE VERTICAL MODULES**

The same foundational system should eventually support a small shop and a large multi-location business without maintaining separate duplicated codebases.

Architecture must support:

Organization
→ Business / Brand
→ Locations
→ Terminals
→ Warehouses

Optional vertical extensions should plug into the common commerce core.

Examples:

Retail
→ standard commerce core

Restaurant
→ Tables
→ KOT
→ Kitchen
→ Modifiers
→ Recipes

Hotel
→ Rooms
→ Reservations
→ Folios
→ Room posting

Pharmacy
→ Medicines
→ Batches
→ Expiry
→ Prescription references

Liquor
→ Excise items
→ Bottle stock
→ Indents
→ state-specific excise adapters

DO NOT tightly couple any state-specific excise rules or any vertical-specific behavior into the common Sales or Inventory modules.

---

# IMPORTANT PRODUCT PRINCIPLES

The application must eventually be:

* Offline-first
* Cloud-managed
* Multi-tenant
* Multi-location
* API-first
* Hardware-neutral
* Payment-provider-neutral
* Modular
* Secure
* Auditable
* Scalable
* Fast enough for real checkout environments

The POS checkout experience must prioritize transaction speed over unnecessary animation.

A cashier should normally complete a standard barcode sale in very few interactions.

---

# TECHNOLOGY DECISION

Unless the existing repository already contains a clearly better justified stack, use:

## Backend

* .NET 10
* ASP.NET Core Web API
* EF Core
* PostgreSQL
* Redis abstraction prepared but Redis should not be required for basic local development
* FluentValidation or equivalent validation pattern
* Serilog structured logging
* ProblemDetails
* OpenAPI
* health endpoints

Use a **modular monolith**, NOT microservices for V1.

However, module boundaries must be strict enough that individual domains could later be extracted.

## Web Back Office

* Next.js
* TypeScript
* React
* Tailwind CSS
* shadcn/ui or similarly maintainable component primitives
* TanStack Query
* React Hook Form
* Zod

Do not create an unnecessarily flashy UI.

Back-office design should feel premium, modern, enterprise-grade and information-dense.

## POS Client

For this stage, create the POS interface as part of the web frontend architecture but isolate POS-specific components and domain logic.

Structure it so the POS frontend could later be packaged using Tauri or another desktop shell.

Do NOT start Tauri packaging yet unless the repository already contains it.

## Local Offline Runtime

Plan for:

* SQLite local database
* local transaction outbox
* local sync metadata
* device/terminal identity
* eventual synchronization with the cloud API

Do not attempt the full production synchronization engine in this first phase.

But architecture and identifiers must already support it.

---

# ID STRATEGY

Do not use sequential database IDs as distributed business identifiers.

Use UUID / GUID identifiers for:

* Tenant
* Organization
* Location
* Terminal
* Warehouse
* Product
* Customer
* Supplier
* Sale
* SaleLine
* Payment
* Inventory transaction
* Purchase
* Sync event

Internal numeric keys may be used only if technically justified, but globally unique business identifiers must exist.

This is required for future offline synchronization.

---

# MONEY

Never use floating point values for financial amounts.

Use decimal/numeric types.

Define consistent rules for:

* currency
* quantities
* tax calculations
* unit price
* discounts
* rounding
* invoice totals

Design the money model carefully before implementation.

---

# INVENTORY RULE

Do NOT store inventory simply as:

Product.StockQuantity

Inventory must be location/warehouse aware.

Use a proper inventory-ledger approach.

Think in concepts such as:

InventoryTransaction

Types may include:

* OpeningStock
* PurchaseReceipt
* Sale
* SaleReturn
* PurchaseReturn
* TransferOut
* TransferIn
* AdjustmentIncrease
* AdjustmentDecrease
* Damage
* Wastage

Stock balance can be calculated/materialized from ledger movements.

Architecture must support auditability.

---

# SALES RULE

A Sale should contain immutable transactional snapshots where necessary.

Do not depend on the current Product record to reconstruct historical invoices.

SaleLine should preserve transaction-time values including appropriate fields such as:

* product identifier
* product name snapshot
* SKU/barcode snapshot
* quantity
* unit price
* tax
* discount
* total

When reasonable.

Design this professionally.

---

# MULTI-TENANCY

Every applicable business record must be tenant scoped.

Prevent cross-tenant access at the architecture level.

Do not merely rely on frontend filtering.

Tenant isolation must be enforced on the server.

Design tenant context and query filtering carefully.

---

# AUDITING

Important operations must eventually be auditable.

Prepare an AuditLog model or auditing abstraction supporting things such as:

* actor
* action
* entity
* entity ID
* timestamp
* tenant
* terminal
* before/after metadata where appropriate

Particularly important eventually for:

* discounts
* voids
* refunds
* stock adjustments
* permission changes
* cash drawer actions

---

# FIRST DEVELOPMENT PHASE

DO NOT BUILD ALL VERTICALS NOW.

For Phase 1, build the common platform foundation and the **Retail POS vertical slice**.

The first milestone should demonstrate:

Admin / Back Office
→ create organization/location
→ create category/product
→ create customer
→ configure tax
→ initialize inventory
→ open POS screen
→ scan/search/add product
→ change quantity
→ optional discount
→ select customer
→ calculate totals
→ choose payment method
→ complete sale
→ deduct inventory
→ create immutable completed sale
→ display receipt
→ view transaction in sales history

This full vertical slice must work.

---

# PHASE 1 MODULES

Create clear modules/bounded contexts such as:

## Identity

* users
* roles
* permissions
* authentication structure

For development, simple secure authentication can be implemented.

Do NOT spend the first iteration integrating external enterprise identity providers.

## Tenancy

* Tenant
* Organization
* Location
* Terminal
* Warehouse

## Catalog

* Product
* ProductVariant if justified
* Category
* Barcode
* UnitOfMeasure
* TaxCategory
* Pricing

Keep V1 manageable but future-proof.

## Customers

* Customer
* customer contact details
* optional GST/tax ID
* store credit fields can be architectural placeholders if not implemented yet

## Inventory

* Warehouse
* InventoryTransaction
* StockBalance/read model
* stock adjustment
* opening stock

## Sales

* Sale
* SaleLine
* Payment
* PaymentMethod
* discount
* taxes
* status

Possible statuses:

* Draft
* Held
* Completed
* Voided
* Refunded

Design carefully.

## Reporting

Only basic reporting initially:

* today's sales
* transaction count
* average order value
* top-selling products
* low-stock products

Do not build a data warehouse yet.

---

# INDIA-FIRST LOCALIZATION

The architecture must be global-ready, but initial implementation should work well for India.

Prepare support for:

* INR
* GST
* CGST
* SGST
* IGST
* GSTIN fields
* HSN/SAC metadata
* inclusive/exclusive tax
* invoice numbering

Do not hard-code Indian tax assumptions directly into the core transaction engine.

Implement India as the first tax/localization implementation behind appropriate abstractions.

Do NOT attempt live GST e-invoice API integration in this first milestone.

---

# PAYMENT MODEL

Payment methods initially:

* Cash
* UPI
* Card
* Wallet
* Credit / On Account if architecture allows

No live payment gateway is required yet.

Payment interfaces must nevertheless be designed so providers such as:

* Razorpay
* Pine Labs
* Stripe
* Adyen
* other acquirers

can later be added without rewriting Sales.

Never store raw card PAN/CVV data.

---

# OFFLINE-FIRST PREPARATION

Even though full synchronization is not being implemented in Phase 1, establish the foundations now.

Completed local transactions should eventually be represented as immutable local business operations.

Prepare concepts such as:

* DeviceId / TerminalId
* ClientGeneratedId
* CreatedAtUtc
* UpdatedAtUtc where appropriate
* Version
* SyncStatus if useful
* OutboxEvent

Create an Outbox pattern in the server architecture.

Avoid architectures that will make offline synchronization impossible later.

---

# API DESIGN

Version APIs.

Example:

`/api/v1/...`

Use REST for the initial API.

Suggested groups:

* `/api/v1/auth`
* `/api/v1/organizations`
* `/api/v1/locations`
* `/api/v1/terminals`
* `/api/v1/products`
* `/api/v1/categories`
* `/api/v1/customers`
* `/api/v1/inventory`
* `/api/v1/sales`
* `/api/v1/payments`
* `/api/v1/reports`

Use proper:

* request DTOs
* response DTOs
* validation
* pagination
* filtering
* ProblemDetails
* HTTP status codes

Do not expose EF Core entities directly through controllers.

---

# DOMAIN EVENTS

Prepare internal domain/integration events such as:

* ProductCreated
* StockAdjusted
* SaleCreated
* SaleCompleted
* PaymentRecorded
* InventoryReduced
* CustomerCreated

Do not introduce Kafka/RabbitMQ in Phase 1.

Use an in-process abstraction and transactional outbox so an external broker can be introduced later.

---

# DATABASE

Use PostgreSQL for cloud/server development.

Create proper migrations.

Indexes must be planned for common operations such as:

* tenant
* product search
* SKU
* barcode
* sales date
* location
* customer phone
* invoice number
* inventory lookup

Composite uniqueness should respect tenant boundaries.

For example, barcode uniqueness may need to be scoped appropriately rather than blindly global.

---

# POS UX

Build a professional checkout screen.

Recommended layout:

Left / center:

* product search
* category shortcuts
* product results / favorites

Right:

* current cart

Cart should display:

* item
* qty
* price
* discount
* line total

Footer/order summary:

* subtotal
* discount
* tax
* grand total

Primary actions:

* Cash
* UPI
* Card

Secondary actions:

* Customer
* Hold
* Discount
* More

Support barcode keyboard-wedge scanners.

A scanner should be able to input barcode digits + Enter and immediately add the item.

Keyboard-first workflows are important.

Prepare hotkeys where appropriate.

Responsive design should work on:

* 1366×768 Windows POS
* 1920×1080 terminal
* tablet

Do not sacrifice desktop POS efficiency just to make it mobile-looking.

---

# BACK-OFFICE UX

Create navigation approximately:

Dashboard
Sales
Products
Inventory
Purchases
Customers
Suppliers
Employees
Reports
Locations
Integrations
Settings

For Phase 1, only implemented sections must be functional.

Future navigation entries can either be omitted or clearly marked unavailable.

Do not present fake functioning enterprise modules.

---

# DESIGN SYSTEM

The product needs a strong commercial SaaS appearance.

Use:

* clean typography
* subtle borders
* restrained shadows
* compact tables
* strong spacing consistency
* accessible contrast
* responsive layouts
* reusable components
* skeleton/loading states
* empty states
* error states
* confirmation dialogs where destructive

Do not overuse gradients or animations.

POS UI should feel like a professional operational tool, not a marketing website.

---

# TESTING

Testing is mandatory.

Backend:

* unit tests for money/tax calculations
* sale total calculations
* inventory changes
* tenant isolation
* sale completion

Integration tests should test important endpoints using a test database strategy.

Frontend:

At minimum add tests around critical calculation/business components where reasonable.

Prefer Playwright for important end-to-end flows when the base application is working.

Critical E2E scenario:

1. login
2. create product
3. add opening inventory
4. open POS
5. sell product
6. complete cash sale
7. verify receipt
8. verify stock reduction
9. verify sale appears in history

---

# SECURITY BASELINE

Implement sensible security from day one:

* password hashing using a well-established framework
* no plaintext passwords
* validation
* authorization
* tenant isolation
* secure headers where applicable
* rate-limit login
* secrets through environment configuration
* no secrets committed
* sanitized logs
* audit support

Do not design a system that stores sensitive payment credentials directly.

---

# OBSERVABILITY

Configure:

* structured Serilog logs
* correlation/request IDs
* `/health`
* `/health/ready`
* error logging

Prepare OpenTelemetry-compatible abstractions/configuration if practical, but do not over-engineer infrastructure.

---

# DOCKER / LOCAL DEVELOPMENT

Provide a straightforward local development environment.

Prefer Docker Compose for infrastructure such as:

* PostgreSQL
* Redis only if necessary

Application should not require Kubernetes.

Create `.env.example`.

Do not put real secrets in repository.

---

# REPOSITORY STRUCTURE

Before implementation, decide and document the repository layout.

A suitable direction may resemble:

```
apps/
  web/
  pos/

src/
  backend/
    Api/
    Modules/
      Identity/
      Tenancy/
      Catalog/
      Customers/
      Inventory/
      Sales/
      Reporting/
    BuildingBlocks/

tests/
  backend/
  e2e/

docs/
```

But inspect the existing repo first and do not restructure destructively if good foundations already exist.

For .NET, an alternative inside backend can be:

```
src/
  Pos.Api
  Pos.Domain
  Pos.Application
  Pos.Infrastructure
```

with domain modules inside those layers.

Choose the structure that best supports the modular-monolith approach without creating hundreds of unnecessary projects.

---

# DOCUMENTATION TO CREATE FIRST

Before significant implementation, create/update:

`docs/ARCHITECTURE.md`

It must document:

* architecture
* module boundaries
* tenant hierarchy
* major entities
* transaction boundaries
* offline strategy
* sync strategy
* tax strategy
* payment abstraction
* vertical module strategy
* deployment topology

Create:

`docs/PHASE_1.md`

Containing:

* Phase 1 scope
* explicit non-goals
* acceptance criteria
* milestones

Create:

`docs/DATA_MODEL.md`

Containing the initial entity relationships and important invariants.

Also create:

`docs/DECISIONS/`

with lightweight ADRs for major decisions such as:

* modular monolith
* PostgreSQL
* UUID IDs
* inventory ledger
* offline architecture

---

# WORKING RULES

You are allowed to create and modify files.

Do not merely give recommendations.

Actually implement the repository.

However:

1. inspect first
2. document the design
3. scaffold the architecture
4. implement foundation
5. implement the Retail vertical slice
6. add tests
7. run the application/tests
8. fix failures

Do not stop at generated boilerplate.

Do not create dozens of TODO-only files.

Do not implement fake features with hard-coded mock data unless explicitly isolated as development fixtures.

---

# VERY IMPORTANT: DO NOT OVERBUILD YET

Do NOT implement in this phase:

* full hotel PMS
* pharmacy prescription system
* liquor excise integrations
* restaurant KDS
* Swiggy/Zomato
* fuel pumps
* marketplace
* accounting ERP
* live payment terminal integration
* AI copilot
* advanced warehouse management
* microservices
* Kubernetes
* Kafka
* ClickHouse
* mobile app

Only establish extension points for them.

---

# INITIAL DEMO DATA

Provide a clean development seed containing something like:

Organization:
Demo Retail Pvt Ltd

Location:
Kalyani Main Store

Warehouse:
Main Store Warehouse

Products:

* Rice 5kg
* Milk 1L
* Premium T-Shirt
* Shampoo 180ml

Customers:

* one walk-in/default customer if appropriate
* one normal customer

Opening inventory sufficient to demonstrate checkout.

Taxes should demonstrate a reasonable India GST configuration.

Seed data must be development-only and idempotent.

---

# QUALITY BAR

Code must be:

* production-oriented
* readable
* typed
* modular
* maintainable
* testable

No giant controllers.

No giant React components.

No business rules hidden inside UI components.

No direct database access from controllers.

No copy-pasted business logic.

No premature abstraction for hypothetical problems.

---

# FIRST EXECUTION

Start now.

Before modifying significant code:

1. inspect repository tree
2. read `project_concept.md`
3. read/extract the relevant research PDF
4. inspect existing source files
5. determine whether there is useful existing code or the project is effectively greenfield

Then create:

* `docs/ARCHITECTURE.md`
* `docs/DATA_MODEL.md`
* `docs/PHASE_1.md`
* relevant ADRs

Then scaffold the solution and implement **Milestone 1 Foundation**.

## MILESTONE 1 FOUNDATION ACCEPTANCE CRITERIA

By the end of this first execution, I want:

* repository architecture established
* backend builds successfully
* frontend builds successfully
* PostgreSQL development configuration
* database migrations
* Tenant / Organization / Location / Terminal / Warehouse entities
* basic User/Auth/RBAC foundation
* Product / Category models
* Customer model
* Inventory ledger foundation
* Sale / SaleLine / Payment foundation
* tax calculation foundation
* health endpoints
* OpenAPI
* ProblemDetails
* structured logging
* seed data
* basic dashboard shell
* basic products UI
* initial POS checkout UI shell
* automated tests for core calculation/domain invariants

Do NOT rush into Milestone 2 until Milestone 1 actually builds and tests pass.

After implementation, run all available builds/tests and fix errors.

Finally give me a concise implementation report containing:

1. Repository structure created
2. Architectural decisions made
3. What is actually working
4. Tests executed and results
5. Database migrations created
6. Exact commands to run backend/frontend locally
7. Remaining items for Milestone 2
8. Any important risks or architectural concerns discovered from the source documents

Do not ask me broad architectural questions unless an absolutely blocking contradiction exists in the source documents.

Where details are unspecified, make professional engineering decisions consistent with `project_concept.md` and the market-research document.
