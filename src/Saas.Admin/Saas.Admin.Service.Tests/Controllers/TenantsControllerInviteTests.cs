using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Saas.Admin.Service.Controllers;
using Saas.Admin.Service.Fulfillment;
using Saas.Admin.Service.Membership;
using Saas.Admin.Service.Services;
using Saas.Permissions.Client;
using Xunit;

namespace Saas.Admin.Service.Tests.Controllers;

public class TenantsControllerInviteTests
{
    [Fact]
    public async Task Invite_CreatesPendingInvitation_AndDoesNotUseGraphEmailLookup()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var permissions = Substitute.For<IPermissionsServiceClient>();
        var controller = new TenantsController(
            Substitute.For<ITenantService>(),
            permissions,
            Substitute.For<IMarketplaceSeatService>(),
            membership,
            Substitute.For<IHttpContextAccessor>(),
            NullLogger<TenantsController>.Instance);

        var tenantId = Guid.NewGuid();

        var result = await controller.InviteUserToTenant(tenantId, "bursar@school.edu");

        Assert.IsType<NoContentResult>(result);
        await membership.Received(1).CreateInvitationAsync(
            tenantId, "bursar@school.edu", Arg.Any<IEnumerable<string>>());
        // The Graph-based email lookup must no longer be used under Workforce multitenant.
        await permissions.DidNotReceive().AddUserPermissionsToTenantByEmailAsync(
            Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task BindMember_BindsTheCallerFromTheirTokenClaims()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var tenantId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        membership.BindMemberAsync(tenantId, oid, "bursar@school.edu", "Jane Bursar").Returns("Bound");

        var controller = new TenantsController(
            Substitute.For<ITenantService>(),
            Substitute.For<IPermissionsServiceClient>(),
            Substitute.For<IMarketplaceSeatService>(),
            membership,
            Substitute.For<IHttpContextAccessor>(),
            NullLogger<TenantsController>.Instance);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", oid.ToString()),
            new Claim("preferred_username", "bursar@school.edu"),
            new Claim("name", "Jane Bursar"),
        }, "test"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };

        var result = await controller.BindMember(tenantId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Bound", ok.Value);
        // Identity comes from the token, not from client-supplied params.
        await membership.Received(1).BindMemberAsync(tenantId, oid, "bursar@school.edu", "Jane Bursar");
    }
}
