using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Saas.Identity.Authorization.Model.Data;
using Saas.Identity.Authorization.Model.Kind;
using Saas.Permissions.Service.Data.Context;
using Saas.Permissions.Service.Interfaces;
using Saas.Permissions.Service.Services;
using Xunit;

namespace Saas.Permissions.Service.Tests;

public class TenantMembershipServiceTests
{
    private static SaasPermissionsContext NewDb() =>
        new(new DbContextOptionsBuilder<SaasPermissionsContext>()
            .UseInMemoryDatabase($"perm-{Guid.NewGuid()}")
            .Options);

    private static TenantMembershipService Build(SaasPermissionsContext db) =>
        new(db, NullLogger<TenantMembershipService>.Instance);

    [Fact]
    public async Task CreateInvitation_WritesPendingRow_NormalizingEmail()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();

        await Build(db).CreateInvitationAsync(tenantId, "  Bursar@School.EDU ", new[] { "Read", "fee.post" });

        var inv = db.TenantInvitations.Single();
        Assert.Equal(tenantId, inv.TenantId);
        Assert.Equal("bursar@school.edu", inv.Email);
        Assert.Equal(TenantInvitationStatus.Pending, inv.Status);
        Assert.Equal("Read;fee.post", inv.PermissionsCsv);
    }

    [Fact]
    public async Task Bind_WithMatchingInvitation_GrantsPermissions_RecordsMember_MarksBound()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = Build(db);
        await svc.CreateInvitationAsync(tenantId, "bursar@school.edu", new[] { "Read", "fee.post" });

        // Email casing differs from the invitation — must still match.
        var result = await svc.BindMemberAsync(tenantId, userId, "Bursar@School.edu", "Jane Bursar");

        Assert.Equal(TenantBindResult.Bound, result);

        var member = db.TenantMembers.Single(m => m.TenantId == tenantId && m.UserId == userId);
        Assert.Equal("bursar@school.edu", member.Email);
        Assert.Equal("Jane Bursar", member.DisplayName);

        var grant = db.SaasPermissions
            .Include(p => p.TenantPermissions)
            .Single(p => p.TenantId == tenantId && p.UserId == userId);
        Assert.Equal(new[] { "Read", "fee.post" }, grant.TenantPermissions.Select(p => p.PermissionStr).ToArray());

        Assert.Equal(TenantInvitationStatus.Bound, db.TenantInvitations.Single().Status);
    }

    [Fact]
    public async Task Bind_WithNoInvitation_DeniesAccess_AndCreatesNothing()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await Build(db).BindMemberAsync(tenantId, userId, "stranger@school.edu", "Stranger");

        Assert.Equal(TenantBindResult.NoInvitation, result);
        Assert.Empty(db.TenantMembers);
        Assert.Empty(db.SaasPermissions);
    }

    [Fact]
    public async Task Bind_WhenAlreadyMember_IsIdempotent()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = Build(db);
        await svc.CreateInvitationAsync(tenantId, "bursar@school.edu", new[] { "Read" });

        var first = await svc.BindMemberAsync(tenantId, userId, "bursar@school.edu", "Jane");
        var second = await svc.BindMemberAsync(tenantId, userId, "bursar@school.edu", "Jane");

        Assert.Equal(TenantBindResult.Bound, first);
        Assert.Equal(TenantBindResult.AlreadyMember, second);
        Assert.Single(db.TenantMembers);     // no duplicate member
        Assert.Single(db.SaasPermissions);   // no duplicate grant
    }

    [Fact]
    public async Task Bind_WhenMemberExistsWithoutIdentity_FillsItOnFirstSignIn()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        // Pre-created member (e.g. the tenant creator) recorded before they ever signed in.
        db.TenantMembers.Add(new TenantMember { TenantId = tenantId, UserId = userId });
        await db.SaveChangesAsync();

        var result = await Build(db).BindMemberAsync(tenantId, userId, "Admin@School.edu", "Tenant Admin");

        Assert.Equal(TenantBindResult.AlreadyMember, result);
        var member = db.TenantMembers.Single();
        Assert.Equal("admin@school.edu", member.Email);
        Assert.Equal("Tenant Admin", member.DisplayName);
    }

    [Fact]
    public async Task AddNewTenant_RecordsTheCreatorAsMember()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = new PermissionsService(db, NullLogger<PermissionsService>.Instance, Substitute.For<IGraphAPIService>());

        await svc.AddNewTenantAsync(tenantId, userId);

        // The creator is a member immediately, so they aren't denied on first sign-in (no invitation).
        var member = db.TenantMembers.Single(m => m.TenantId == tenantId && m.UserId == userId);
        Assert.Equal(tenantId, member.TenantId);
        Assert.Equal(userId, member.UserId);
    }

    [Fact]
    public async Task GetMembers_ReturnsEachMemberWithTheirRoleTags()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var bursarId = Guid.NewGuid();

        var permissionsSvc = new PermissionsService(db, NullLogger<PermissionsService>.Instance, Substitute.For<IGraphAPIService>());
        await permissionsSvc.AddNewTenantAsync(tenantId, creatorId); // creator -> Super-Admin

        var membership = Build(db);
        await membership.CreateInvitationAsync(tenantId, "bursar@school.edu", TenantRoleStrings(TenantRole.Bursar));
        await membership.BindMemberAsync(tenantId, bursarId, "bursar@school.edu", "Jane Bursar");

        var members = await membership.GetMembersAsync(tenantId);

        var creator = members.Single(m => m.UserId == creatorId);
        Assert.Contains(TenantRole.SuperAdmin, creator.Roles);

        var bursar = members.Single(m => m.UserId == bursarId);
        Assert.Equal("bursar@school.edu", bursar.Email);
        Assert.Equal("Jane Bursar", bursar.DisplayName);
        Assert.Equal(new[] { TenantRole.Bursar }, bursar.Roles);
    }

    private static string[] TenantRoleStrings(string role) => TenantRole.ToPermissionStrings(role);

    [Fact]
    public async Task AddNewTenant_BootstrapsTheCreatorAsSuperAdmin()
    {
        using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var svc = new PermissionsService(db, NullLogger<PermissionsService>.Instance, Substitute.For<IGraphAPIService>());

        await svc.AddNewTenantAsync(tenantId, userId);

        var grant = db.SaasPermissions
            .Include(p => p.TenantPermissions)
            .Single(p => p.TenantId == tenantId && p.UserId == userId);
        var tenantPerms = grant.TenantPermissions.Select(p => p.PermissionStr).ToArray();

        // Creator gets the CRUD Admin permission (API authorization) AND the Super-Admin role tag (UI).
        Assert.Equal(TenantRole.ToPermissionStrings(TenantRole.SuperAdmin), tenantPerms);
        Assert.Contains("Role:Super-Admin", tenantPerms);
    }
}
