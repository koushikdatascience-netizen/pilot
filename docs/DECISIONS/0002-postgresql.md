# ADR 0002: Use PostgreSQL for Cloud Persistence

## Status

Accepted

## Decision

Use PostgreSQL as the primary cloud/server database.

## Rationale

PostgreSQL gives strong relational integrity, indexing, JSON support for audit/outbox metadata, and broad operational maturity.

## Consequences

Local development includes Docker Compose for PostgreSQL. Phase 1 includes migration SQL even while the first API uses an in-memory development repository to keep the scaffold buildable without external package restore.
