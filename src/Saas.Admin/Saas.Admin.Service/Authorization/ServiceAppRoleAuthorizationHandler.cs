using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Saas.Admin.Service.Authorization;

/// <summary>
/// Succeeds when the caller's token carries the required app role. Azure AD delivers app roles in the
/// <c>roles</c> claim; depending on claim mapping it may also surface as <see cref="ClaimTypes.Role"/>,
/// so both are checked. Fail-closed: no matching role ⇒ the requirement is simply not satisfied.
/// </summary>
public sealed class ServiceAppRoleAuthorizationHandler : AuthorizationHandler<ServiceAppRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ServiceAppRoleRequirement requirement)
    {
        var hasRole =
            context.User.HasClaim("roles", requirement.RoleValue) ||
            context.User.HasClaim(ClaimTypes.Role, requirement.RoleValue);

        if (hasRole)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
