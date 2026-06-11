# Per-product deployment runbook

This repo is a **reusable Azure Marketplace SaaS template**. It is not a drop-in library — it is a
multi-service platform (4 App Services + infra) that you **redeploy per product**, swapping your
product UI into the provided shell. Edulynk is product #1. This is the checklist for product #2+.

## Architecture recap
- **4 apps:** Admin API (tenants + marketplace fulfillment + webhook), Permissions API (Graph-backed
  roles), Sign-up Administration Web (onboarding wizard + marketplace landing + publisher console),
  SaaS App (`Saas.Application.Web` — the thin product shell you replace).
- **Identity:** multitenant Microsoft Entra **Workforce** only (External ID/B2C removed). Tenant keyed
  on the customer `tid` claim.
- **Marketplace:** vendored fulfillment accelerator in `src/Saas.Marketplace`; flat per-seat billing;
  1 subscription = 1 tenant; gated on subscription status.
- **Config-driven:** inputs in gitignored `config/config.json` (template:
  `src/Saas.Identity/Saas.IdentityProvider/deployment/config/config-template.json`); secrets in
  gitignored `config/external-id.secrets.json` + App Configuration/Key Vault.

## Prerequisites (per product)
- **Azure:** a subscription + rights to create a resource group; a home Entra tenant where you can
  create app registrations and grant admin consent.
- **Partner Center:** a Commercial Marketplace publisher account; a new SaaS offer (manual-activation,
  flat per-seat plans); a publisher Entra app + service principal for fulfillment.
- **Tooling:** WSL/Ubuntu + Docker (the infra deploy is containerized), `az` CLI, .NET 8 SDK.

---

## Step 1 — Customize the product code
1. **Swap your product into `src/Saas.Application/Saas.Application.Web`.** Search `SWAP-IN SEAM`:
   replace the `Pages/Index.cshtml` branches (per-tenant home + landing) and the branding in
   `Pages/Shared/_Layout.cshtml`. **Keep the seam** (see that project's `README.md`): route →
   `ITenantService.GetTenantInfoByRouteAsync` → Admin API `tenantinfo/{route}`; the
   `RequireActiveSubscriptionMiddleware`; and the Entra auth + `IAdminServiceClient` wiring.
2. **Tier catalog:** set `ReferenceData.ProductServicePlans` + `SR` plan-name constants
   (`src/Saas.SignupAdministration/Saas.SignupAdministration.Web`) to your product's plans/tiers.
3. **Plan→tier map:** you'll set `Marketplace:PlanToProductTier` (purchased marketplace plan id →
   internal ProductTier id) in config (Step 3).

## Step 2 — Create identities (manual, in the portal)
App registrations are created manually (B2C end-of-sale legacy). In your home tenant create **four new
multitenant Workforce apps**:

| App | Notes |
|---|---|
| admin-api | Expose the `tenant.*` scopes under **Application ID URI `api://{clientId}`**. The Admin API's `:AzureB2C:Audience` MUST be `api://{clientId}` (the v2 token's `aud`), not the bare GUID — else IDX10214/401. |
| permissions-api | Graph **app roles** `User.Read.All` + `Application.ReadWrite.OwnedBy`, admin-consented. |
| signupadmin-app | Web; redirect URIs `…/signin-oidc` + `…/signout-callback-oidc`; client secret. Pre-authorized to admin-api's scopes. |
| saas-app | Web; redirect URIs `…/signin-oidc` + `…/signout-callback-oidc`; client secret; needs admin-api `tenant.read`. |

Also create a **separate publisher app + SP** for marketplace fulfillment (client-credentials; the SaaS
Fulfillment API scope is fixed and handled by the SDK). Its tenantId/appId go into the offer's Technical
Configuration (Step 5) and into config (Step 3).

> The `publisher console` is gated by an **`Owner` app role** on `signupadmin-app` plus
> `Marketplace:OwnerRole=Owner` — define the role, assign owners, or leave the role unset to allow any
> publisher-tenant user.

## Step 3 — Fill config
In gitignored `config/config.json` (copy from the template):
- `initConfig.naming.solutionPrefix` + `solutionName` → a **new postfix** (drives every resource name),
  `initConfig.location`.
