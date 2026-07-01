using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Saas.Shared.Options;

namespace Saas.SignupAdministration.Web.Services;

public interface IMarketplaceAdminClient
{
    /// <summary>All subscriptions — backs the publisher console (Admin API enforces publisher-only).</summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetAllSubscriptionsAsync();

    /// <summary>The caller's own subscriptions — backs customer self-service (Admin API filters by tid).</summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync();

    /// <summary>Re-pull live state from Microsoft (publisher console).</summary>
    Task<SubscriptionInfo?> RefreshSubscriptionAsync(Guid subscriptionId);

    /// <summary>Administratively override a subscription's status (publisher console).</summary>
    Task<SubscriptionInfo?> OverrideSubscriptionStatusAsync(Guid subscriptionId, string status);

    /// <summary>Read the publisher-editable notification settings (publisher console).</summary>
    Task<NotificationSettings> GetNotificationSettingsAsync();

    /// <summary>Update the publisher-editable notification settings (publisher console).</summary>
    Task UpdateNotificationSettingsAsync(NotificationSettings settings);

    /// <summary>
    /// Send a one-off test email via the Admin API's Graph transport. Returns the API's message
    /// verbatim (success text or the surfaced transport error) so the console can show it.
    /// </summary>
    Task<(bool Ok, string Message)> SendTestEmailAsync(string to);
}

public record NotificationSettings(
    bool Enabled,
    string? ToEmails,
    bool SignupAlert,
    bool Welcome,
    bool Invite,
    bool RoleChange);

public record ResolvedSubscription(
    Guid SubscriptionId,
    string? SubscriptionName,
    string? OfferId,
    string? PlanId,
    int Quantity,
    int ProductTierId);

public record SubscriptionInfo(
    Guid SubscriptionId,
    string? Name,
    string? OfferId,
    string? PlanId,
    int Quantity,
    string? Status,
    string? PurchaserEmail,
    Guid? CustomerTenantId,
    Guid? TenantId,
    string? TenantName,
    DateTime? CreatedTime);

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
    public Task<IReadOnlyList<SubscriptionInfo>> GetAllSubscriptionsAsync() =>
        GetListAsync("api/marketplace/subscriptions");

    public Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync() =>
        GetListAsync("api/marketplace/subscriptions/mine");

    public Task<SubscriptionInfo?> RefreshSubscriptionAsync(Guid subscriptionId) =>
        PostForSubscriptionAsync($"api/marketplace/subscriptions/{subscriptionId}/refresh", content: null);

    public Task<SubscriptionInfo?> OverrideSubscriptionStatusAsync(Guid subscriptionId, string status) =>
        PostForSubscriptionAsync($"api/marketplace/subscriptions/{subscriptionId}/status", new { status });

    public async Task<NotificationSettings> GetNotificationSettingsAsync()
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("api/marketplace/notifications/settings", UriKind.Relative);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<NotificationSettings>()
            ?? new NotificationSettings(Enabled: false, ToEmails: null,
                SignupAlert: false, Welcome: false, Invite: false, RoleChange: false);
    }

    public async Task UpdateNotificationSettingsAsync(NotificationSettings settings)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Put;
        request.RequestUri = new Uri("api/marketplace/notifications/settings", UriKind.Relative);
        request.Content = JsonContent.Create(settings);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(bool Ok, string Message)> SendTestEmailAsync(string to)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(
            $"api/marketplace/notifications/settings/test?to={Uri.EscapeDataString(to)}", UriKind.Relative);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, body);
    }

    private async Task<IReadOnlyList<SubscriptionInfo>> GetListAsync(string relativeUri)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(relativeUri, UriKind.Relative);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<SubscriptionInfo>>()
            ?? new List<SubscriptionInfo>();
    }

    private async Task<SubscriptionInfo?> PostForSubscriptionAsync(string relativeUri, object? content)
    {
        using var request = await CreateHttpRequestMessageAsync(CancellationToken.None);
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(relativeUri, UriKind.Relative);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content);
        }

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SubscriptionInfo>();
    }
}
