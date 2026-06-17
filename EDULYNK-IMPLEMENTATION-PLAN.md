# Edulynk → ASDK Marketplace Integration — Implementation Plan

Engineering plan for placing the Educ8e Connector (Edulynk) product onto the ASDK marketplace
template. Decisions and shared language are fixed in [`CONTEXT.md`](./CONTEXT.md); this document is
the *how*. Branch: `core-app-integration`.

## Deliverables (5 deployables)

| # | Deployable | Repo | Hosting |
|---|---|---|---|
| 1 | Admin API (platform) | this repo | App Service (exists) |
| 2 | Permissions API (platform) | this repo | App Service (exists) |
| 3 | SignupAdmin + Publisher console (platform) | this repo | App Service (exists) |
| 4 | **Edulynk API** (product) | Educ8e Connector | App Service (**exists, reuse**) |
| 5 | **Edulynk Web** (product) | Educ8e Connector - FE | Static Web App (exists, retarget) |

The ASDK Razor shell `Saas.Application.Web` and its pipeline are retired.

## Decisions this plan implements (from the grilling)

1. **Gate = Students** (app-DB rows), ceiling per-Plan; ASDK owns the map, Edulynk enforces locally.
2. **Membership** via ASDK invite + permission assignment; staff bring their own Microsoft accounts.
3. **RBAC** in the ASDK Permissions store, resolved per-request; not Entra app roles.
4. **Auth** = Workforce multitenant; FE on saas-app reg, API on its own reg; `tid` → Tenant.
5. **Config** = Key Vault (secrets) / App Configuration (deploy) / DB (per-Tenant settings).
6. **DB-per-tenant**, Basic DTU, created at activation, managed-identity auth (no SQL passwords).

---

## Dependency graph (what unblocks what)

```
Phase 1 Platform seam (Admin API quota + dbName + provision hook)
   │         └────────────┐
Phase 4 Identity/app regs │   (can run in parallel with 1)
   │                      ▼
   └────────►  Phase 2 Edulynk API (auth, tenant resolution, DB, RBAC, student gate, provision)
                          │
                          ▼
              Phase 3 Edulynk Web (multitenant sign-in, permission gating, tenant settings)
                          │
   Phase 5 Infra/deploy ◄─┘   (SQL server+pool, edulynk-api App Service, SWA, pipelines, KV/AppConfig)
                          │
                          ▼
              Phase 6 Partner Center offer  →  Phase 7 data migration + cutover + security
```

Phases 1 and 4 are the critical path and have no product-code dependencies — start there.

---

## Phase 1 — Platform seam (Admin API)  ✅ CHECKPOINTED (2026-06-17)

Goal: expose everything Edulynk needs from the platform, and call Edulynk at activation. No Edulynk
code required to land/test this phase.

> **Checkpoint status:** the write-path + membership seam is **done and committed** — quota endpoint
> (1.1), Tenant `DatabaseName` + migration (1.2), provisioning hook at activation (1.3, Noop until the
> HTTP impl lands with 2.6), seat-stays-Noop (1.4), and the full JIT invite→bind round-trip (1.5:
> entities, `TenantMembershipService`, Permissions controller, hand-written `ITenantMembershipClient`,
> Admin `invite` rewrite + `members/bind` endpoint). 35 Admin marketplace/integration tests + 6
> Permissions membership tests green. **One item deferred** (see 1.5 below). Moving to Phase 2.

### 1.1 Student ceiling map + quota endpoint
- `Saas.Lib/Saas.Shared/Options/MarketplaceOptions.cs`: add
  `Dictionary<int,int>? TierMaxStudents` (ProductTier id → max students). **Fail-closed:**
  unmapped tier / absent map ⇒ ceiling `0` = **no students may be registered** (a config slip must not
  hand out unlimited students; no "unlimited" sentinel — uncapped plans map to an explicit high number).
- `Fulfillment/MarketplaceFulfillmentService.cs`: add `MapTierMaxStudents(int tierId)` mirroring the
  existing `MapPlanToProductTier`.
- New `Controllers/TenantQuotaController.cs`: `GET /api/tenants/{tenantId}/quota` →
  `{ tenantId, planId, productTierId, maxStudents, subscriptionStatus }`. Resolves the tenant →
  `SubscriptionId` → marketplace `Subscriptions` row → `AmpplanId` → tier → maxStudents. Authorized
  for the service-to-service caller (see 4.4).
- DTO `Fulfillment/QuotaDto.cs`.

