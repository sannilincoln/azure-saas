using System;
using System.Collections.Generic;
using System.Linq;
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
using Saas.Identity.Authorization.Model.Kind;
using Saas.Permissions.Client;
using Xunit;

namespace Saas.Admin.Service.Tests.Controllers;

public class TenantsControllerInviteTests
{
    private static TenantsController BuildController(
        ITenantMembershipClient membership,
        IPermissionsServiceClient permissions,
        IEmailSender? emailSender = null) =>
        new(
            Substitute.For<ITenantService>(),
            permissions,
            Substitute.For<IMarketplaceSeatService>(),
            membership,
            emailSender ?? Substitute.For<IEmailSender>(),
            Substitute.For<IHttpContextAccessor>(),
            NullLogger<TenantsController>.Instance);

    [Fact]
    public async Task Invite_CreatesPendingInvitation_AndDoesNotUseGraphEmailLookup()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var permissions = Substitute.For<IPermissionsServiceClient>();
        var emailSender = Substitute.For<IEmailSender>();
        var controller = BuildController(membership, permissions, emailSender);

        var tenantId = Guid.NewGuid();

        var result = await controller.InviteUserToTenant(tenantId, "bursar@school.edu");

        // Invite now returns the email-delivery result (so the UI can warn on failure) rather than 204.
        Assert.IsType<OkObjectResult>(result);
        await membership.Received(1).CreateInvitationAsync(
            tenantId, "bursar@school.edu", Arg.Any<IEnumerable<string>>());
        // The invitee is emailed (Flow 3).
        await emailSender.Received(1).NotifyUserInvitedAsync(
            Arg.Is<UserInvitedNotice>(n => n.Email == "bursar@school.edu"), Arg.Any<System.Threading.CancellationToken>());
        // The Graph-based email lookup must no longer be used under Workforce multitenant.
        await permissions.DidNotReceive().AddUserPermissionsToTenantByEmailAsync(
            Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task Invite_WithRole_GrantsThatRolesPermissionStrings()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var controller = BuildController(membership, Substitute.For<IPermissionsServiceClient>());
        var tenantId = Guid.NewGuid();

        var result = await controller.InviteUserToTenant(tenantId, "bursar@school.edu", TenantRole.Bursar);

        Assert.IsType<OkObjectResult>(result);
        await membership.Received(1).CreateInvitationAsync(
            tenantId,
            "bursar@school.edu",
            Arg.Is<IEnumerable<string>>(p => p.SequenceEqual(TenantRole.ToPermissionStrings(TenantRole.Bursar))));
    }

    [Fact]
    public async Task Invite_WithoutRole_DefaultsToAdmin_ForBackCompat()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var controller = BuildController(membership, Substitute.For<IPermissionsServiceClient>());
        var tenantId = Guid.NewGuid();

        await controller.InviteUserToTenant(tenantId, "owner@school.edu");

        await membership.Received(1).CreateInvitationAsync(
            tenantId,
            "owner@school.edu",
            Arg.Is<IEnumerable<string>>(p => p.SequenceEqual(TenantRole.ToPermissionStrings(TenantRole.Admin))));
    }

    [Fact]
    public async Task Invite_WithUnknownRole_ReturnsBadRequest_AndCreatesNothing()
    {
        var membership = Substitute.For<ITenantMembershipClient>();
        var controller = BuildController(membership, Substitute.For<IPermissionsServiceClient>());

        var result = await controller.InviteUserToTenant(Guid.NewGuid(), "x@school.edu", "Wizard");

        Assert.IsType<BadRequestObjectResult>(result);
        await membership.DidNotReceive().CreateInvitationAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task AssignTenantRole_AddsRolesPermissionStrings_ToExistingUser()
    {
        var permissions = Substitute.For<IPermissionsServiceClient>();
        var controller = BuildController(Substitute.For<ITenantMembershipClient>(), permissions);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await controller.AssignTenantRole(tenantId, userId, TenantRole.BillingAccountant);

        Assert.IsType<NoContentResult>(result);
        await permissions.Received(1).AddUserPermissionsToTenantAsync(
            tenantId,
            userId,
            Arg.Is<IEnumerable<string>>(p => p.SequenceEqual(TenantRole.ToPermissionStrings(TenantRole.BillingAccountant))));
    }

    [Fact]
    public async Task AssignTenantRole_UnknownRole_ReturnsBadRequest_AndGrantsNothing()
    {
        var permissions = Substitute.For<IPermissionsServiceClient>();
        var controller = BuildController(Substitute.For<ITenantMembershipClient>(), permissions);

        var result = await controller.AssignTenantRole(Guid.NewGuid(), Guid.NewGuid(), "Wizard");

        Assert.IsType<BadRequestObjectResult>(result);
        await permissions.DidNotReceive().AddUserPermissionsToTenantAsync(
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public void GetAssignableRoles_ReturnsTheTenantRoleVocabulary()
    {
        var controller = BuildController(
            Substitute.For<ITenantMembershipClient>(), Substitute.For<IPermissionsServiceClient>());

        var ok = Assert.IsType<OkObjectResult>(controller.GetAssignableRoles().Result);
        Assert.Equal(TenantRole.All, ok.Value);
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
            Substitute.For<IEmailSender>(),
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
