using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Real product provisioning: POSTs to the product API's internal provisioning endpoint
/// (<c>POST /internal/tenants/{tenantId}/provision</c>) to create + migrate the tenant's dedicated
/// database. Authenticates app-to-app as the Admin API's identity (the token carries the granted
/// <c>Provisioning.Write</c> app role). Called synchronously at activation; a terminal failure
/// propagates so onboarding can surface/retry it. The endpoint is idempotent, so a retried activation
/// (or the short transient retry here) is safe.
/// </summary>
public sealed class HttpProductProvisioningService(
    HttpClient httpClient,
    IServiceTokenProvider tokenProvider,
    IOptions<ProductProvisioningOptions> options,
    ILogger<HttpProductProvisioningService> logger) : IProductProvisioningService
{
    private readonly ProductProvisioningOptions _options = options.Value;

    public async Task ProvisionAsync(Guid tenantId, string databaseName)
    {
        var scope = _options.Scope
            ?? throw new InvalidOperationException("ProductProvisioning:Scope is required for the HTTP provisioner.");

        var maxAttempts = Math.Max(1, _options.MaxRetries + 1);
        var delay = TimeSpan.FromSeconds(Math.Max(0, _options.RetryDelaySeconds));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var token = await tokenProvider.GetAppTokenAsync(scope);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"internal/tenants/{tenantId}/provision");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(new ProvisionRequest(databaseName));

                using var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Provisioned tenant {TenantId} (database {DatabaseName}) via the product API on attempt {Attempt}.",
                        tenantId, databaseName, attempt);
                    return;
                }

                if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                {
                    logger.LogWarning("Provisioning tenant {TenantId} returned transient {StatusCode} (attempt {Attempt}/{MaxAttempts}); retrying.",
                        tenantId, (int)response.StatusCode, attempt, maxAttempts);
                    await Task.Delay(delay);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync();
                throw new ProductProvisioningException(
                    $"Provisioning tenant {tenantId} (database {databaseName}) failed with status {(int)response.StatusCode}: {body}");
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Provisioning tenant {TenantId} failed to reach the product API (attempt {Attempt}/{MaxAttempts}); retrying.",
                    tenantId, attempt, maxAttempts);
                await Task.Delay(delay);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.RequestTimeout || status == HttpStatusCode.TooManyRequests;

    private sealed record ProvisionRequest(string DatabaseName);
}

/// <summary>Thrown when product provisioning fails terminally (non-transient status, or retries exhausted).</summary>
public sealed class ProductProvisioningException(string message) : Exception(message);