- `initConfig.entraExternalId.tenantId` + `tenantDomain` + the four `apps.*` **client IDs** from Step 2.
- `marketplace`: `publisherTenantId`, `publisherClientId` (the fulfillment app), `offerId`, `saasAppUrl`,
  `planToProductTier` (e.g. `{"basic":6,"standard":7,"premium":8}`).

**Secrets — never in config.json:**
- The 4 app client secrets → gitignored `config/external-id.secrets.json`.
- Provision into App Configuration / Key Vault out-of-band: `Marketplace:PublisherClientSecret` and
  `Sql:MarketplaceSQLConnectionString` (the marketplace feature is all-or-nothing — the Admin API throws
  at startup if PublisherTenantId/ClientId/ClientSecret are not all present).

## Step 4 — Deploy
1. **Infra (WSL container flow):** run the identity-foundation deploy, then each app's infra deploy
   (`run.sh` from each `deployment/` dir). Creates the RG, App Configuration, Key Vault, SQL Server,
   the 4 App Services, and the user-assigned managed identity (which gets `Key Vault Secrets User`).
2. **Marketplace DB:** create the marketplace SQL DB and wire the `Marketplace:*` keys + the two secrets
   above; restart admin-api (it migrates the vendored accelerator schema on boot).
3. **App code (Azure DevOps):** the `.azuredevops/` pipelines deploy the 4 apps. They are wired to **this**
   product's web-app names + the `asdk-azure` service connection, so a new product needs **its own
   pipelines + service connection + naming** (clone `.azuredevops/*.yml`, change `webAppName` +
   `azureSubscription`), ideally in its own ADO project. See `PORTING-TO-AZURE-DEVOPS.md`.

## Step 5 — Partner Center
Configure the offer's **Technical Configuration**:
- Landing page URL → `https://signupadmin-app-<postfix>.azurewebsites.net/marketplace/landing`
- Connection webhook → `https://admin-api-<postfix>.azurewebsites.net/api/marketplace/webhook`
- Azure AD tenant + app id → the **publisher fulfillment** app (must match `Marketplace:Publisher*`).

Publish a **preview** offer, add your dev account to the preview audience.

## Step 6 — Verify
- Health: admin-api `/api/marketplace/subscriptions` → 401; permissions → 401; signupadmin `/` → 200;
  saas-app `/` → 200.
- Sign in to signupadmin; `/Publisher/Subscriptions` (Owner) loads.
- Preview purchase → lands on `/marketplace/landing` → onboarding (service-plan step skipped) → tenant
  created → **Activate** fires → subscription `Subscribed`, linked to the tenant.
- A lifecycle webhook (ChangeQuantity / Suspend / Reinstate) updates status; Suspended → saas-app 403.
- saas-app `/{route}` resolves the provisioned tenant.

---

## Gotchas (learned the hard way)
- **Admin API audience:** token `aud` is `api://{clientId}` → set `AdminApi:AzureB2C:Audience` to the
  `api://` form (the deploy emitter now does this). Bare GUID → IDX10214 → 401 on every OBO call.
- **NuGet strictness on hosted build agents:** a clean restore on a current SDK escalates transitive
  version conflicts to hard **NU1107** that local/older builds tolerate as NU1608 warnings. The Admin
  API pins `Microsoft.CodeAnalysis.Common 4.14.0` and `System.Text.Json 9.0.6` directly to unify the
  graph; expect similar direct pins if you bump packages.
- **Infra deploy is interactive**, not one-button: register `Microsoft.ContainerInstance`; on the WSL
  host use a plaintext MSAL cache (`az config set core.encrypt_token_cache=false` + re-login) so the
  container can read it; run each app's `run.sh` from its own `deployment/` dir (`source ./constants.sh`
  is relative).
- **Legacy cruft** in `config-template.json`: the `azureb2c` block + `IdentityExperienceFramework` /
  `ProxyIdentityExperienceFramework` app registrations are **inert** (Workforce is used now) but kept;
  `:AzureB2C:` config-key names are cosmetic holdovers. Safe to ignore; candidate for cleanup.
- **Dormant/optional:** email notifications need SMTP (`Marketplace:Notifications:*`); a
  convention-based tier scheme (plan id == tier slug) would remove the `PlanToProductTier` map for
  multi-product reuse — not yet done.
