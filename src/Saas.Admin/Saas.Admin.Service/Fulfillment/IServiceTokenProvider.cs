namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Acquires an app-only (client-credentials) access token for a downstream service, so the Admin API
/// can call a product API as its own identity. The production implementation authenticates as the
/// Admin API's Entra app registration (whose service principal holds the product API's app role) and
/// caches tokens; abstracted here so the provisioning service is testable without a real token issuer.
/// </summary>
public interface IServiceTokenProvider
{
    Task<string> GetAppTokenAsync(string scope, CancellationToken cancellationToken = default);
}
