# ADR 0003: Use UUID Business Identifiers

## Status

Accepted

## Decision

Use UUID/GUID identifiers for tenants, organizations, locations, terminals, warehouses, products, customers, sales, payments, inventory transactions, and outbox events.

## Rationale

Offline terminals must create permanent identifiers before they sync with the cloud. Sequential database IDs are not suitable as distributed business identifiers.

## Consequences

All domain entities expose GUID IDs. Database indexes and API contracts assume GUIDs as public identifiers.
