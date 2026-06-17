using Microsoft.Identity.Web;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Production <see cref="IServiceTokenProvider"/>: acquires an app-only token via Microsoft.Identity.Web
/// (<see cref="ITokenAcquisition.GetAccessTokenForAppAsync"/>), authenticating as the Admin API's own
/// Entra app registration using its configured client credentials. MSAL caches the token, so repeated
/// provisioning calls don't re-mint. Requires <c>EnableTokenAcquisitionToCallDownstreamApi</c> on the
/// authentication registration and a client secret/certificate in the Admin API's Entra config.
/// </summary>
public sealed class TokenAcquisitionServiceTokenProvider(ITokenAcquisition tokenAcquisition) : IServiceTokenProvider
{
    public Task<string> GetAppTokenAsync(string scope, CancellationToken cancellationToken = default) =>
        tokenAcquisition.GetAccessTokenForAppAsync(scope);
}
