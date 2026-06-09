using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Saas.Shared.Options;

namespace Saas.SignupAdministration.Web.Services;

public interface IMarketplaceAdminClient
{
    Task<ResolvedSubscription> ResolveAsync(string token);
    Task ActivateAsync(Guid subscriptionId, Guid tenantId);
}

public record ResolvedSubscription(
    Guid SubscriptionId,
    string? SubscriptionName,
    string? OfferId,
    string? PlanId,
    int Quantity);

/// <summary>
/// Thin typed client over the Admin API's marketplace endpoints. Reuses <see cref="OAuthBaseClient"/>
/// so the signed-in user's bearer token is attached exactly like the generated AdminServiceClient —
/// the marketplace endpoints live on the same Admin API audience/scopes.
/// </summary>
public class MarketplaceAdminClient(
    HttpClient httpClient,
    ITokenAcquisition tokenAcquisition,
    IOptions<SaasAppScopeOptions> scopes) : OAuthBaseClient(tokenAcquisition, scopes), IMarketplaceAdminClient
{
    public async Task<ResolvedSubscription> ResolveAsync(string token)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri("api/marketplace/resolve", UriKind.Relative);
        request.Content = JsonContent.Create(new { token });

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ResolvedSubscription>()
            ?? throw new InvalidOperationException("Admin API returned an empty resolve response.");
    }

    public async Task ActivateAsync(Guid subscriptionId, Guid tenantId)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri($"api/marketplace/{subscriptionId}/activate", UriKind.Relative);
        request.Content = JsonContent.Create(new { tenantId });

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
