# ADR 0001: Use a Modular Monolith for V1

## Status

Accepted

## Decision

Build the first production foundation as one ASP.NET Core modular monolith with strict internal module boundaries.

## Rationale

The product needs a unified commerce core and fast iteration across retail POS, inventory, tenancy, tax, and payments. Microservices would add deployment and data consistency complexity before the domain boundaries are proven.

## Consequences

Modules share one deployable process and database in V1. Module APIs, events, and persistence ownership should still be clear enough that high-pressure domains can later be extracted.
