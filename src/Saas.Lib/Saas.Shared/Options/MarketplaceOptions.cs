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

    /// <summary>
    /// Maps this product's internal ProductTier id to the maximum number of students a tenant on that
    /// tier may hold (the marketplace plan's gated metric). Keyed by ProductTier id; value is the
    /// ceiling. The ceiling is enforced product-side (Edulynk counts student rows); the platform only
    /// publishes the number via the tenant quota endpoint.
    /// <para>
    /// <b>Fail-closed:</b> a tier not present in the map — or a null/absent map — resolves to a ceiling
    /// of <c>0</c>, i.e. <b>no students may be registered</b>. This is deliberate: a configuration slip
    /// must not silently hand a tenant unlimited students. Every live plan's tier MUST be mapped here.
    /// </para>
    /// </summary>
    public Dictionary<int, int>? TierMaxStudents { get; init; }

    /// <summary>
    /// Prefix for a marketplace-provisioned tenant's dedicated database name; the name is
    /// <c>{prefix}-{tenant route}</c>, stored on the tenant and handed to the product provisioning
    /// service at activation. When null/empty, per-tenant database provisioning is disabled (the
    /// platform sets no database name and does not call the provisioning service) — appropriate for
    /// products that don't use a database-per-tenant model. Keeps the platform product-agnostic: the
    /// product name lives in config, not code.
    /// </summary>
    public string? TenantDatabaseNamePrefix { get; init; }

    /// <summary>
    /// Optional email-notification settings. When enabled, the publisher is emailed on key
    /// subscription events — currently a new subscription activating (i.e. a tenant signing up).
    /// Disabled and inert unless <see cref="MarketplaceNotificationOptions.Enabled"/> is set.
    /// </summary>
    public MarketplaceNotificationOptions? Notifications { get; init; }
}

/// <summary>
/// SMTP + recipient settings for marketplace publisher notifications. Bound from
/// <c>Marketplace:Notifications:*</c>. <see cref="SmtpPassword"/> should be supplied as a Key Vault
/// reference in App Config, never a literal.
/// </summary>
public record MarketplaceNotificationOptions
{
    /// <summary>Master switch. When false (default) no email is ever sent.</summary>
    public bool Enabled { get; init; }

    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public bool SmtpUseSsl { get; init; } = true;
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }

    /// <summary>From address on the notification email.</summary>
    public string? FromEmail { get; init; }

    /// <summary>
    /// Publisher recipients — the mailbox(es) notified when a tenant signs up. Semicolon- or
    /// comma-separated, e.g. "ops@lagetronix.com;sales@lagetronix.com".
    /// </summary>
    public string? ToEmails { get; init; }

    /// <summary>Also CC the customer who onboarded (the new tenant's creator email).</summary>
    public bool CopyToCustomer { get; init; }
}
