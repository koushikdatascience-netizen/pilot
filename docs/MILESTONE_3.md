# Milestone 3: Offline Preparation

This scope is derived from the Milestone 3 roadmap in PHASE_1.md and ADR 0005. No separate user-supplied Milestone 3 prompt was present.

Deliver:

- Versioned sync contracts, tenant-scoped terminal/device registration and acknowledgements.
- A durable change feed published atomically from the transactional outbox.
- A bounded background publisher safe for multiple API instances.
- A SQLite edge library and command-line demonstrator with a catalog cache, checkpoint, inbox and immutable upload queue.
- At-least-once sale upload using existing server idempotency and authoritative stock/tax/permission checks.
- Restart recovery, version-aware catalog updates and explicit conflict state.
- PostgreSQL and SQLite tests, CI, local commands and verification report.

Rules:

- No broker, microservice, Tauri/mobile packaging or native drivers.
- The cloud remains the invoice/stock authority. The queue contains sale requests, not completed offline invoices.
- Catalog and cursor commit together. Acknowledgement follows durable local commit.
- Pending writes retain the same client UUID and payload on retry. Validation/stock/permission conflicts require review; they never silently change prices or payments.
- Never persist passwords or bearer tokens. The development SQLite file is not production encryption; customer profiles are not cached.
- Completed cloud sales and stock ledgers remain immutable.

Acceptance: registration replay/conflict/isolation; publication rollback/replay; cursor/ack bounds; parallel publication order; bootstrap consistency; SQLite restart/transaction rollback; duplicate command protection; upload replay/conflict handling; all previous builds/tests/E2E remain green.
