# Milestone 4: Operational Hardening Report

Implementation and verification: 2026-10-22. Scope follows `MILESTONE_4.md`, derived from the next PHASE_1 roadmap entry; no separate milestone prompt existed.

## Delivered

- Tenant-scoped user creation, name/status/role editing, password reset and role/permission editing under ManageUsers.
- Version-checked edits, serialized identity transactions, current-permission rechecks and last-active-administrator protection.
- Current PostgreSQL permissions on every authenticated request, rather than trusting eight-hour token grants. `/auth/me` refreshes browser session metadata.
- Credential generations revoke sessions after reset and status changes, including disable/re-enable. Legacy tokens default to generation 1; upgrade accounts initialize to 1.
- Read-only, paged audit review under ViewAudit, with exact action/entity/actor and UTC-range filters. Identity mutations and safe audit metadata commit or roll back together. No passwords, hashes or bearer tokens are included in identity DTOs/audits.
- `/administration` Users/Roles/Audit views, native modal editors and event review, loading/error/empty states, filters, pagination, labeled icon actions, keyboard tabs and responsive table scrolling. No audit editing/deletion or hard deletion of identity records.
- Same-origin browser API requests, local proxy fallback and a standalone startup helper that stages static files. The standalone runtime is exercised by E2E rather than relying on the development server.
- CI deployment packages, checksums, commit/run manifest, test evidence upload, packaged API/bundle smoke checks and a provenance-checked approval handoff workflow.
- Fixed a stock-concurrency regression uncovered during the full suite: mixed Serializable checkout/Read Committed adjustment could oversell from a stale snapshot despite the advisory lock. Inventory adjustments now use Serializable and bounded retry, sharing provider-level serialization/deadlock classification with sales. A deterministic paused-checkout test forces the problematic ordering and checks that stock cannot be consumed twice.

## Files And Migration

Principal additions: `Pos.Application/Administration/{AdministrationDtos,AdministrationService,AuditService}.cs`, `Auth/IdentityPermissions.cs`, `apps/web/app/administration/page.tsx`, `apps/web/scripts/start.mjs`, PostgreSQL `AdministrationFlowTests.cs`, Playwright `administration.spec.ts`, `.github/workflows/release.yml`, `docs/MILESTONE_4.md` and `docs/DEPLOYMENT.md`.

Updated API routes/DI/auth handler/token issuer; CurrentUser/AuthService; user entity/permission enum; DbContext/design factory/migration snapshot; browser API/session helper, navigation, styles, Next config/start command; CI, README, architecture/data-model docs. Stock fixes additionally touch InventoryService, the sale concurrency classifier, new `Persistence/DatabaseConcurrency.cs`, and deterministic interception in PostgreSQL retail tests.

Migration `20261021100216_OperationalHardening` adds AuthVersion, ordered tenant/audit indexing and a migration-owned normalized-email expression index. It copies ManageUsers/ViewAudit once from existing ManageSettings roles. Startup seeding does not restore later revoked permissions. Global case-insensitive email uniqueness matches email-only login. Duplicate legacy emails cause an explicit migration failure and require ownership resolution, not an automatic merge. Financial records and edge formats are unchanged.

## APIs

- `GET /api/v1/auth/me`: current authenticated identity and permissions.
- `GET /api/v1/administration/permissions`, `/roles`, `/users?q=&page=&pageSize=`: administration reads.
- `POST /api/v1/administration/roles`, `PUT /roles/{id}`: role edits, expected version for updates.
- `POST /api/v1/administration/users`, `PUT /users/{id}`: create/update users; tenant-owned role IDs only.
- `POST /api/v1/administration/users/{id}/password`: administrator reset with expected version, 12-128-character password and session invalidation.
- `GET /api/v1/audit?action=&entity=&actorId=&fromUtc=&toUtc=&page=&pageSize=`: tenant-scoped review, at most 100 entries per page, deterministic newest-first ordering.

Every administration route requires ManageUsers; audit requires ViewAudit. Validation is 400, missing tenant-owned targets 404, denied permission 403, invalidated credentials 401 and stale/lockout edits 409. Last-administrator checks happen inside the serialized transaction after proposed changes; rejected mutations roll back their audits as well. Actor UUIDs are durable; displayed actor names are resolved from current tenant users, not historical name snapshots.

## Verification

Exact verification results are recorded below. Commands:

