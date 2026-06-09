namespace Saas.Admin.Client;

/// <summary>
/// Hand-added field on the NSwag-generated <see cref="TenantInfoDTO"/>. The Admin API now returns
/// the denormalized Marketplace subscription status on the public tenantinfo path; the product app
/// reads it to gate access. Kept in a partial (rather than regenerating the whole client) so a
/// later NSwag regen won't clobber it. System.Text.Json populates it via the property-name match.
/// </summary>
public partial class TenantInfoDTO
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptionStatus")]
    public string? SubscriptionStatus { get; set; }
}
