# Milestone 5: Retail Operations Report

Implemented 2026-10-22. The user selected retail returns, voids and cash drawer after the roadmap ended at Milestone 4. Scope is recorded in `MILESTONE_5.md`; no new vertical or full offline checkout was started.

## Delivered

- Receipt-linked partial returns with three-decimal quantities, restock/no-restock decisions, original warehouse and historical tax/discount snapshots. Current catalog edits do not change the reversal.
- Cumulative coupled decimal allocation: quantity determines gross, historical tax is allocated within gross, and CGST/SGST within tax (or IGST alone). Every component stays nonnegative and complete returns exactly reconcile original amounts, including small fractional returns.
- Full completed-sale void only before any earlier correction. Original completed invoices, payment captures and stock entries stay unchanged; correction documents, manual refund records and optional positive stock entries are appended separately.
- Held/Draft cancellation produces a zero-value OpenVoid audit document without payment, stock or invoice creation. Expected-status validation and shared held-cart locking protect races with completion.
- Tenant-scoped processing terminals, separate ProcessReturns/VoidSales/ManageCashDrawer permissions, reason validation, permanent command UUIDs, canonical payload hashes and replay. Reused UUIDs with changed payloads, over-returns and excess tender refunds are rejected.
- One open cash drawer per terminal, opening float, paid-in/out, cash sale/refund capture, expected cash, counted close, note and variance. Sale capture uses AmountDue rather than tender including change. Cash refunds and paid-out cannot overdraw expected cash.
- Atomic correction/refund/stock/drawer/audit/outbox commits, Serializable transactions, advisory locks and bounded database-concurrency retries. Drawer movements advance the session concurrency token so stale snapshots cannot append after closure.
- `/cash-drawer`, receipt correction history/printing and held-cart void controls. Frozen uncertain commands survive reload through same-tab session storage and reuse the original UUID/payload. Error, pending, permission, empty and closed states are handled.
- Dashboard gross sales, corrections recorded today, net revenue and separate refund tender mix. Returns of older invoices reduce today's net revenue; original history remains gross. Day boundaries retain the existing UTC reporting policy.

## Files And Migration

Principal additions are `Pos.Application/Retail/{RetailDtos,RetailTransactions,SaleCorrectionService,CashDrawerService}.cs`, `Pos.Domain/Sales/ReturnAllocation.cs`, `apps/web/components/retail-corrections.tsx`, `apps/web/app/cash-drawer/page.tsx`, `apps/web/lib/use-retail-command.ts`, domain `ReturnAllocationTests.cs`, PostgreSQL `RetailOperationsTests.cs` and browser `retail-operations.spec.ts`.

Updated API policies/routes/DI, completion drawer capture, reporting, permission enum, entities/DbContext, browser API/navigation/history/print/styles, README, architecture, data model and roadmap.

Migration `20261022052659_RetailCorrectionsAndDrawers` creates sale_corrections, sale_correction_lines, sale_refunds, cash_drawer_sessions and cash_drawer_movements with composite tenant foreign keys and command/document uniqueness. A partial unique index permits one open session per terminal. It grants the three new permissions once to roles already holding ManageSettings. Later revocations are not overwritten. Existing financial records and edge queue formats are unchanged.

## APIs

- `GET /api/v1/sales/{id}/corrections`: effective state and immutable correction history; CompleteSale, ProcessReturns or VoidSales required.
- `POST /api/v1/sales/{id}/returns/quote`, `/returns`: server quote and recorded partial return; ProcessReturns required.
- `POST /api/v1/sales/{id}/void/quote`, `/void`: quote and open/full completed void; VoidSales required. ExpectedStatus must match the original sale's current status.
- `GET /api/v1/cash-drawers/terminals/{id}`: newest 20 sessions with movements; ManageCashDrawer required.
- `POST /api/v1/cash-drawers/terminals/{id}/open`, `/cash-drawers/{id}/close`, `/cash-drawers/{id}/movements`: idempotent drawer commands; ManageCashDrawer required.

Correction requests include processing TerminalId, ClientGeneratedId, Reason, Lines and Refunds. Quote calculates authoritative amounts but does not reserve quantities or funds; recording revalidates inside the transaction. Refund amounts must exactly match the correction, with each tender capped by original captured AmountDue less earlier refunds. Non-cash/non-credit refunds require an operator reference. Validation returns 400, denied permissions 403, missing tenant-owned references 404, and financial/state/idempotency conflicts 409.

## Verification

