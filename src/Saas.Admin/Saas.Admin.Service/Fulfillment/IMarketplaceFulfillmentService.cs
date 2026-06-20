namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Glue between the marketplace landing/onboarding flow and the vendored accelerator
/// fulfillment client + marketplace store. Lives in the Admin API because that is the
/// always-on, session-free service that holds the publisher service-principal credentials
/// and owns both the marketplace store (SaasKitContext) and the tenant store.
/// </summary>
public interface IMarketplaceFulfillmentService
{
    /// <summary>
    /// Exchanges a marketplace token for the durable subscription, persists it
    /// (status PendingFulfillmentStart) and returns enough to seed onboarding. The token is
    /// single-use / 24h; this is called once, immediately, from the landing page.
    /// The subscription's customer-tenant key (used for runtime tenant resolution and self-service
    /// filtering) is taken from the resolved subscription's <c>Beneficiary.TenantId</c> — the org whose
    /// users will sign in to the product — not from the interactive caller, since this runs app-only.
    /// </summary>
    Task<ResolvedSubscriptionDto> ResolveAsync(string marketplaceToken);

    /// <summary>
    /// Links the subscription to the created tenant and queues it for provisioning (marks the tenant
    /// <c>Provisioning</c>). Returns immediately — the slow work (CREATE DATABASE + migrate + seed, then
    /// Microsoft activation) runs out of band in <see cref="ProcessPendingProvisioningAsync"/> so the
    /// onboarding request doesn't block ~60s and time out. The tenant's <c>DatabaseName</c> stays null
    /// until provisioning succeeds, so it cannot serve traffic prematurely.
    /// </summary>
    Task ActivateAsync(Guid subscriptionId, Guid tenantId);

    /// <summary>
    /// Drains tenants currently in the <c>Provisioning</c> state: provisions the per-tenant database,
    /// activates the subscription with Microsoft (billing starts only after provisioning succeeds),
    /// sets <c>DatabaseName</c>, and marks them <c>Provisioned</c> (or <c>Failed</c>). Driven by the
    /// background <c>TenantProvisioningWorker</c>; idempotent and safe to re-run.
    /// </summary>
    Task ProcessPendingProvisioningAsync(CancellationToken cancellationToken = default);
}
