# Phase 1 Plan

## Scope

Phase 1 proves the common commerce foundation through a retail POS slice.

Included:

- Repository architecture and local development setup.
- Tenant, organization, location, terminal, and warehouse foundation.
- Basic identity, user, role, and permission model.
- Category, product, barcode, HSN/SAC, and tax metadata.
- Customer profile model.
- Inventory ledger, opening stock, sale stock reduction, and stock balance read model.
- Sale, sale line, payment, discounts, GST calculation, status, and receipt-friendly snapshots.
- Health endpoints, API version prefix, ProblemDetails, structured logging, and OpenAPI entry point.
- Development seed for Demo Retail Pvt Ltd, Kalyani Main Store, products, customers, GST, and opening inventory.
- Dashboard shell, products UI, and POS checkout shell.
- Automated tests for money, tax, sale totals, inventory, tenant isolation, and sale completion invariants.

## Non-Goals

Phase 1 does not include full restaurant KDS, hotel PMS, pharmacy prescription management, liquor excise integration, payment terminal integration, AI copilot, accounting ERP, mobile app, marketplace, Kafka, Kubernetes, or data warehouse infrastructure.

## Acceptance Criteria

- Backend builds successfully.
- Frontend builds successfully when dependencies are installed.
- Domain tests pass.
- PostgreSQL development configuration exists.
- Initial database migration SQL exists.
- API exposes health, readiness, tenant, product, customer, inventory, sale, and report endpoints.
- POS shell supports search, barcode-enter add flow, quantity editing, discount, customer selection, payment method selection, totals, and a completed sale response.

## Milestones

1. Foundation: docs, solution structure, domain models, API shell, seed data, migration, frontend shell, tests.
2. Retail completion: real persistence via EF Core/PostgreSQL, authentication flow, product/customer management forms, sale persistence, receipt UI, stock history, and sales history.
3. Offline preparation: local edge data store, outbox processing, sync status, terminal registration, conflict rules.
4. Operational hardening: RBAC enforcement, audit trails, cash drawer, returns/voids, Playwright E2E, deployment pipeline.
