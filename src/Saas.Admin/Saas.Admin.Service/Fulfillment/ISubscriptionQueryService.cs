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
