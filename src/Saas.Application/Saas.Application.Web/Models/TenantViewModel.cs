namespace Saas.Application.Web.Models;

public class TenantViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Azure Marketplace subscription status for this tenant (Subscribed / Suspended /
    /// Unsubscribed / PendingFulfillmentStart), or null for non-marketplace tenants. Used by
    /// <see cref="Saas.Application.Web.Middleware.RequireActiveSubscriptionMiddleware"/> to gate access.
    /// </summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>
    /// True when the subscription is in a state that should block end-user access. Null/unknown
    /// statuses (and non-marketplace tenants) are treated as active so legacy tenants keep working.
    /// </summary>
    public bool IsAccessBlocked =>
        string.Equals(SubscriptionStatus, "Suspended", StringComparison.OrdinalIgnoreCase)
        || string.Equals(SubscriptionStatus, "Unsubscribed", StringComparison.OrdinalIgnoreCase);
}
