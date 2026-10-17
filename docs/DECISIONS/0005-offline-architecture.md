# ADR 0005: Prepare an Offline-First Edge Runtime

## Status

Accepted

## Decision

Design cloud and POS contracts so a future local edge runtime can execute checkout, print receipts, and sync immutable operations when connectivity returns.

## Rationale

The source research identifies offline resilience as a major competitive requirement, and the product concept positions hybrid cloud plus local edge as a differentiator.

## Consequences

Phase 1 includes terminal identity, client-generated IDs, sync status, immutable sale snapshots, and outbox events. Milestone 3 adds a SQLite preparation runtime with catalog bootstrap, versioned change pages, atomic local checkpoints, durable command queue, lease recovery, and server-authoritative upload. The API publishes transactional outbox events into a PostgreSQL feed; device acknowledgement is separate from publication.

The browser POS is not yet wired to the local runtime. Offline invoicing, local stock authority, device-specific credentials, encrypted storage, native packaging, conflict-resolution UI, and feed retention/compaction remain deferred. See `docs/MILESTONE_3.md` for the bounded preparation scope.
