using System;
using System.Linq;
using Saas.Identity.Authorization.Model.Kind;
using Xunit;

namespace Saas.Admin.Service.Tests.Authorization;

public class TenantRoleTests
{
    [Fact]
    public void Bootstrap_IsSuperAdmin()
    {
        Assert.Equal(TenantRole.SuperAdmin, TenantRole.Bootstrap);
    }

    [Theory]
    [InlineData(TenantRole.SuperAdmin)]
    [InlineData(TenantRole.Admin)]
    public void ToPermissionStrings_AdminRoles_BackByCrudAdmin_PlusRoleTag(string role)
    {
        var perms = TenantRole.ToPermissionStrings(role);

        Assert.Equal(new[] { TenantPermissionKind.Admin.ToString(), $"Role:{role}" }, perms);
    }

    [Theory]
    [InlineData(TenantRole.Bursar)]
    [InlineData(TenantRole.BillingAccountant)]
    [InlineData(TenantRole.ReceivableAccountant)]
    [InlineData(TenantRole.Student)]
    [InlineData(TenantRole.BoardMember)]
    [InlineData(TenantRole.Reviewer)]
    public void ToPermissionStrings_NonAdminRoles_BackByCrudRead_PlusRoleTag(string role)
    {
        var perms = TenantRole.ToPermissionStrings(role);

        Assert.Equal(new[] { TenantPermissionKind.Read.ToString(), $"Role:{role}" }, perms);
    }

    [Fact]
    public void ToPermissionStrings_NormalizesCasing_ToCanonicalTag()
    {
        var perms = TenantRole.ToPermissionStrings("bursar");

        // The stored tag uses the canonical spelling regardless of caller casing.
        Assert.Contains("Role:Bursar", perms);
    }

    [Fact]
    public void ToPermissionStrings_UnknownRole_Throws()
    {
        Assert.Throws<ArgumentException>(() => TenantRole.ToPermissionStrings("Wizard"));
    }

    [Theory]
    [InlineData("Bursar", true)]
    [InlineData("bursar", true)]
    [InlineData("Wizard", false)]
    [InlineData(null, false)]
    public void IsKnown_RecognizesRolesCaseInsensitively(string? role, bool expected)
    {
        Assert.Equal(expected, TenantRole.IsKnown(role));
    }

    [Fact]
    public void FromClaimTag_RoundTripsRoleTags_AndIgnoresPlainPermissions()
    {
        Assert.Equal("Super-Admin", TenantRole.FromClaimTag("Role:Super-Admin"));
        Assert.Null(TenantRole.FromClaimTag("Admin"));   // a CRUD permission, not a role tag
        Assert.Null(TenantRole.FromClaimTag(null));
    }

    [Fact]
    public void All_ContainsSuperAdmin_AndIsDistinct()
    {
        Assert.Contains(TenantRole.SuperAdmin, TenantRole.All);
        Assert.Equal(TenantRole.All.Count, TenantRole.All.Distinct().Count());
    }
}
