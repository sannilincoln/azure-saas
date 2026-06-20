namespace Saas.Admin.Service.Data;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int ProductTierId { get; set; }
    public int CategoryId { get; set; }
    public string CreatorEmail { get; set; } = string.Empty;
    public DateTime? CreatedTime { get; set; }

    /// <summary>
    /// The Azure Marketplace subscription GUID (AMP subscription id) this tenant was
    /// provisioned for. Null for tenants not created via a Marketplace purchase. The full
    /// subscription record lives in the marketplace store (SaasKitContext.Subscriptions);
    /// this is the link key (== Subscriptions.AmpsubscriptionId).
    /// </summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// Denormalized Marketplace subscription status (PendingFulfillmentStart / Subscribed /
    /// Suspended / Unsubscribed), kept in sync from the connection webhook. Read by the
    /// tenantinfo path and used to gate app access. Null for non-marketplace tenants.
    /// </summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>
    /// Name of this tenant's dedicated product database (the per-tenant DB the product app connects
    /// to). Set at provisioning. The product resolves it via the tenantinfo path and connects with its
    /// managed identity — so no per-tenant credentials are stored here, only the database name. Null
    /// for tenants not yet provisioned (or non-product tenants).
    /// </summary>
    public string? DatabaseName { get; set; }

    /// <summary>
    /// Background provisioning lifecycle: <c>Provisioning</c> (queued/in progress), <c>Provisioned</c>
    /// (database ready — see <see cref="DatabaseName"/>), or <c>Failed</c>. Set when a marketplace
    /// subscription is linked; the background worker advances it. Null for tenants that never went
    /// through marketplace provisioning. See <see cref="Fulfillment.ProvisioningStatuses"/>.
    /// </summary>
    public string? ProvisioningStatus { get; set; }

    [Timestamp]
    public byte[]? ConcurrencyToken { get; set; }
}
