namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// A marketplace subscription as surfaced to the publisher console and customer self-service.
/// Projected from the vendored accelerator store (Subscriptions) joined to the linked tenant.
/// </summary>
public record SubscriptionDto
{
    public Guid SubscriptionId { get; init; }
    public string? Name { get; init; }
    public string? OfferId { get; init; }
    public string? PlanId { get; init; }
    public int Quantity { get; init; }
    public string? Status { get; init; }
    public string? PurchaserEmail { get; init; }
    public Guid? CustomerTenantId { get; init; }

    /// <summary>The provisioned tenant this subscription is linked to (null until onboarding completes).</summary>
    public Guid? TenantId { get; init; }
    public string? TenantName { get; init; }

    public DateTime? CreatedTime { get; init; }
}

/// <summary>Body for POST .../{id}/status — a publisher status override.</summary>
public record OverrideStatusRequest(string Status);
