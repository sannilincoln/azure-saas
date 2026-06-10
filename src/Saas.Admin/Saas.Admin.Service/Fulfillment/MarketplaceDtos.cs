namespace Saas.Admin.Service.Fulfillment;

/// <summary>Body for POST /api/marketplace/resolve — the raw marketplace token from the landing page.</summary>
public record ResolveRequest(string Token);

/// <summary>Body for POST /api/marketplace/{subscriptionId}/activate.</summary>
public record ActivateRequest(Guid TenantId);

/// <summary>
/// The durable result of resolving a marketplace token: enough to seed onboarding. The full
/// record is persisted server-side (marketplace store) keyed on <see cref="SubscriptionId"/>.
/// </summary>
public record ResolvedSubscriptionDto
{
    public Guid SubscriptionId { get; init; }
    public string? SubscriptionName { get; init; }
    public string? OfferId { get; init; }
    public string? PlanId { get; init; }
    public int Quantity { get; init; }

    /// <summary>
    /// The internal ProductTier id the purchased plan maps to (via Marketplace:PlanToProductTier).
    /// Used to provision the tenant at the purchased tier and skip the in-app service-plan step.
    /// </summary>
    public int ProductTierId { get; init; }
}
