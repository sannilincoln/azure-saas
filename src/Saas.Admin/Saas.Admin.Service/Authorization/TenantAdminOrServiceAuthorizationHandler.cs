using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Saas.Identity.Authorization.Model.Claim;
using Saas.Identity.Authorization.Model.Data;
using Saas.Identity.Authorization.Model.Kind;

namespace Saas.Admin.Service.Authorization;

/// <summary>
/// Authorizes tenant member-management (invite / assign role) for <em>either</em>:
/// <list type="bullet">
///   <item>a tenant administrator on a user token — holds <c>Tenant.&lt;tenantId&gt;.Admin</c> for the
///   route's tenant (the publisher's own admin UI path); or</item>
///   <item>a trusted service on an app-only token carrying the <c>Service.Access</c> app role — the
///   product BFF, which has already authorized the end user as a tenant admin before proxying here.</item>
/// </list>
/// This OR is why invite/assign don't use <c>[SaasAuthorize]</c> alone: under Option B customer tenants
/// never consent to the Admin API, so the BFF must call app-only — but the publisher's admin UI still
/// uses a user token. See <see cref="ServiceAccessPolicy"/>.
/// </summary>
public static class TenantAdminOrServicePolicy
{
    public const string Name = "TenantAdminOrService";

    /// <summary>Route value carrying the tenant id evaluated for the user-token path.</summary>
    public const string RoutingKeyName = "tenantId";
}

public sealed class TenantAdminOrServiceRequirement : IAuthorizationRequirement;

public sealed class TenantAdminOrServiceAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<TenantAdminOrServiceRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantAdminOrServiceRequirement requirement)
    {
        // Service-to-service: app-only token with the Service.Access app role.
        if (context.User.HasClaim("roles", ServiceAccessPolicy.RoleValue) ||
            context.User.HasClaim(ClaimTypes.Role, ServiceAccessPolicy.RoleValue))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // User token: must hold the tenant Admin permission for the route's tenant id.
        if (httpContextAccessor.HttpContext?.GetRouteValue(TenantAdminOrServicePolicy.RoutingKeyName) is string routeValue
            && Guid.TryParse(routeValue, out var tenantId)
            && HasTenantAdmin(context.User, tenantId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool HasTenantAdmin(ClaimsPrincipal user, Guid tenantId)
    {
        // Accumulate the user's tenant-permission bits for this tenant and require the full Admin set,
        // mirroring the bitwise semantics of SaasTenantPermissionAuthorizationHandler.
        var accumulated = user.Claims
            .Where(c => c.Type == SaasPermissionClaim<TenantPermissionKind>.PermissionClaimsIdentifier)
            .Select(c => new SaasPermissionClaim<TenantPermissionKind>(c.Value, TenantPermission.EntityName))
            .Where(p => p.IsValid && p.Entity == tenantId)
            .Aggregate(0, (acc, p) => acc | p.ToInt());

        return (accumulated & (int)TenantPermissionKind.Admin) == (int)TenantPermissionKind.Admin;
    }
}