```powershell
dotnet build UniversalCommerce.sln -m:1 --no-restore
dotnet test UniversalCommerce.sln -m:1 --no-build --no-restore
```

In apps/web: `npm test`, `npm run build`, `npm run test:e2e`. EF `migrations has-pending-model-changes --no-build` with explicit configuration confirmed no model drift. API Release publish was also exercised locally.

Coverage includes grants/revocations on existing tokens, reset and disable/re-enable invalidation, password/hash exclusion, last-admin rollback, concurrent self-demotion, cross-tenant read/write/reference rejection, stale versions, unknown permissions/weak-password validation, audit filtering/pagination, upgrade backfill/seed preservation and legacy token compatibility/revocation. Existing retail/concurrency/edge-recovery coverage remains in the solution.

Browser flows cover role/user creation, assignment and edit, audit filters/event details, live audit-permission revocation, cashier denial, persisted cash checkout/history/stock and receipt PDF generation. Administration/audit screenshots include desktop, tablet and 390px mobile; details were moved to a modal after visual QA exposed oversized off-screen expanded rows. Generated evidence remains ignored under `apps/web/test-results/`.

## Deployment Boundary

CI packages Linux x64 API publish output, standalone web output/static files and a self-contained EF bundle only after tests/E2E. It applies the packaged migration runner twice against a separate empty PostgreSQL database, starts the packaged API in Production, checks readiness and verifies no demo tenant was seeded. The archive has a SHA-256 companion and exact commit/run manifest.

`Approve Deployment Candidate` checks a successful master push from this repository's CI workflow at the exact dispatch commit, then enters the configurable release environment and preserves the verified candidate. Required reviewers, branch restrictions and secrets are repository/operator configuration, not enforceable by YAML alone. The dispatch workflow itself has not been run against an operator-configured release environment.

No production host or deployment secrets were supplied. No live deployment or production migration was performed. This is a tested deployment-candidate and approval-handoff pipeline, not an invented host deployment. The deployment runbook covers explicit credentials, staging/backup, ambiguity preflight, proxy/runtime setup, health checks and financial/security-aware rollback. EF tools now fail without explicit connection configuration instead of choosing the demo database.

## Limits

Custom HMAC authentication remains; no IdP, MFA, invitation flow, self-service recovery, password policy beyond length, per-location assignments or production bootstrap UI. Existing commerce read permissions are unchanged outside the new administration/audit boundary. Permission reload adds database reads per request; no permission cache is introduced. No production monitoring stack, retention automation, tamper-evident external audit store or cloud provisioning. Audit data can contain merchant/customer personal data and needs appropriate access, retention and backups. Do not roll back to pre-credential-generation authentication after revocations; prefer a forward fix or rotate signing keys. Full offline checkout and vertical modules remain deferred.

## Results

- Solution build succeeded with zero warnings/errors; final solution test run passed all 55 tests: 15 domain, 2 fast integration, 25 PostgreSQL and 13 SQLite edge tests.
- Both focused stock-concurrency tests passed before the final full run, including the deterministically paused stale-snapshot case.
- Five frontend calculation tests passed. Production Next build/TypeScript checks passed. All three standalone Chromium E2E flows passed after the final stock fix.
- Desktop/mobile screenshots were visually inspected; the audit dialog and tables have no page-width overflow at tested viewports. EF reported no pending model changes. Final API Release publish passed.
- Local API readiness and `/administration` HTTP smoke checks passed. Preview: `http://localhost:3000/administration` (sign-in required).
- Implementation commit `9f534d6` was pushed to master. [GitHub Actions run 39726672659](https://github.com/koushikdatascience-netizen/pilot/actions/runs/39726672659) passed all steps in 2m42s: restore/build/tests on PostgreSQL 16, frontend tests/build, all three browser flows, Linux candidate packaging, migration bundle applied twice, published Production API readiness and zero seeded tenants.
- CI uploaded `pilot-linux-x64` (74,492,450 bytes) and `test-evidence` (892,273 bytes). Both artifacts were confirmed available through the Actions API. The candidate contains the archive/checksum pair described in the runbook.
- GitHub recognizes both workflows as active. The approval workflow was not dispatched; release reviewers, environment protection and production credentials remain unconfigured by this task. No live deployment or production database changes occurred. Actions emitted non-blocking Node-runtime/runner-image notices; all required checks passed.
