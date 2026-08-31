# ADR 0004: Model Inventory as a Ledger

## Status

Accepted

## Decision

Inventory is represented by append-only inventory transactions and materialized/read stock balances, not by a mutable `Product.StockQuantity`.

## Rationale

Retail, grocery, pharmacy, liquor, and wholesale workflows require auditability across opening stock, purchases, sales, returns, transfers, wastage, damage, and adjustments.

## Consequences

Every stock-affecting operation writes ledger entries. Sale completion writes negative sale movements for tracked products.