### 1.2 Tenant → database name on the Tenant record (the Catalog)
- `Data/Tenant.cs`: add `string? DatabaseName`.
- `Data/TenantEntityTypeConfiguration.cs`: map it (nullable, max len 128).
- EF migration `AddTenantDatabaseName` (mirror the existing `AddSubscriptionLinkToTenant` migration).
- `Controllers/TenantInfoDTO.cs`: surface `DatabaseName` so `tenantinfo/{route}` and the by-id path
  return it. `Saas.Admin.Client` is regenerated from the nswag spec (it's a generated client).

### 1.3 Provisioning hook at activation  ✅ (HTTP impl deferred)
- **Done:** `ActivateAsync` now chooses the database name from a **configurable prefix**
  (`MarketplaceOptions.TenantDatabaseNamePrefix` → `"{prefix}-{route}"`, kept product-agnostic — the
  product name is config, not a literal in the platform), persists it on the tenant, then calls
  `IProductProvisioningService.ProvisionAsync(tenantId, databaseName)`. No prefix ⇒ provisioning is
  skipped (products without a DB-per-tenant model). The call is **synchronous and not swallowed** — a
  provisioning failure propagates (unlike the best-effort email notify).
- **Done:** `IProductProvisioningService` + `NoopProductProvisioningService` (default, registered in
  `Program.cs`); `MarketplaceFulfillmentService` takes it as a dependency.
- **Remaining (depends on 2.6 + 4.4):** the real `HttpProductProvisioningService` — `POST` to the
  Edulynk internal provision endpoint, app-to-app auth, idempotent, with a short retry. Register it in
  `MarketplaceServiceCollectionExtensions` (overriding the Noop) once the product endpoint + service
  app-role exist. Open decision still: short-retry-sync vs. queue via the existing Service Bus; and
  whether to mark the tenant `ProvisioningPending` on failure.

### 1.4 Seat service stays Noop  ✅
- **Done:** **removed** the `IMarketplaceSeatService → MarketplaceSeatService` registration that the
  marketplace block previously added (the original plan's "no change" was wrong — that registration
  *activated* the user-seat cap). The Noop guard registered earlier now stays active, so staff invites
  are uncapped. We gate Students product-side, not users. `MarketplaceSeatService` is kept in the tree
  (and unit-tested) for products that DO sell per-seat — wire it back there for those.

### 1.5 Membership under multitenant — JIT binding (the Graph landmine)

**Root cause (verified in code).** `PermissionsService.AddUserPermissionsToTenantByEmailAsync`
(line 124) calls `GraphAPIService.GetUserByEmail` (line 50), which queries the **publisher's** Graph:

```
_graphServiceClient.Users.GetAsync(filter:
   "identities/any(id: id/issuer eq '{Domain}' and id/issuerAssignedId eq '{userEmail}')")
```

That is a B2C **local-account** filter against the **single publisher directory**. The client is
authenticated as the publisher app in our tenant, so it can only see users in our tenant, and the
`identities/issuerAssignedId` shape is an External-ID/B2C construct Workforce users don't carry. A
bursar living in `school.edu`'s directory is unreachable. `GetUserById` / `GetUsersByIds` (behind
`Permissions GetTenantUsers`) share the flaw. **Conclusion: never look the user up — capture identity
from their own validated token (`oid`/`tid`/`email`/`name`), which their tenant already issued.** This
is the standard Workforce JIT-provisioning pattern, and it removes the publisher-Graph dependency for
membership entirely (this path no longer needs `User.Read.All`).

**Split "invite" into a promise + a first-sign-in binding:**

*Schema (Permissions DB), small:*
- `TenantInvitation` table: `(Id, TenantId, Email, Roles[], InvitedByOid, CreatedAt,
  Status: Pending | Bound | Revoked)`.
- Add `Email` + `DisplayName` to the member record (`SaasPermission` or a new `TenantMember`),
  captured at sign-in, so member lists never touch Graph.

*Endpoints:*
- `invite` (Admin API `POST /tenants/{id}/invite`) **drops the Graph call**: writes a Pending
  `TenantInvitation(tenantId, email, roles)` and returns 200 immediately. Email is plain text — not
  verified to exist anywhere. (Optionally email the invitee a sign-in link.)
- New `POST /tenants/{id}/members/bind` `{ oid, tid, email, name }`: find a Pending invitation
  matching `email` (case-insensitive). **Found** → create `SaasPermission(tenantId, userId = oid)`
  with the invitation's roles, store `email`+`name`, mark invitation `Bound`. **None** → no access
  (caller returns 403 "ask your admin"). **Idempotent** — already-bound ⇒ no-op (safe to call on every
  login).
- `GetTenantUsers` returns members from the local store (stored `email`/`displayName`) plus Pending
  invitations flagged "awaiting first sign-in". No Graph.

*Caller (first-sign-in hook):* the Edulynk API/FE calls `members/bind` after token validation on
login, then caches the resolved permissions (Phase 2.5).

**Nuances baked into the design:**
- **The tenant Admin needs no invitation** — `AddNewTenantAsync(tenantId, userId)` already receives the
  purchaser's `oid` at activation; bind them immediately (also capture their email/name).
- **Email is a one-time matching key only.** The admin knows the person by email; we match on it once
  at bind, then upgrade to the immutable `oid` and never rely on email for identity again. If a token's
  email differs from the invited alias, the user lands in "no access — contact admin", who re-invites
  with the right address (or binds manually).
- **Retire** `GetUserByEmail` and Graph enrichment for the tenant-member path.

Exit criteria: an admin invites `bursar@school.edu`; on the bursar's first sign-in they gain the Bursar
role and appear in the member list, with zero Graph calls into the customer's tenant.

**Status — core domain logic done (TDD):** new entities `TenantInvitation` + `TenantMember`
(`Saas.Lib/Saas.Identity`), wired into `SaasPermissionsContext`; new `ITenantMembershipService` /
`TenantMembershipService` with `CreateInvitationAsync` (writes a Pending invite, email normalized) and
`BindMemberAsync` (matches pending invite by email case-insensitively → grants permissions to the oid +
records the member identity + marks Bound; idempotent; no match → `NoInvitation`). Registered in DI.
New test project `Saas.Permissions.Service.Tests` (4 green). Schema: the Permissions DB uses
`EnsureCreated()` (no migrations), so a **fresh** DB picks up the two tables automatically — an
**existing** prod DB needs a one-time `CREATE TABLE` script (Phase 5 deploy note).

