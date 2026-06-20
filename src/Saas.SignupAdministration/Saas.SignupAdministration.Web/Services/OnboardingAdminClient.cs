using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Saas.Shared.Options;
using Microsoft.Identity.Web;

namespace Saas.SignupAdministration.Web.Services;

/// <summary>
/// App-only (service-to-service) client for the Admin API calls made during onboarding: marketplace
/// resolve/activate, route validation, and tenant creation.
/// </summary>
/// <remarks>
/// <para>Unlike <see cref="MarketplaceAdminClient"/> / the generated admin client (which attach the
/// signed-in user's token), this acquires an <b>app-only</b> token bearing the Admin API's
/// <c>Service.Access</c> app role. That is the crux of the multitenant marketplace model: a customer's
/// Entra tenant must only ever consent to user-consentable Graph scopes during interactive sign-in —
/// never to the Admin API — because customers are typically non-admins who cannot grant admin consent.
/// The web app signs the user in, then performs the privileged provisioning calls as itself.</para>
/// <para>The app token is minted against the publisher's home tenant (client-credentials cannot use the
/// "organizations" authority — that throws IDW10405), exactly like the Admin API's own downstream
/// provisioning call.</para>
/// </remarks>
public interface IOnboardingAdminClient
{
    /// <summary>True when the route/path is available (not already taken).</summary>
    Task<bool> IsValidPathAsync(string path);

    /// <summary>Creates the tenant and returns its new id. The creator becomes the tenant admin.</summary>
    Task<Guid> CreateTenantAsync(OnboardingTenantRequest request);

    /// <summary>Resolves a marketplace token into a durable subscription.</summary>
    Task<ResolvedSubscription> ResolveAsync(string token);

    /// <summary>Activates a resolved subscription (starts billing) and links it to the tenant.</summary>
    Task ActivateAsync(Guid subscriptionId, Guid tenantId);
}

/// <summary>Payload for app-only tenant creation. Carries the creator's object id explicitly because
/// there is no user token to read it from on the (app-only) Admin API call.</summary>
public record OnboardingTenantRequest(
    string Name,
    string Route,
    string CreatorEmail,
    Guid CreatorObjectId,
    int ProductTierId,
    int CategoryId);

public class OnboardingAdminClient : IOnboardingAdminClient
{
    private readonly HttpClient _httpClient;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly string _scope;
    private readonly string _publisherTenantId;

    public OnboardingAdminClient(
        HttpClient httpClient,
        ITokenAcquisition tokenAcquisition,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _tokenAcquisition = tokenAcquisition;

        var appIdUri = configuration.GetSection(AdminApiOptions.SectionName).Get<AdminApiOptions>()?.ApplicationIdUri
            ?? throw new InvalidOperationException($"{AdminApiOptions.SectionName}:ApplicationIdUri is required for app-only token acquisition.");
        _scope = $"{appIdUri.TrimEnd('/')}/.default";

        _publisherTenantId = configuration.GetSection(PublisherConsoleOptions.SectionName).Get<PublisherConsoleOptions>()?.PublisherTenantId
            ?? throw new InvalidOperationException($"{PublisherConsoleOptions.SectionName}:PublisherTenantId is required for app-only token acquisition.");
    }

    public async Task<bool> IsValidPathAsync(string path)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"api/Tenants/IsValidPath/{Uri.EscapeDataString(path)}");
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>();
    }

    public async Task<Guid> CreateTenantAsync(OnboardingTenantRequest request)
    {
        using var httpRequest = await CreateRequestAsync(HttpMethod.Post, "api/Tenants");
        httpRequest.Content = JsonContent.Create(request);

        using var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreatedTenant>()
            ?? throw new InvalidOperationException("Admin API returned an empty tenant-creation response.");
        return created.Id;
    }

    public async Task<ResolvedSubscription> ResolveAsync(string token)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "api/marketplace/resolve");
        request.Content = JsonContent.Create(new { token });

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ResolvedSubscription>()
            ?? throw new InvalidOperationException("Admin API returned an empty resolve response.");
    }

    public async Task ActivateAsync(Guid subscriptionId, Guid tenantId)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"api/marketplace/{subscriptionId}/activate");
        request.Content = JsonContent.Create(new { tenantId });

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativeUri)
    {
        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(_scope, tenant: _publisherTenantId);
        var request = new HttpRequestMessage(method, new Uri(relativeUri, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record CreatedTenant(Guid Id);
}
