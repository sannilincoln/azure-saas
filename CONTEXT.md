# Edulynk-on-ASDK Integration

The Educ8e Connector product (a .NET API + Next.js frontend, school fee/ERP connector) is being
re-platformed onto the reusable Azure SaaS Dev Kit (ASDK) marketplace template. This glossary fixes the
shared language across the two codebases, which use overlapping words for different things.

## Language

**Tenant**:
An ASDK-provisioned customer organization, keyed on the Entra `tid` claim and addressed by a URL `route`.
One marketplace Subscription provisions exactly one Tenant. This is the canonical word.
_Avoid_: Organization, Org, Customer, School (those are Educ8e-side or business words for the same thing).

**Organization** (legacy Educ8e term):
Educ8e's `OrganizationConfiguration` record — bundled per-org Entra config + per-org database
coordinates. Being replaced by **Tenant** + centralized config; do not carry the term forward.

**Seat / User**:
A human who signs in (an Entra identity assigned to a Tenant). Counted by ASDK's `MarketplaceSeatService`.
Distinct from Student.
_Avoid_: Member, Account.

**Student**:
A domain row in the Edulynk app database (`DbSet<Student>`). NOT a login identity. The metric the
marketplace plan caps. A Tenant has many Students and a (usually small) number of Seats.

**Plan**:
A marketplace offer tier the customer purchases (e.g. basic/standard/premium). Maps to an internal
ProductTier and to a **Student ceiling**.

**Student ceiling**:
The maximum number of Students a Tenant may hold, determined by its purchased Plan via a
plan -> maxStudents map (config-driven, like the existing `Marketplace:PlanToProductTier`). Enforced by
counting Student rows at create time. Independent of the marketplace `Ampquantity` (seat) value.

**Permission**:
A capability string scoped to a `(Tenant, Seat)` pair, stored in ASDK's `SaasPermission` table
(`TenantPermission` / `UserPermission` kinds). The authoritative authorization fact. Resolved at
request time via the Permissions API (`GetUserPermissionClaimsForTenant`) — NOT carried as an Entra
app-role claim (that path died with B2C and does not work for multitenant Workforce users).
_Avoid_: App role, Entra role.

**Role**:
A named bundle of Permissions meaningful to school staff (e.g. Bursar, Registrar, Admin). A
convenience grouping over Permissions; stored as a permission string and expanded by the app.
_Avoid_: Entra App Role (the old, now-removed mechanism).

**Tenant Admin**:
The Seat created when a Subscription is activated (the purchaser). Holds the `Admin` tenant
permission; the only Role allowed to invite Seats and assign Roles within the Tenant.

**Tenant Database**:
A per-Tenant Azure SQL database (standalone **Basic DTU** tier per database) holding that school's
Edulynk data (Students, fees, transactions). One Tenant = one Tenant Database; created at
subscription activation. Accessed via managed identity — per-tenant SQL credentials do not exist.

**Catalog**:
The mapping from a Tenant to its Tenant Database (the database name on the ASDK Tenant record).
Resolved per request from the token's `tid`.
_Avoid_: OrganizationConfiguration (the retired hardcoded Educ8e list).

**Tenant Settings**:
Per-Tenant customization values (e.g. a school's Power BI workspace/report IDs), stored in the
database and served by the Edulynk API. Distinct from deployment config (App Configuration) and
secrets (Key Vault) — if a value differs per school, it is a Tenant Setting, never an env var.
_Avoid_: Env var, app setting (for per-school values).

**Invite**:
Granting an existing Microsoft work identity (a user in the customer's own Entra tenant) access to a
Tenant, with a Role. Creates no credential — staff bring their own Microsoft accounts (Workforce
multitenant; Edulynk never provisions logins).
_Avoid_: Create user, register user.