**Admin self-binding — done (TDD):** `AddNewTenantAsync` now records the creator as a `TenantMember`,
and `BindMemberAsync` fills a pre-created member's email/name on first sign-in (returns `AlreadyMember`).
6 membership tests green.

**Permissions API surface — done:** `TenantMembershipController` exposes `CreateInvitation` and
`BindMember` (service-to-service; no generated-client dependency).

**Cross-service client — done (hand-written, no nswag):** nswag CLI isn't available and its spec is a
hand-maintained embedded JSON, so instead of regenerating `PermissionsServiceClient` we added a focused
typed `ITenantMembershipClient` in the Admin service (same Permissions base URL + x-api-key). The Admin
API `invite` path now calls `CreateInvitationAsync` (JIT pending invitation) instead of the Graph email
lookup. Controller test green; 35 Admin marketplace/integration tests green.

**`members/bind` endpoint — done:** Admin API `POST /tenants/{tenantId}/members/bind` binds the
signed-in user from their **token claims** (`oid`/`preferred_username`/`name`) — never client params,
so a user can only bind themselves; auth-only (no tenant permission, since the user has none yet).
Calls `ITenantMembershipClient.BindMemberAsync`. Assumes the caller presents the user's token
(passthrough/OBO — Phase 2.1/4). Controller test green.

**DEFERRED (one item, its own slice — entangled with the generated AdminServiceClient + the
SignupAdmin member-list UI; not on the Phase 2 critical path):**
- Switch `GetTenantUsers` to read `TenantMember` (drop Graph enrichment) — add `GetTenantMembers` on
  the Permissions service + the membership client, then point the Admin consumption (and the console)
  at it. The write path (invite + bind) no longer touches Graph; this is the read/listing path. Picked
  up after Phase 2, or whenever the SignupAdmin member-list UI is next touched.

### Tests (Phase 1)
- Unit: tier resolution (mapped → ceiling / unmapped → 0 block / absent map → 0 block). Quota
  controller resolves status; unknown tenant → 404.
- Unit: provision hook called once on activate; idempotent on replay; failure path marks pending.
- Unit: JIT bind matches a pending invite by email; second bind is a no-op.
- Integration: extend `MarketplaceSubscriptionsApiTests` for the quota endpoint (401 unauth, 200 with
  service token). Run via the existing marketplace `--filter` (the AutoFixture/EF8 admin suite is
  pre-existing red — keep new tests in the green marketplace set).

---

## Phase 2 — Edulynk API (.NET backend)

Goal: turn the single-tenant Educ8e API into a Workforce-multitenant, per-tenant-DB product service
that consumes the platform. Replaces the hardcoded `GetOrganizationConfiguration`.

### 2.1 Multitenant authentication  ✅ DONE (TDD, `saas-kit-integration`)
- `Program.cs`: `AddMicrosoftIdentityWebApi` bound to **multitenant** (`Instance`
  `https://login.microsoftonline.com/`, `TenantId = "organizations"`), audience =
  `api://f41f679b…` (the reused API reg — unchanged audience, see 4.2). Remove the boot-time
  `GetAzureAdConfigurationAsync("lagetronix")` call entirely.
- Issuer validation: accept any tenant but **reject** a token whose `tid` has no provisioned Tenant
  (the tenant resolver returns 404 → middleware 403). Optionally an `IssuerValidator` that defers to
  the resolver.
- **Done:** config-driven via `EdulynkAuthSettings` (`API/Authentication/`, **fails fast** if pinned to
  a single tenant or missing audience — 3 tests) + `AddEdulynkMultitenantAuth` extension. Boot-time
  blocking single-tenant lookup removed. `appsettings AzureAd:TenantId → "organizations"`; added
  `Edulynk:AdminApiUrl`. Fixed pipeline order (`UseAuthentication` before `UseAuthorization`). No
  custom `IssuerValidator` needed — `organizations` + the resolution middleware cover it.
