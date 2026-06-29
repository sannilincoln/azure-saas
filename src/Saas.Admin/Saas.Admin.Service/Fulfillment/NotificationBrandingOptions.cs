namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Per-product branding for the customer-facing notification emails (welcome / invite / role-change),
/// bound from <c>Notifications:Branding:*</c>. Lets the same code ship per product without hard-coding
/// a product name or URL. <see cref="AppBaseUrl"/> is the front-end the "sign in" link points at.
/// </summary>
public record NotificationBrandingOptions
{
    public const string SectionName = "Notifications:Branding";

    /// <summary>Product display name used in subjects/bodies. A neutral default until configured.</summary>
    public string ProductName { get; init; } = "your workspace";

    /// <summary>Front-end base URL the "sign in here" link points at (e.g. the SWA host).</summary>
    public string? AppBaseUrl { get; init; }

    public string? LogoUrl { get; init; }

    public string? SupportEmail { get; init; }
}
