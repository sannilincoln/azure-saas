namespace Saas.Shared.Options;

/// <summary>
/// Azure Marketplace SaaS fulfillment configuration for a single product/offer.
/// The publisher identity is the Microsoft Entra app + service principal registered in the
/// offer's Partner Center Technical Configuration; the Admin API authenticates as it
/// (client-credentials, fixed marketplace scope handled by the SDK) to call the Fulfillment
/// and Operations APIs. <see cref="PublisherTenantId"/> + <see cref="PublisherClientId"/> MUST
/// match that Technical Configuration exactly or the calls return 401/403.
/// </summary>
public record MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    public string? PublisherTenantId { get; init; }
    public string? PublisherClientId { get; init; }
    public string? PublisherClientSecret { get; init; }

    /// <summary>The offer id this deployment serves (one offer per product, per the template model).</summary>
    public string? OfferId { get; init; }

    /// <summary>URL customers are sent to after onboarding completes (the running SaaS app).</summary>
    public string? SaaSAppUrl { get; init; }

    /// <summary>
    /// Maps an Azure Marketplace plan id (what the buyer purchased on Azure) to this product's
    /// internal ProductTier id, so a marketplace-onboarded tenant is provisioned at the purchased
    /// tier without the in-app service-plan step. Keyed by plan id; value is the ProductTier id.
    /// A plan not present in the map resolves to tier 0 (the default/unmapped tier).
    /// </summary>
    public Dictionary<string, int>? PlanToProductTier { get; init; }
}
