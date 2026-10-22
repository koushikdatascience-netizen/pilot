# Milestone 5: Retail Corrections And Cash Drawer

User-selected next scope: retail returns, voids and cash drawer. This closes deferred retail workflows, not full offline checkout or a new vertical.

## Policy

- Original completed invoices, payments and sale ledger entries stay unchanged. Separate immutable correction documents reference the original sale/lines. Effective return/void state is derived from those documents.
- Returns support partial quantities (three decimal places), explicit restock/no-restock decisions, original warehouse and original tax/discount snapshots. Allocate gross refund cumulatively by quantity, then historical tax within cumulative gross and CGST/SGST within tax (or IGST alone). Coupled decimal rounding keeps every component nonnegative and all partial returns together exact. Discount snapshots allocate cumulatively by quantity. Unsupported mixed historical tax components require manual review. Do not use current prices, taxes or stock-tracking flags.
- Completed-sale void is a full reversal only when no previous correction exists. Held/Draft void changes only the open cart status and creates a zero-value audit document; no stock, invoice or refund is generated.
- Require reason, tenant-owned active processing terminal and permanent client command UUID. Replay identical commands; reject changed payloads, over-returns, repeated voids and cross-tenant references. Serialize corrections per original sale and retry only database concurrency failures.
- Refund allocations must exactly cover the server-calculated correction and cannot exceed the original captured amount of each tender after earlier refunds. Cash/UPI/Card/Wallet/Credit are manual records; non-cash/non-credit refunds require an operator reference. No live gateway, statutory credit-note submission, tax filing or accounting integration is claimed.
- ProcessReturns, VoidSales and ManageCashDrawer are separate permissions, granted once to existing ManageSettings roles by migration. Cashiers require an explicit role grant.
- One open drawer session per terminal. Opening float, paid-in/out, cash sale/refund movements, expected cash, counted close and variance are durable and audited. Cash sale allocation is AmountDue (not amount tendered). Drawer tracking is opt-in: pre-existing checkout without a session continues; those sales are not retroactively assigned.
- Cash refunds require an open processing-terminal drawer with sufficient expected cash. Paid-out also cannot overdraw expected cash. Closed sessions/movements are immutable. Sale/refund movement capture is atomic with the financial transaction; close/open and capture share a terminal lock and concurrency token.
- Today's revenue is net of correction documents recorded today, including returns of earlier sales. Also expose gross sales, refund totals and refund tender mix; original invoice history remains gross.

## Deliverables And Acceptance

API, migrations, domain allocation tests, PostgreSQL replay/rollback/concurrency/isolation tests, usable receipt-linked correction controls and drawer UI, browser E2E, all prior builds/tests/CI, and a verification report. No physical drawer/printer driver, offline refund, automatic correction, hard deletion, production deployment or additional vertical.
