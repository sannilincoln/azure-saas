using Microsoft.AspNetCore.Authorization;

namespace Saas.Admin.Service.Authorization;

/// <summary>
/// Authorization for service-to-service callers (the product API) holding the <c>Service.Access</c>
/// app role on an app-only (client-credentials) token. App roles arrive in the token's <c>roles</c>
/// claim; because <c>Service.Access</c> is an Application-only role, only an app-only token carries it
/// (a delegated user token never will). Applied to the s2s endpoints the product API calls
/// (tenant resolution, quota).
/// </summary>
public static class ServiceAccessPolicy
{
    /// <summary>Policy name used on <c>[Authorize(Policy = …)]</c>.</summary>
    public const string Name = "ServiceAccess";

    /// <summary>The app role value granted to the product API's service principal (Phase 4.3).</summary>
    public const string RoleValue = "Service.Access";
}

/// <summary>Requires the caller's token to carry the <see cref="RoleValue"/> app role.</summary>
public sealed class ServiceAppRoleRequirement(string roleValue) : IAuthorizationRequirement
{
    public string RoleValue { get; } = roleValue;
}
