namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Read/manage access to the marketplace subscription store for the in-app consoles. The hard
/// authorization boundary (publisher-only vs. customer-scoped) is enforced in the controller; this
/// service just provides the queries and management operations.
/// </summary>
public interface ISubscriptionQueryService
{
    /// <summary>All subscriptions — publisher console only.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetAllAsync();

    /// <summary>Subscriptions belonging to a single customer tenant — customer self-service.</summary>
    Task<IReadOnlyList<SubscriptionDto>> GetByCustomerTenantAsync(Guid customerTenantId);

    /// <summary>
    /// Resolves the product <see cref="Saas.Admin.Service.Data.Tenant"/> for a customer's Entra tenant
    /// id (the buyer's home tenant, captured as <c>Subscriptions.PurchaserTenantId</c> at resolve time).
    /// This is the <c>tid → Tenant</c> lookup the multitenant product API needs: there is no direct
    /// column linking them, so it joins via the subscription. Returns null when no provisioned tenant
    /// exists for that Entra tenant. When the Entra tenant has multiple subscriptions, prefers an
    /// active (Subscribed) one, then the most recent.
    /// </summary>
    Task<Saas.Admin.Service.Data.Tenant?> GetTenantByCustomerTenantAsync(Guid customerTenantId);

    /// <summary>
    /// Re-pull a subscription's live state from Microsoft and update the local row (status, plan,
    /// quantity), keeping the denormalized tenant status in sync. Publisher console only.
    /// </summary>
    Task<SubscriptionDto?> RefreshFromMarketplaceAsync(Guid subscriptionId);

    /// <summary>
    /// Administratively override the stored status (and the denormalized tenant status). Publisher
    /// console only — a break-glass control, distinct from the webhook-driven status flow.
    /// </summary>
    Task<SubscriptionDto?> OverrideStatusAsync(Guid subscriptionId, string status);
}
