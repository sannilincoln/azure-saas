using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saas.Admin.Service.Authorization;
using Xunit;

namespace Saas.Admin.Service.Tests.Authorization;

public class ServiceAppRoleAuthorizationHandlerTests
{
    private static async Task<bool> Evaluate(params Claim[] claims)
    {
        var requirement = new ServiceAppRoleRequirement(ServiceAccessPolicy.RoleValue);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        await new ServiceAppRoleAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_WhenAppOnlyToken_CarriesServiceAccessInRolesClaim()
    {
        Assert.True(await Evaluate(new Claim("roles", "Service.Access")));
    }

    [Fact]
    public async Task Succeeds_WhenRoleSurfacesAsMappedRoleClaim()
    {
        Assert.True(await Evaluate(new Claim(ClaimTypes.Role, "Service.Access")));
    }

    [Fact]
    public async Task Fails_WhenCallerHasADifferentRole()
    {
        Assert.False(await Evaluate(new Claim("roles", "Some.Other.Role")));
    }

    [Fact]
    public async Task Fails_WhenNoRolesClaimPresent()
    {
        Assert.False(await Evaluate(new Claim("name", "a user")));
    }
}
