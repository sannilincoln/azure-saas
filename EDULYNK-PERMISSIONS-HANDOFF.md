# Edulynk — `/me/permissions` 500 + empty-permissions: diagnosis & fix (handoff)

**Date:** 2026-06-30
**Author:** prior session (Claude Code)
**Scope:** the Edulynk (Educ8e Connector) product on the ASDK marketplace template. Covers why
`GET /api/me/permissions` returns **500**, why permissions come back **empty** even after that, and the
**exact fixes**. No application code changes are required — the remaining work is a **deploy + two app
settings** plus optional data checks.

> **Standing constraint:** the restructured Educ8e Connector **API** is the *canonical* structure/patterns
> (`Result<T>` envelope, `PermissionPolicy.*` constants, layered Domain/Service/Data/API). Match it; do not
> scatter ad-hoc types into controllers. The FE repo has its own conventions.

---

## Repos / branches / key commits

| Repo | Path (local) | Branch | Live remote |
|---|---|---|---|
| API / BFF (Educ8e Connector) | `Educ8e Connector` | `saas-kit-integration` | **`devops`** (ADO `oadesanya/Edulynk/_git/Educ8e Connector`). ⚠ `origin` is **dead** (old project "ERP Integration" renamed) |
| FE (Next.js) | `Educ8e Connector - FE-1` | `saas-kit-integration` | `origin` (ADO `oadesanya/Edulynk/_git/Educ8e Connector - FE`) |
| Platform (ASDK Admin/Permissions/SignupAdmin) | `AzureSaas` | `main` | GitHub `sannilincoln/azure-saas` |

