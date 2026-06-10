# Saas.Marketplace — vendored Azure Marketplace SaaS fulfillment

These two projects (`Services`, `DataAccess`) are **vendored copies** of the
fulfillment + data-access libraries from Microsoft's MIT-licensed
[Azure/Commercial-Marketplace-SaaS-Accelerator](https://github.com/Azure/Commercial-Marketplace-SaaS-Accelerator).

- **Upstream commit:** `dd94531a60f7fad6ab7e4017e137fd4f1713973c`
- **Vendored:** 2026-06-09
- **License:** MIT (see `ACCELERATOR-LICENSE`)
- **Namespaces kept as upstream** (`Marketplace.SaaS.Accelerator.Services` /
  `Marketplace.SaaS.Accelerator.DataAccess`) to minimise drift from upstream and
  ease future updates.

## Why vendored (not submodule / NuGet)
The accelerator publishes only the `Marketplace.SaaS.Client` SDK to NuGet, not
these libraries — they are source-only. We copy them in so we can target our
solution, edit DI/wiring, and (later) prune unused metering code, while shipping
as a standalone template.

## What we use
- `Services/Services/FulfillmentApiService.cs` (resolve / activate / get / list /
  changeplan / changequantity / delete / operations) + `WebHook/*` (webhook
  validation & processing) + the Pending/Unsubscribe `StatusHandlers`.
- `DataAccess` `SaasKitContext` + the subscription/plan/offer/event entities &
  repositories.

## What we do NOT use (dead weight, retained as-is for now)
The **metered billing** subsystem — `MeteredBillingAPIService`,
`MeteredPlanSchedulerManagement*`, the `Metered*`/`Scheduler*` entities,
repositories, and models. Our offers are flat per-seat, so no usage is emitted.
This code is never registered or called; it may be pruned in a later pass.
The upstream `AdminSite`, `CustomerSite`, and `MeteredTriggerJob` were not vendored.
