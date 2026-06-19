using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Production <see cref="IServiceTokenProvider"/>: acquires an app-only token via Microsoft.Identity.Web
/// (<see cref="ITokenAcquisition.GetAccessTokenForAppAsync"/>), authenticating as the Admin API's own
/// Entra app registration using its configured client credentials. MSAL caches the token, so repeated
/// provisioning calls don't re-mint. Requires <c>EnableTokenAcquisitionToCallDownstreamApi</c> on the
/// authentication registration and a client secret/certificate in the Admin API's Entra config.
/// </summary>
public sealed class TokenAcquisitionServiceTokenProvider(
    ITokenAcquisition tokenAcquisition,
    IOptions<MarketplaceOptions> marketplaceOptions) : IServiceTokenProvider
{
    // Client-credentials token acquisition needs a SPECIFIC tenant — it cannot use the "organizations"
    // authority the Admin API uses to validate incoming multitenant tokens (that throws IDW10405). The
    // app + its admin-consented app roles live in the publisher's home tenant.
    private readonly string _homeTenantId = marketplaceOptions.Value.PublisherTenantId
        ?? throw new InvalidOperationException("Marketplace:PublisherTenantId is required for app-only token acquisition.");

    public Task<string> GetAppTokenAsync(string scope, CancellationToken cancellationToken = default) =>
        tokenAcquisition.GetAccessTokenForAppAsync(scope, tenant: _homeTenantId);
}