API fix commits already on `saas-kit-integration` **HEAD `0f4a13e`**:
- `000847c` — *Fix /api/me/permissions 500: fail closed when the control plane is unreachable/unauthorized*
- `0f4a13e` — *Exempt /api/me/* from the tenant gate so permission discovery works without a resolved tenant*

FE reconciliation commits on `saas-kit-integration`:
- `f5a5070` — Admin Settings page (Power BI) + unwrap `Result<T>` envelope for tenant-settings
- `4e00ed6` — unwrap `Result<T>` on `me/permissions` + `team/*` (the endpoints the restructure broke)

---

## Symptom

`GET /api/me/permissions` → **500**; FE shows "please select a role to continue". Test user
`test@pvpro.com.ng` (`oid 5d785d7b…`), token **v1**, `tid = e77b3a11-2e95-4f9e-973b-918d466ba68d`
(this tenant is the **publisher/home** tenant; `pvpro.com.ng` is an org **under** the publisher, so it
shares that tid), `aud = api://6a3e6083…`, `scp = access_as_user`. Token is valid.

---

## Root cause #1 — the 500 (ALREADY FIXED IN CODE; needs deploy)

The restructure replaced the old **local** tenant lookup with a hard **live S2S call**
(`RequestTenantResolver → ITenantCatalog → GET api/tenants/by-tid/{tid}` on the Admin API). The catalog/
resolver originally treated only **404** as "not found" and threw on everything else, so any **401/403/5xx**
(typically the app-only token not yet accepted) became an unhandled exception → **500 on every
authenticated request**.

**Fixed** in `000847c`: `TenantCatalog.FetchAsync` and `PermissionResolver.FetchAsync` now fail-closed on
**any** non-success (return null / no permissions, never throw). The whole `GetPermissionsAsync` chain is
now defensive (unresolved tenant → empty, empty user → empty, `EnsureBoundAsync` try/catch, `FetchAsync`
soft-fail). **A build at HEAD returns 200 with an empty list, never 500.**

➡️ **The 500 means the deployed `educ8e-connector-app` is running a build from *before* `000847c`.**
**Action: deploy `saas-kit-integration` HEAD (`0f4a13e`).** This is a deploy, not a code change.

---

## Root cause #2 — empty permissions after the deploy (CONFIG: TWO MISSING APP SETTINGS)

The restructure renamed the control-plane config section to **`SaasKit:*`** (`SaasKitOptions.SectionName
= "SaasKit"`). The **non-secret** values are baked into `appsettings.json` and ship in the build:

```
SaasKit:AdminApiBaseUrl       = https://admin-api-asdk-test-x16w.azurewebsites.net/
SaasKit:PermissionsApiBaseUrl = https://api-permission-asdk-test-x16w.azurewebsites.net/
SaasKit:ServiceTokenScope     = api://2ddb6984-d793-4e10-8133-00c65b81b790/.default
SaasKit:HomeTenantId          = e77b3a11-2e95-4f9e-973b-918d466ba68d
```

But the **two secrets** must come from app settings, and on `educ8e-connector-app` they are present only
under the **old `Edulynk__*` names** (which the canonical code no longer reads):

| Canonical code reads | Deployed app currently has | Status |
|---|---|---|
| `SaasKit__ServiceClientSecret` | only `AzureAd__ClientSecret` | ❌ missing |
| `SaasKit__PermissionsApiKey` | `Edulynk__PermissionsApiKey` | ❌ wrong name |

`SaasKitTokenProvider` builds `ClientSecretCredential(tenant=SaasKit:HomeTenantId,
clientId=AzureAd:ClientId=6a3e6083, secret=SaasKit:ServiceClientSecret)` and **throws if the secret is
empty**. Because `AzureAd:ClientId` is `6a3e6083` and `AzureAd__ClientSecret` is a secret on that *same*
app reg, it is directly reusable (or use the Key Vault secret `edulynk-api-s2s`). Without these two:
no s2s token + no Permissions x-api-key → both calls soft-fail → **empty permissions** (no 500).

### Fix (config only; copies existing values without printing them; restarts the app)

```bash
sec=$(az webapp config appsettings list -n educ8e-connector-app -g Rapha-Backend \
      --query "[?name=='AzureAd__ClientSecret'].value | [0]" -o tsv)
pk=$(az webapp config appsettings list -n educ8e-connector-app -g Rapha-Backend \
      --query "[?name=='Edulynk__PermissionsApiKey'].value | [0]" -o tsv)
az webapp config appsettings set -n educ8e-connector-app -g Rapha-Backend \
      --settings SaasKit__ServiceClientSecret="$sec" SaasKit__PermissionsApiKey="$pk" -o none
```

(Optionally delete the now-unused `Edulynk__*` settings afterwards. Do this **before/with** the HEAD
deploy — if `SaasKitTokenProvider` is constructed eagerly, a missing secret can fail startup.)

### Verified OK (not the problem)
- `Service.Access` app-role grant: Edulynk API SP `6a3e6083` **holds** `Service.Access` (`00e9949e…`) on
  `admin-api` SP `fbd741fe…` (and the permissions-api role `4ede5415…`), admin-consented. ✅

---

## Open items #2 / #3 — data facts (verify AFTER #1+#2, ideally via the live endpoint)

`by-tid/{tid}` resolves: `Subscriptions.Where(PurchaserTenantId == tid).OrderBy(IsActive, CreateDate)` →
its linked `Tenant` (must have `DatabaseName`). `HomeTenantId` does **not** exclude the home tenant from
resolution (it's used only to pick the authority for the app-only token). So for `test@pvpro` (tid
`e77b3a11`) to get permissions:

- **#2** There must be a **Subscription row with `PurchaserTenantId = e77b3a11`** linked to a provisioned
  `Tenant` (with `DatabaseName`) in the **Admin DB** (`sqldb-asdk-test-x16w`).
- **#3** The user (`oid 5d785d7b…`) must be **bound + hold a role** in the **Permissions DB**: the
  purchaser is auto-bound as **Super-Admin** at activation (`*`); anyone else needs an **invite + first
  sign-in JIT bind** (`PermissionResolver.EnsureBoundAsync` POSTs `TenantMembership/BindMember` on the
  first `/me/permissions` call, but only binds if a matching **Pending invitation** exists).

These weren't verified directly (the platform DBs use managed-identity auth; `ssunday@lagetronix.com` is
only Application Administrator, not a SQL principal, and no firewall rule exists for the dev host). They
are **downstream of #1+#2 and become observable** once HEAD is deployed with the two app settings: the
live `/me/permissions` response + App Insights traces on `educ8e-connector-app` will show whether
resolution succeeds and, if empty, whether it's #2 (no subscription) or #3 (not bound / no role).

---

## FE side — already done (no action)

The restructure standardized **every** API response into the `Result<T>` envelope
(`{ content, isSuccess, … }`). Legacy data endpoints were already enveloped (FE `*Response` interfaces
model `content`), but the endpoints added during integration (`permissions.me`, `team.*`,
`tenant-settings`) now arrive wrapped. The FE was reconciled (`4e00ed6`, `f5a5070`):
- `lib/apiResult.ts` `unwrapContent<T>(body, fallback)` — single, rollout-safe envelope reader.
- `data/api/ApiHandler.ts` `permissions.me`/`team.*`/`tenantSettings.*` unwrap `.content`.
- **Pattern going forward:** any *new* FE call to the Edulynk API must run through `unwrapContent`.
- Also: deployed FE must serve commit `4e00ed6`+. And confirm the deployed FE's
  **`AZURE_AD_TENANT_ID`** is `organizations` (multitenant) — if pinned to a single tenant it forces the
  wrong `tid` (here it happens to be fine because pvpro is under the publisher tenant).

---

## Do-this-next checklist

1. ☐ Apply the two `SaasKit__*` app settings on `educ8e-connector-app` (commands above).
2. ☐ Deploy API `saas-kit-integration` HEAD (`0f4a13e`) to `educ8e-connector-app` (clears 500 + binds `SaasKit:*`).
3. ☐ Sign in as `test@pvpro` → `GET /api/me/permissions` should be **200**. Empty? read App Insights.
4. ☐ If empty: confirm **#2** (subscription `PurchaserTenantId = e77b3a11` + provisioned Tenant) and **#3**
   (user bound + role / pending invite). Use a temporary SQL firewall rule + a SQL principal if needed,
   and remove the rule after.
5. ☐ Ensure the deployed **FE** serves `4e00ed6`+ and `AZURE_AD_TENANT_ID=organizations`.

## Reference IDs

- Edulynk API app reg: `6a3e6083-1a43-439e-b66a-141dd7e13f70` (SP that holds the grant)
- Admin API app reg: `2ddb6984-d793-4e10-8133-00c65b81b790` (SP `fbd741fe-2f3f-4891-92ad-450085717b73`), `Service.Access` role `00e9949e-9d55-428a-86ad-a829fdb8d9f5`
- Permissions API role `Service.Access`: `4ede5415-7587-4a5c-a444-55c2139dfc49`
- Tenant (publisher/home): `e77b3a11-2e95-4f9e-973b-918d466ba68d`
- Subscription: `a353af1c-9dd6-4de9-af57-3bae4b638eee` ("Microsoft Partner Network")
- App Service: `educ8e-connector-app` (RG `Rapha-Backend`); platform SQL: `sqldb-asdk-test-x16w`
