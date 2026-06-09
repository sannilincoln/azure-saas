# Product app shell (`Saas.Application.Web`)

This is the **reusable product-app shell** for the marketplace SaaS template. It is intentionally
thin: it authenticates the user, resolves which tenant the request is for, enforces the tenant's
Azure Marketplace subscription status, and then hands off to *your* product. The sample
"BadgeMeUp" product has been removed — what remains is the seam you build on.

Per the template model, you redeploy this app per product and replace the UI with your own. Edulynk
is product #1.

## What to keep (the seam — don't remove this)

1. **Tenant resolution by route.** `Pages/Index.cshtml` is routed as `"{route?}"`. The `route`
   segment is passed to `ITenantService.GetTenantInfoByRouteAsync(route)`
   (`Services/TenantService.cs`), which calls the Admin API `tenantinfo/{route}` endpoint and
   returns a `TenantViewModel` (`Id`, `Name`, `SubscriptionStatus`). This is the only tenant
   contract the shell provides.

2. **Subscription gating.** `Middleware/RequireActiveSubscriptionMiddleware.cs` runs after
   authentication and **blocks (403)** any request whose tenant subscription is `Suspended` or
   `Unsubscribed`. It fails open on unknown status / transient errors so legacy tenants and trials
   keep working. Registered in `Program.cs` right after `UseAuthorization()`.

3. **Auth + Admin API wiring.** `Program.cs` configures Microsoft Entra (Workforce, multitenant)
   sign-in and the `IAdminServiceClient` used to reach the Admin API. Leave this in place.

## What to replace (the swap-in points)

Search for `SWAP-IN SEAM` comments:

- `Pages/Index.cshtml` — the two branches are (a) a signed-in, per-tenant home and (b) the
  no-tenant landing. Replace both with your product UI, or redirect to dedicated pages/areas.
- `Pages/Shared/_Layout.cshtml` — brand, navbar, footer, and `<title>` are generic placeholders.

Then add your own pages, services, and assets as normal. The resolved `TenantViewModel` (and the
user's claims) are your starting context for everything tenant-scoped.

## Verify after swapping in

- Hitting `/{route}` for a provisioned tenant resolves it (name shown / your home page renders).
- A `Suspended`/`Unsubscribed` tenant gets 403 from the middleware; `Reinstate` restores access.
- No `BadgeMeUp` / `bmu` residue remains in your replacement UI.
