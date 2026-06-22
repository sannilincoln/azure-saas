using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Saas.Admin.Service.Authorization;
using Saas.Identity.Authorization.Model.Kind;
using Xunit;

namespace Saas.Admin.Service.Tests.Authorization;

public class TenantAdminOrServiceAuthorizationHandlerTests
{
    private static async Task<bool> Evaluate(Guid routeTenantId, params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["tenantId"] = routeTenantId.ToString();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var requirement = new TenantAdminOrServiceRequirement();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);

        await new TenantAdminOrServiceAuthorizationHandler(accessor).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static Claim Permission(string value) => new("permissions", value);

    [Fact]
    public async Task Succeeds_ForAppOnlyServiceAccessToken()
    {
        Assert.True(await Evaluate(Guid.NewGuid(), new Claim("roles", ServiceAccessPolicy.RoleValue)));
    }

    [Fact]
    public async Task Succeeds_ForTenantAdmin_OnTheRouteTenant()
    {
        var tenantId = Guid.NewGuid();
        Assert.True(await Evaluate(tenantId, Permission($"Tenant.{tenantId}.{TenantPermissionKind.Admin}")));
    }

    [Fact]
    public async Task Fails_ForTenantAdmin_OnADifferentTenant()
    {
        var routeTenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        Assert.False(await Evaluate(routeTenant, Permission($"Tenant.{otherTenant}.{TenantPermissionKind.Admin}")));
    }

    [Fact]
    public async Task Fails_ForNonAdminTenantPermission()
    {
        var tenantId = Guid.NewGuid();
        // Read alone does not satisfy the full Admin bit set.
        Assert.False(await Evaluate(tenantId, Permission($"Tenant.{tenantId}.{TenantPermissionKind.Read}")));
    }

    [Fact]
    public async Task Fails_ForRoleTagOnly_WithoutBackingAdminPermission()
    {
        var tenantId = Guid.NewGuid();
        // A Role: tag is not a CRUD permission and must not be mistaken for Admin.
        Assert.False(await Evaluate(tenantId, Permission($"Tenant.{tenantId}.Role:Super-Admin")));
    }

    [Fact]
    public async Task Fails_ForCallerWithNoRelevantClaims()
    {
        Assert.False(await Evaluate(Guid.NewGuid(), new Claim("name", "a user")));
    }
}
