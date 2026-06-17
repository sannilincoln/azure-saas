using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Saas.Identity.Authorization.Model.Data;
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
}
