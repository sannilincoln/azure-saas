using Saas.Interface;

namespace Saas.Shared.Options;

/// <summary>
/// Common identity/config fields for a Microsoft Entra (Azure AD Workforce) application.
/// Replaces the former AzureAdB2CBase: this product is a B2B Azure Marketplace SaaS, so all
/// sign-in is via a multitenant Workforce Entra app (External ID / B2C has been removed).
/// For multitenant, <see cref="TenantId"/> is "organizations" and <see cref="Instance"/> is
/// "https://login.microsoftonline.com/"; Microsoft.Identity.Web validates the issuer per-tenant.
/// </summary>
public record EntraIdentityOptions
{
    public string? ClientId { get; init; }
    public string? Audience { get; init; }

    /// <summary>Directory domain — still used by the Permissions service Graph user lookup.</summary>
    public string? Domain { get; init; }
    public string? Instance { get; init; }
    public string? SignedOutCallbackPath { get; init; }
    public string? TenantId { get; init; }
    public string? BaseUrl { get; init; }
    public string? Certificate { get; init; }
    public string? ClientSecret { get; init; }

    public KeyVaultCertificate[]? KeyVaultCertificateReferences { get; init; }
}

public record KeyVaultCertificate : IKeyVaultInfo
{
    public string? SourceType { get; init; }
    public string? KeyVaultUrl { get; init; }
    public string? KeyVaultCertificateName { get; init; }
}