- **Security note:** `appsettings.json` still has a **plaintext `DefaultConnection` password**
  (`P@ssword`) + Service Bus key; removed with 2.3/2.8 and on the Phase 7 rotation list (a *second*
  plaintext password, distinct from `GetOrganizationConfiguration`'s `Lagetronix1#`).

### 2.2 Tenant resolution (replaces OrganizationConfiguration)
- New `ITenantContextAccessor` (scoped): reads `tid`/`oid`/`email`/`name` from the validated token.
  **DONE (TDD, `saas-kit-integration`):** `Domain/Common/Identity/TenantContextAccessor`, tolerates URI +
  short claim forms, fail-closed when no tid/oid. 3 green.
- New `ITenantCatalog`: given `tid`, calls the Admin API **by-tid** endpoint → `{ id, route,
  databaseName, subscriptionStatus }`; **cached** (IMemoryCache, ~5 min). **DONE (TDD):**
  `Data/Platform/TenantCatalog`, caches only successful resolutions, `IsActive` flag. 4 green.
- **DONE (TDD):** `IRequestTenantResolver` (`Data/Platform`) memoizes accessor→catalog per request and
  classifies Resolved/NoTenantClaim/NotProvisioned/Inactive (5 green); `TenantResolutionMiddleware`
  (`API/Middleware`) 403s authenticated requests with no active tenant, anonymous passes through
  (3 green). Wired after `UseAuthentication`.
- **DONE:** deleted `GetOrganizationConfiguration.cs` and its hardcoded list (removed the `Lagetronix1#`
  plaintext password) once 2.1 (multitenant auth) + 2.3 (per-tenant DbContext) replaced its two uses.

> **Platform-side dependency added (DONE, TDD, this repo `core-app-integration`).** The plan originally
> said the catalog "calls `tenantinfo`", but `tenantinfo/{route}` is keyed by **route** and
> **user-permission-gated** — there is no `tid → Tenant` lookup and no service-callable variant. The
> platform Tenant has no `tid` column either; the customer's Entra tid is captured on
> `Subscriptions.PurchaserTenantId` (at resolve). So the resolution joins
> `tid → Subscription.PurchaserTenantId → AmpsubscriptionId → Tenant.SubscriptionId`. Added:
> `SubscriptionQueryService.GetTenantByCustomerTenantAsync(tid)` (prefers an active subscription, then
> most recent) + new `TenantResolutionController` `GET /api/tenants/by-tid/{tid}` returning
> `TenantInfoDTO`. `[Authorize]` (any authenticated caller) like the quota endpoint — Phase 4.3
> tightens both to the service app-role. 6 unit + 3 integration tests green; 47-test marketplace/
> integration suite green.

### 2.3 Per-tenant DbContext (managed-identity connection)  ✅ DONE (TDD, `saas-kit-integration`)
- Replace the static `AddDbContext` (`DependencyInjection.cs`) with a **scoped** connection resolved
  per request: connection string built as
  `Server={sharedSqlServer};Database={tenant.DatabaseName};Authentication=Active Directory Managed Identity`.
- Implement via a scoped `DbContextOptions` factory that reads `ITenantCatalog` for the current
  request (configure connection in a scoped provider, not `OnConfiguring` statics). Keep
  `EnableRetryOnFailure`.
- `sharedSqlServer` comes from App Configuration (`Edulynk:Sql:Server`).
- **Done:** `TenantConnectionStringBuilder` (pure, MI string, no password — 4 tests) +
  `ITenantConnectionStringProvider` (reads the request's resolved tenant via the resolver — memoized by
  the middleware so the sync read never blocks — **fails closed** when no provisioned tenant DB; 3
  tests). `AddDbContext` now builds its connection per request via the provider. Removed the startup
  DB-seeding block (incompatible with per-tenant DBs — moves to 2.6 provisioning). Added
  `Edulynk:Sql:Server`.
- **Capstone (DONE):** deleted `GetOrganizationConfiguration`(+interface), `OrganizationConfiguration`,
  `AzureAdConfiguration` — nothing referenced them after 2.1+2.3. This **removed the `Lagetronix1#`
  plaintext SQL password from the codebase entirely**. (The `appsettings.json` `DefaultConnection`
  password remains — still on the Phase 7 rotation list; goes once 2.6/2.8 settle design-time
  migrations + secrets.)

### 2.4 Student ceiling enforcement  ✅ DONE (TDD, `saas-kit-integration`)
- New `IStudentQuotaService`: calls Admin API `GET /tenants/{id}/quota` (cached ~5 min) → `maxStudents`.
- Enforce at **student create** and **bulk import** in `StudentRepository`/`StudentService`: if
  `currentCount + incoming > maxStudents` → throw `StudentLimitExceededException` → API returns **403**
  with an upgrade message. **Fail-closed:** `maxStudents == 0` blocks every student (matches the
  platform's unmapped-tier behavior) — there is no "unlimited" interpretation of 0.
- `currentCount` is a cheap `COUNT(*)` on the tenant DB.
- **Done:** `StudentCeiling` (Domain, pure fail-closed rule: 0 blocks all, allows up to & including the
  ceiling, zero-add is a no-op — 5 tests) + `StudentLimitExceededException`. `StudentQuotaService`
  (`Data/Platform`) reads the ceiling for the resolved tenant, cached; fail-closed (unresolved / 404 →
  0; 4 tests). `StudentService` overrides `CreateAsync`/`ImportAsync` to enforce before the base write
  (count via new `IStudentRepository.CountAsync`; 3 tests). `StudentLimitExceededMiddleware` maps the
  exception → 403 + message (2 tests). Quota client uses the same Admin base URL — s2s auth header is
  Phase 2.8 (401s until then).

### 2.5 RBAC (per-request permissions)  ✅ DONE (TDD, `saas-kit-integration`)
- New `IPermissionResolver`: calls Permissions API
  `GET /api/Permissions/GetUserPermissionsForTenant?tenantId&userId` (already exists) → permission
  strings; cached per (tenant,user) for the request/session.
- Define the Edulynk **role→permission catalog** in code (e.g. `Role:Bursar` → `fee.post`,
  `student.read`, …). Translate roles to permission strings on assignment.
- Add ASP.NET Core **authorization policies** mapping endpoints to required permissions; replace the
  lone `[Authorize]` with policy attributes. Controllers gate on permissions, not Entra roles.
- **Done — engine:** `PermissionResolver` (`Data/Platform`, reads the caller's perms for the resolved
  tenant, cached, fail-closed; 3 tests) + `SaasPermissionRequirement`/`PermissionAuthorizationHandler`
  (`*` = full access; 3 tests) + `SaasPermissionPolicyProvider` (`[Authorize(Policy="perm:<x>")]` →
  requirement + RequireAuthenticatedUser; 3 tests). DI-registered; client base URL
  `Edulynk:PermissionsApiUrl` (x-api-key is Phase 2.8 — until then resolver returns none = fail-closed).
- **Done — catalog (decided: Admin + Bursar starter):** `EdulynkPermissions` (vocabulary) +
  `EdulynkRoleCatalog` (Admin → `*`; Bursar → `fee.read`,`fee.post`,`student.read`,`transaction.read`;
  4 tests).
- **Done — decoration (decided: key controllers now):** `StudentsController` (read=student.read,
  write=student.write) + `FeePaymentsController` (read=fee.read, mutate=fee.post). **Remaining (follow-up
  pass):** decorate the other controllers (academics, other fee/transaction controllers, core lookups)
  once the matrix settles in use.

### 2.6 Provisioning endpoint (called by Phase 1.3)  ✅ DONE (TDD, `saas-kit-integration`)
- New `POST /internal/tenants/{tenantId}/provision` `{ databaseName }`, authorized **app-only**
  (the platform's service token; see 4.4) — never user-callable.
- Steps (idempotent): `CREATE DATABASE [name] (EDITION='Basic')` if absent → run EF
  `Database.Migrate()` against it → seed reference data (countries, banks, ERP defaults via the
  existing `DatabaseSeeder`). Returns 200 when the DB is ready (safe to replay).
- Requires the API's managed identity to have **CREATE DATABASE** rights on the server (server AAD
  admin or `dbmanager`); provisioning also runs `CREATE USER [mi] FROM EXTERNAL PROVIDER` + role on
  the new DB so subsequent runtime connections (2.3) work.
- **Done:** `TenantProvisioningController` (`/internal`, `[Authorize]` — 4.4 tightens to the
  provisioning app-role; 1 test) → `TenantProvisioningService` orchestrator (validates name,
  EnsureDatabaseExists → MigrateAndSeed in order, idempotent/replay-safe; 2 tests). Real adapter
  `SqlTenantDatabaseProvisioner` (CREATE DATABASE Basic via MI → EF `MigrateAsync` → `DatabaseSeeder`;
  name-guarded). `TenantResolutionMiddleware` bypasses `/internal` (1 test). **Decision:**
  sync-with-retry — the endpoint is idempotent; transient-retry lives in the platform HTTP caller (1.3),
  not here. **Deferred to Phase 5 (integration, real SQL):** validate the DDL/migrate path + add the
  runtime MI `CREATE USER … FROM EXTERNAL PROVIDER` grant on the new DB (TODO in
  `SqlTenantDatabaseProvisioner`). **Platform side still pending (1.3 + 4.4):** swap the
  `NoopProductProvisioningService` for the real `HttpProductProvisioningService` that POSTs here.

### 2.7 Tenant Settings (Power BI etc.)  ✅ DONE (TDD, `saas-kit-integration`)
- New `TenantSetting` table in the tenant DB (or a small per-tenant settings row) holding the school's
  Power BI workspace/report IDs and branding.
- `GET/PUT /api/tenant-settings` (admin-gated) so values are managed in-product, not env.
- Power BI embed-token route reads workspace/report IDs from settings, not `process.env`.
- **Done (decided: key/value bag):** `TenantSetting {Id,Key,Value}` (`cor_tenant_settings`, unique Key)
  — new reports/branding need no schema change. `TenantSettingsService` (get→dict, set→upsert; 3 tests
  via EF InMemory). `TenantSettingsController` `GET/PUT /api/tenant-settings` gated on
  `tenant.settings.read`/`write` (admin-only — only Admin's `*`; 2 tests). Added
  `ApplicationDbContextFactory` (`IDesignTimeDbContextFactory`) so EF tooling can build the per-request
  context. The FE switch (3.3) consumes `GET /api/tenant-settings` instead of `process.env`.
- **⚠ Migration deferred to Phase 5 (real infra):** `dotnet ef migrations add` scaffolded **323
  destructive ops** — the Educ8e **migration chain is drifted from the model** (stale snapshot; not the
  schema source of truth). So provisioning's `Database.MigrateAsync()` (2.6) will **not** build a correct
  fresh schema. Phase 5 must decide the schema-provisioning strategy (baseline migration vs
  `EnsureCreated`) and create `cor_tenant_settings` (+ all tables) in real tenant DBs. Feature logic is
  complete + tested via InMemory.

### 2.8 Service-to-service auth & secrets  ✅ DONE (TDD, `saas-kit-integration`)
- Edulynk API → Admin/Permissions APIs: **client-credentials** app token (the endpoints take
  tenantId/userId as params, so no OBO needed). Edulynk API app reg gets an **app role** on the Admin
  API and Permissions API (see 4.3).
- Remove all plaintext secrets from `appsettings*.json`; SQL via MI, Service Bus connection via Key
  Vault reference, config via App Configuration (managed identity), mirroring the other ASDK apps.
- **Done:** `BearerTokenHandler` (DelegatingHandler) attaches an app-only client-credentials token (via
  `IServiceTokenProvider` → `ITokenAcquisition.GetAccessTokenForAppAsync`, MSAL-cached) to the Admin API
  clients (`ITenantCatalog`, `IStudentQuotaService`); `ApiKeyHandler` attaches `x-api-key` to the
  Permissions client (`IPermissionResolver`) — matching how the platform's own Admin service calls
  Permissions. Enabled `EnableTokenAcquisitionToCallDownstreamApi` + in-memory token cache. Config:
  `Edulynk:AdminApiScope` + `PermissionsApiKey` (placeholders → Key Vault / App Configuration at deploy).
  2 handler tests.
- **Secrets:** removed the dead plaintext `ConnectionStrings:DefaultConnection` (`P@ssword`) — the
  **second** SQL password, now gone (both out of the codebase: `Lagetronix1#` went in 2.3). The Service
  Bus SAS key stays in `appsettings` (read at 6 call sites via config) → **Key Vault reference at
  deploy, no code change** (Phase 5/7).
- **Cross-repo/deploy remaining:** client secret on the Edulynk app reg + app role on the Admin API
  (Phase 4.3) so the bearer token is issued/accepted; the Admin API accepting app-only tokens (4.3).
  Until then the three clients are fail-closed.

### Tests (Phase 2)
- Unit: tenant resolver rejects unknown `tid` / suspended; quota service blocks at ceiling and blocks
  entirely when `maxStudents == 0`; permission resolver maps roles→permissions; provision is idempotent.
- Integration: spin an empty SQL DB, run provision, assert schema + seed; student-create returns 403
  at ceiling. Use the existing `Lagetronix.Connector.Tests` project.

---

## Phase 3 — Edulynk Web (FE) (Next.js)  🟡 IN PROGRESS (FE repo `Educ8e Connector - FE-1`, branch `saas-kit-integration`)

> **Test harness added:** the FE had no test runner — added **Vitest + React Testing Library** (jsdom),
> config + `test` scripts. FE work is TDD'd where it's unit-testable (logic/hooks); NextAuth/config is
> typecheck-verified. FE suite: 10 green.
>
> **⚠ Pre-existing build issue found:** `app/api/auth/[...nextauth]/route.ts` has a stray
> `export const authOptions = {…}` that violates Next's route-module contract (`.next/types` TS2344) —
> likely breaks `next build`. Predates this work; remove it during the 3.2 pass.

### 3.1 Multitenant sign-in  ✅ DONE (typecheck-verified)
- `app/api/auth/[...nextauth]/route.ts`: `AzureADProvider` `tenantId: "organizations"`; client id =
  the **saas-app** registration; add the Edulynk API scope (`api://f41f679b…/access_as_user`, the
  reused API reg per 4.2) to request a token usable against the API.
- Add the FE redirect URIs to the saas-app registration (4.1).
- **Done:** `tenantId` defaults to `"organizations"`; scope now requests the API scope (configurable via
  `AZURE_AD_API_SCOPE`). Kept `decoded.roles` for now so the current role UI works until the 3.2
  call-site migration. *Validation needs the saas-app reg redirect URIs + API reg pre-auth (4.1/4.2).*

### 3.2 Permission-based gating (replace Entra roles)  🟡 PARTIAL
- `app/context/roleSelection-context.tsx` + `lib/withAuth.tsx`: stop reading `decoded.roles` from the
  Entra token. Instead fetch permissions from the Edulynk API (which proxies the Permissions API for
  the signed-in user+tenant) once per session; gate UI on permission strings.
- Keep the role *concept* for display ("you are a Bursar"), derived from the assigned role string.
- **Done (core, TDD):** `lib/permissions.ts` `hasPermission` (`*`=all, OR-lists, fail-closed; 5 tests);
  `lib/permissions-context.tsx` `PermissionsProvider`/`usePermissions` (injected loader, fail-closed;
  2 tests); axios **bearer-token interceptor** (`data/api/authInterceptor.ts`, from the NextAuth session;
  3 tests) — the FE previously sent **no token** to the API; `permissions.me()` + `loadUserPermissions`.
  **Backend (API branch):** `GET /api/me/permissions` (`MeController`, reuses `IPermissionResolver`;
  1 test).
- **Remaining (the big, entangled pass):** wire `PermissionsProvider` into the app provider tree; migrate
  all call sites off role-based `hasPermission`/`selectedRole`/`availableRoles` (`roleSelection-context`,
  `withAuth`, sidebar, dashboards) to `can(...)`; keep role for display. Validate with `next build`.

### 3.3 Tenant Settings instead of env
- Power BI report/workspace IDs (the ~12 `*_REPORT_ID` env vars) move to a `GET /api/tenant-settings`
  call (Phase 2.7). FE env keeps only: `AZURE_AD_CLIENT_ID/SECRET`, `AZURE_AD_TENANT_ID=organizations`,
  `AUTH_SECRET`, `NEXT_PUBLIC_API_URL`.

### 3.4 Config on SWA
- Server-side values → **SWA application settings** (set by the FE pipeline). `NEXT_PUBLIC_*` baked at
  build time (after 3.3 that's just the API URL). Verify NextAuth runs on SWA's hybrid Next.js
  **early** (low risk — Educ8e already deploys to SWA — but confirm before building on it).

---

## Phase 4 — Identity & app registrations (Workforce)

### 4.1 saas-app reg → the FE
Retarget to multitenant; add FE redirect URIs (`/api/auth/callback/azure-ad`,
signout). This is the FE's sign-in identity.

### 4.2 Reuse the existing API reg `f41f679b…` (flip to multitenant)
Reuse the existing Educ8e API app registration as the Edulynk API identity rather than minting a new
one. Changes on `f41f679b…`:
- Set **`signInAudience` = `AzureADMultipleOrgs`** (single-tenant → multitenant).
- Ensure the **Application ID URI** is `api://f41f679b…` and the exposed scope is
  `access_as_user` (the API already validates `aud = api://f41f679b…`, so the audience is unchanged —
  only multitenancy is added). Remove any stale single-tenant-only scopes/URIs.
- **Pre-authorize the saas-app (FE) client** on `access_as_user` so the FE can request API tokens.
- Verify the API's token validation uses `TenantId = "organizations"` (Phase 2.1) so multi-tenant
  issuers are accepted.

> Reuse subtlety (why this isn't free): multitenant + the app's existing consent state means existing
> single-tenant grants stay, but new customer tenants must consent on first sign-in. Confirm no
> leftover B2C/External-ID reply URLs or exposed scopes linger on the reg before flipping.

### 4.3 Service-to-service app roles
Define an app role (e.g. `Service.Access`) on **admin-api** and **permissions-api**; grant it to the
Edulynk API app (`f41f679b…`, admin-consented) so Edulynk's client-credentials token is accepted
(Phase 2.8). The Admin API must be configured to accept app-only tokens carrying that role for the
quota/provision/bind endpoints. Reusing the reg means this client-credentials flow needs a **client
secret / certificate** on `f41f679b…` — the old single-tenant API never needed one. Create it here,
store it in Key Vault, and add it to the Phase 7 secret-rotation list (rotate after testing).

### 4.4 Provisioning identity
The platform → Edulynk provision call (1.3) uses the Admin API's identity with an app role on the
Edulynk API reg (`f41f679b…`, `Provisioning.Write`). Internal provision endpoint authorizes that role
only.

### 4.5 Retire old reg
Only the **old FE app registration** is retired after cutover. The API reg `f41f679b…` is **kept and
reused** (4.2). Confirm nothing else still depends on the old FE reg before deleting it.

---

## Phase 5 — Infra & deploy

### 5.1 Per-tenant SQL
- One Azure SQL **logical server** (shared) + the Edulynk API's **managed identity** set as the
  server's **Microsoft Entra admin** (or granted `dbmanager`/`loginmanager`) so it can CREATE DATABASE
  and add MI users at provision time.
- Databases created at runtime (Phase 2.6) as standalone **Basic** DBs. (Elastic pool deferred — DBs
  migrate into a pool unchanged later if volume warrants.)

### 5.2 Reuse the existing Edulynk API App Service + add a deploy pipeline
The Edulynk API is **already deployed** on Azure — do **not** create a new App Service. We re-architect
the code and rewire config onto the existing host (keeps its DNS, TLS, monitoring, app settings). What
to add/change on the existing app:
- **Managed identity:** enable system-assigned MI if absent; grant it `Key Vault Secrets User` on the
  platform Key Vault and the SQL rights from 5.1 (CREATE DATABASE + per-DB user).
- **App Configuration:** add the platform App Configuration endpoint as an app setting so it reads
  `Edulynk:*` config.
- **Pipeline:** the current `azure-pipelines.yml` is **build-only** (publishes a `drop` artifact;
  deploy is a separate/classic release). Replace it with a consolidated build-and-deploy YAML modeled
  on the shared `templates/deploy-app.yml` (project `…/Lagetronix.Connector.API.csproj`,
  `appType: webAppLinux`), pointing `AzureWebApp@1` at the **existing** app name, with its own path
  filters.

**Confirmed placement:** the existing App Service is **Linux** and in the **same subscription** as the
platform — so no cross-subscription RBAC, the template default `webAppLinux` applies, and the
per-tenant **SQL server (5.1) is co-located in that same subscription/region**.

**App registration (decided):** the existing API reg `f41f679b…` is **reused**, flipped to multitenant
(Phase 4.2) — audience unchanged (`api://f41f679b…`), so the API needs only the `signInAudience` change
plus a client secret for the service-to-service flow.

> Create a *new* App Service only if you deliberately want clean separation from the legacy
> single-tenant deployment.

### 5.3 FE pipeline (SWA)
- `.azuredevops/edulynk-web.yml` using the `AzureStaticWebApp` task (crib from the existing SWA
  workflow files in the FE repo). `NEXT_PUBLIC_*` injected at build; server settings via SWA app
  settings.

### 5.4 Config & secrets
- App Configuration: `Edulynk:Sql:Server`, `Edulynk:AdminApiUrl`, `Edulynk:PermissionsApiUrl`, the
  edulynk-api client id, label `ver0.8.0`.
- Key Vault: Service Bus connection, the saas-app/edulynk-api client secrets, any Power BI SP secret.
- Admin API config: `Marketplace:TierMaxStudents`, `Marketplace:PlanToProductTier`,
  `Edulynk:ProvisionUrl`.

---

## Phase 6 — Partner Center offer
- Plans (Basic/Standard/Premium) with finalised prices and **student ceilings** → `TierMaxStudents`.
- Technical Configuration: landing →
  `https://signupadmin-app-<postfix>…/marketplace/landing`; webhook →
  `https://admin-api-<postfix>…/api/marketplace/webhook`; AAD app = publisher fulfillment app.
- Preview audience = a dev account; run a real preview purchase end-to-end.

---

## Phase 7 — Data migration, cutover, security
- **Migrate the existing Lagetronix data**: stand up its Tenant + DB on the new model, import from
  `lagetronix_rapha_dev`. (One-off script; verify counts.) **Map its ProductTier in
  `Marketplace:TierMaxStudents`** (and tier 0 if any non-marketplace tenants exist) — otherwise the
  fail-closed quota (1.1) blocks all student registration for that tenant.
- **Rotate** the secrets that were committed in git history. Both plaintext **SQL passwords are now
  removed from the codebase** (`GetOrganizationConfiguration.cs` `Lagetronix1#` deleted in 2.3;
  `appsettings.json` `DefaultConnection` `P@ssword` removed in 2.8) — but **rotate them anyway** (they
  live in history) and switch to managed-identity SQL (done in 2.3). The **Service Bus SAS key** is
  still in `appsettings.json` (6 call sites read it via config) → move to a **Key Vault reference** at
  deploy + rotate. Plus any chat-shared secrets.
- **New `f41f679b…` client secret:** the reused API reg needs a client secret/certificate (added in
  4.3) for the service-to-service calls — the old single-tenant API never had one. Store it in Key
  Vault and add it to this rotation list (rotate after testing, per the standing secret-hygiene rule).
- **Retire** the old Educ8e **FE** app reg (4.5); decommission the old single-tenant deploy. The API
  reg `f41f679b…` is **kept** (reused per 4.2).
- Finish the paused email-notification feature (Exchange 365 mailbox creds).

---

## Cross-cutting risks & open issues

1. **Cross-tenant Graph (resolved, see 1.5):** invite-by-email and member listing currently need the
   publisher's Graph into the customer tenant — impossible under Workforce multitenant. Mechanism fully
   specified in 1.5 (JIT bind: Pending `TenantInvitation` + `/members/bind` at first sign-in + local
   member identity). Remaining build cost: 2-table schema add, one endpoint, one FE login hook. Residual
   edge to watch: token-email vs invited-alias mismatch (handled by the "contact admin / manual bind"
   path).
2. **App-only tokens on the Admin API:** the Admin API today expects user OBO tokens with scopes.
   Accepting Edulynk's client-credentials token for quota/provision/bind needs an app-role auth policy
   (4.3) — confirm the Admin API's `JwtBearer`/Identity.Web config supports both.
3. **Per-request DbContext + connection pooling:** resolving the connection per request must not break
   EF pooling or leak across tenants; use a scoped options factory, not static `OnConfiguring`.
4. **MI privilege to CREATE DATABASE:** making the API's MI the server AAD admin is broad; consider a
   dedicated provisioning identity with `dbmanager` only.
5. **SWA hybrid + NextAuth:** verify first (3.4).
6. **Provisioning latency/failure UX:** Basic DB create + migrate + seed can take a minute; decide
   sync-with-retry vs Service Bus queue (1.3) and what the buyer sees meanwhile. **Resolved (2.6):**
   sync-with-retry — the Edulynk endpoint is idempotent; transient-retry lives in the platform's HTTP
   caller.
7. **⚠ Edulynk migration chain is drifted (found in 2.7):** `dotnet ef migrations add` scaffolds ~323
   destructive ops — the model evolved far past the only migration's snapshot, so **migrations are not
   the schema source of truth** and `Database.MigrateAsync()` (used by provisioning 2.6) will not build
   a correct fresh tenant schema. Until fixed, per-tenant DBs can't be stood up automatically.
   **DECISION (locked, do in Phase 5): re-baseline.** Delete the stale `InitialMigration` + snapshot,
   generate one fresh baseline migration from the current model, and **validate it reproduces the
   existing `lagetronix_rapha_dev` schema** (diff against the live DB so existing data isn't disturbed)
   before relying on it. Keep migrations as the source of truth thereafter, so `MigrateAsync`-based
   provisioning (2.6) is correct and future schema changes migrate incrementally. (Rejected:
   `EnsureCreated()` — blocks future incremental migrations; DACPAC — extra artifact to maintain.) Must
   run against real Azure SQL (Phase 5 infra: shared server + MI `CREATE DATABASE`).

## Suggested sequencing (milestones)
- **M1 (platform-only, shippable):** Phase 1 + Phase 4.1–4.4. Testable with the existing marketplace
  suite; no Edulynk dependency.
- **M2 (Edulynk API on one tenant):** Phase 2 against a manually-created DB; prove auth + DB + gate +
  RBAC for the migrated Lagetronix tenant.
- **M3 (self-service):** wire provisioning (1.3 ↔ 2.6), Phase 3 FE, Phase 5 infra/pipelines.
- **M4 (go-live):** Phase 6 offer + Phase 7 migration/cutover/security.
