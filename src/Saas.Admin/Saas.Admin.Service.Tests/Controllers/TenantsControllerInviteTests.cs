using System;
using System.Collections.Generic;
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
}
