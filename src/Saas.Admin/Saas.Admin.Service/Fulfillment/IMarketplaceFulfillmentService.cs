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
    /// </summary>
    Task<ResolvedSubscriptionDto> ResolveAsync(string marketplaceToken);

    /// <summary>
    /// Activates the subscription with Microsoft (starts billing) AFTER onboarding has
    /// succeeded, flips the stored status to Subscribed, and links it to the created tenant.
    /// </summary>
    Task ActivateAsync(Guid subscriptionId, Guid tenantId);
}