Verification commands: `dotnet build UniversalCommerce.sln -m:1 --no-restore`, `dotnet test UniversalCommerce.sln -m:1 --no-build --no-restore`; in apps/web, `npm test`, `npm run build`, `npm run test:e2e`; EF `migrations has-pending-model-changes --no-build` with an explicit connection. Final results and CI evidence are recorded below after execution.

PostgreSQL coverage includes cumulative returns, no-restock, immutable original captures/ledger, manual reference validation, historical catalog/warehouse changes, rollback without a drawer, replay and changed-payload conflict, simultaneous last-unit returns, identical-command concurrency, held cancellation versus completion, competing drawer openings, overdraw/closed-session rejection, close versus cash checkout, permission denial and real foreign-tenant references.

The browser workflow simulates a committed return whose response is lost, reloads and retries the retained command, then verifies one correction. It also exercises completed and held voids, paid-in, close variance, stock reconciliation, correction PDF and desktop/tablet/mobile layouts. Generated evidence is ignored under `apps/web/test-results/`.

## Limits And Operations

Refunds are manual records, including credit reversals; there is no payment gateway execution, customer-credit accounting ledger, bank reconciliation, statutory credit-note submission or tax filing. Operators must independently execute/verify external refunds. Correction numbers are UUID-based internal document identifiers, not a claimed statutory numbering scheme. Historical mixed/invalid tax or ambiguous warehouse allocation requires manual review instead of inferred reconstruction.

Drawer tracking is opt-in: sales made without an open session are not retroactively assigned. Sessions are terminal-wide, not personal shifts; closing may race successfully before a sale, leaving that later sale unassigned by policy. No physical cash-drawer or printer driver, denomination counting or supervisor approval workflow is added. Session listing is limited to the newest 20 sessions and includes all their movements; paged long-term archive/export remains future work.

Browser pending commands are session storage, not a durable cross-device queue. Do not clear storage, close a tab with an unresolved request, or reissue a fresh UUID before reviewing authoritative history. No offline returns/voids or automatic conflict resolution is provided. Server idempotency records remain permanent. Audit/outbox data and operator references require normal tenant access controls, backups and retention policy; no tamper-evident external ledger is claimed.

No production host, secrets or approval reviewers were supplied. No production migration or live deployment was performed. The existing tested deployment-candidate packaging/approval handoff remains the deployment boundary. Back up before applying migrations and prefer forward corrections over downgrades that discard retail history.

## Results

- Solution build passed with zero warnings/errors. All 70 backend tests passed: 20 domain, 2 fast integration, 35 PostgreSQL and 13 SQLite edge tests. This includes a regression proving that returning an older invoice makes today's net negative without inventing today's gross sales, plus malformed-line validation.
- All five frontend calculation tests passed. The production Next build and TypeScript checks passed. All four standalone Chromium browser flows passed in 53.6 seconds.
- Desktop/tablet/mobile checks at 1366, 768 and 390 pixels found no page-width overflow. Return and drawer screenshots were visually reviewed; correction and original receipt PDFs were generated. Evidence remains in the ignored test-results directory.
- EF reported no pending model changes. API Release publish succeeded. Local API readiness returned ready/postgres and `/cash-drawer` returned HTTP 200. Preview: `http://localhost:3000/cash-drawer` (sign-in required).
- One attempted build overlapped the browser API and hit Windows' executable lock; the final build passed after the suite stopped. The first unbounded parallel Release publish exited without diagnostics; a single-worker publish passed, and its orphan build workers were cleaned up. Browser test timing/locator issues were corrected before the final full passing run.
- Implementation commit `a9c670a` was pushed to master. [GitHub Actions run 39749994515](https://github.com/koushikdatascience-netizen/pilot/actions/runs/39749994515) passed in 3m23s, including all 70 backend tests on PostgreSQL 16, five frontend tests, production build and all four browser flows (21.3 seconds).
- CI packaged Linux API/standalone web and a self-contained EF migration runner. The packaged runner applied the schema to an empty database and a second invocation confirmed it was up to date. Packaged Production API readiness and absence of seeded demo tenants passed.
- Actions artifacts were confirmed available and unexpired: `pilot-linux-x64` (74,594,200 bytes) and `test-evidence` (2,271,422 bytes). The candidate includes the archive/checksum pair and exact commit/run manifest.
- CI emitted non-blocking action Node-runtime and Ubuntu runner-image notices. All required checks passed. No live production deployment or production database change occurred.
