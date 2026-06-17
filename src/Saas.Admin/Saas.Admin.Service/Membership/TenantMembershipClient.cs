using System.Net.Http.Json;

namespace Saas.Admin.Service.Membership;

/// <summary>
/// Minimal typed client for the Permissions service's JIT membership endpoints. Hand-written (rather
/// than added to the nswag-generated <c>PermissionsServiceClient</c>) so it needs no nswag toolchain
/// and survives a future client regen. Shares the Permissions base URL + x-api-key (configured in DI).
/// </summary>
public interface ITenantMembershipClient
{
    /// <summary>Creates a pending invitation by email (no directory lookup).</summary>
    Task CreateInvitationAsync(Guid tenantId, string email, IEnumerable<string> permissions);

    /// <summary>Binds a signed-in user to a pending invitation; returns the outcome name.</summary>
    Task<string> BindMemberAsync(Guid tenantId, Guid userId, string email, string? displayName);
}

public class TenantMembershipClient(HttpClient httpClient) : ITenantMembershipClient
{
    public async Task CreateInvitationAsync(Guid tenantId, string email, IEnumerable<string> permissions)
    {
        var url = $"api/TenantMembership/CreateInvitation?tenantId={tenantId}&email={Uri.EscapeDataString(email)}";
        using var response = await httpClient.PostAsJsonAsync(url, permissions.ToArray());
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> BindMemberAsync(Guid tenantId, Guid userId, string email, string? displayName)
    {
        var url = $"api/TenantMembership/BindMember?tenantId={tenantId}&userId={userId}&email={Uri.EscapeDataString(email)}";
        if (!string.IsNullOrEmpty(displayName))
        {
            url += $"&displayName={Uri.EscapeDataString(displayName)}";
        }

        using var response = await httpClient.PostAsync(url, content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
