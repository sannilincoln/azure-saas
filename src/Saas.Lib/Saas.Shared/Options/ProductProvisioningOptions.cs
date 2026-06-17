namespace Saas.Shared.Options;

/// <summary>
/// Configuration for the platform → product provisioning call made at marketplace activation
/// (Phase 1.3). When <see cref="BaseUrl"/> is set, the real <c>HttpProductProvisioningService</c> is
/// wired (overriding the no-op) and POSTs to the product's internal provisioning endpoint, authenticating
/// app-to-app as the Admin API's identity for <see cref="Scope"/> (the product API's
/// <c>api://{appId}/.default</c>, which carries the granted <c>Provisioning.Write</c> app role).
/// Absent <see cref="BaseUrl"/>, provisioning stays a no-op — keeping the platform product-agnostic.
/// </summary>
public record ProductProvisioningOptions
{
    public const string SectionName = "ProductProvisioning";

    /// <summary>Base address of the product API exposing <c>POST /internal/tenants/{tenantId}/provision</c>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>App-only scope for the product API, e.g. <c>api://6a3e6083…/.default</c>.</summary>
    public string? Scope { get; init; }

    /// <summary>Retries attempted <i>after</i> the first try, on transient failures. Default 2 (3 attempts total).</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Delay between attempts. Provisioning is synchronous at activation, so keep this short.</summary>
    public int RetryDelaySeconds { get; init; } = 2;
}
