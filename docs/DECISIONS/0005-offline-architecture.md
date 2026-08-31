# ADR 0005: Prepare an Offline-First Edge Runtime

## Status

Accepted

## Decision

Design cloud and POS contracts so a future local edge runtime can execute checkout, print receipts, and sync immutable operations when connectivity returns.

## Rationale

The source research identifies offline resilience as a major competitive requirement, and the product concept positions hybrid cloud plus local edge as a differentiator.

## Consequences

Phase 1 includes terminal identity, client-generated IDs, sync status, immutable sale snapshots, and outbox events. The full SQLite sync engine is deferred.
