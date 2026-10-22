# Milestone 4: Operational Hardening

Derived from the next roadmap entry in PHASE_1.md. No separate Milestone 4 prompt is present.

Deliver tenant-scoped user/role administration, explicit ManageUsers and ViewAudit permissions, live permission revocation, credential-version invalidation, and last-administrator protection. Support creating users, editing names/status/roles, resetting passwords, and creating/editing permission roles. Use expected versions to reject stale edits. Do not expose password hashes or credentials in responses, audit records or logs.

Provide a paged/filterable read-only audit review API and UI with actor, action, entity and UTC time. Audit identity changes transactionally; never allow audit updates/deletes through the API. Extend browser coverage to administration, audit review, permission denial and responsive layout.

Provide a deployment pipeline that packages tested API/web artifacts and an EF migration bundle, with a manually approved GitHub environment gate. Do not invent a production host, publish credentials, execute migrations against an unspecified database or claim a live deployment. Include a deployment/rollback runbook and honest verification evidence.

Keep the modular monolith, PostgreSQL authority and existing retail/edge behavior. No verticals, full offline checkout, external identity provider, MFA, self-service recovery, location assignments or production infrastructure provisioning.

Acceptance: tenant/RBAC isolation; immediate grant/revocation on existing tokens; credential reset and disable/re-enable invalidate old tokens; last-admin and concurrent demotion protection; stale-update conflicts; validation/rollback; secret-free audit; audit pagination/filtering; frontend build, all tests and CI green; reproducible deployment artifacts.
