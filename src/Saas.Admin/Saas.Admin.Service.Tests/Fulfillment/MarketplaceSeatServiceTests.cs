using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Saas.Permissions.Client;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class MarketplaceSeatServiceTests
{
    private static IPermissionsServiceClient PermissionsWithUserCount(int count)
    {
        var permissions = Substitute.For<IPermissionsServiceClient>();
        ICollection<User> users = Enumerable.Range(0, count)
            .Select(_ => new User { UserId = Guid.NewGuid(), DisplayName = "u" })
            .ToList();
        permissions.GetTenantUsersAsync(Arg.Any<Guid?>()).Returns(users);
        return permissions;
    }

    private static MarketplaceSeatService Build(
        Saas.Admin.Service.Data.TenantsContext tenants,
        Marketplace.SaaS.Accelerator.DataAccess.Context.SaasKitContext marketplace,
        IPermissionsServiceClient permissions) =>
        new(tenants, marketplace, permissions, NullLogger<MarketplaceSeatService>.Instance);

    [Fact]
    public async Task NonMarketplaceTenant_DoesNotEnforce_AndNeverCountsUsers()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: null);

        var permissions = PermissionsWithUserCount(100);
        var service = Build(tenants, marketplace, permissions);

        // Should not throw.
        await service.EnsureSeatAvailableAsync(tenantId);

        // And must not even bother counting users for a non-marketplace tenant.
        await permissions.DidNotReceive().GetTenantUsersAsync(Arg.Any<Guid?>());
    }

    [Fact]
    public async Task QuantityZero_IsTreatedAsUnlimited()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, quantity: 0);
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId);

        var service = Build(tenants, marketplace, PermissionsWithUserCount(999));

        await service.EnsureSeatAvailableAsync(tenantId); // no throw
    }

    [Fact]
    public async Task MissingSubscriptionRow_DoesNotEnforce()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var tenantId = Guid.NewGuid();
        // Tenant linked to a subscription id that has no row in the marketplace store.
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: Guid.NewGuid());

        var service = Build(tenants, marketplace, PermissionsWithUserCount(50));

        await service.EnsureSeatAvailableAsync(tenantId); // no throw
    }

    [Fact]
    public async Task BelowSeatLimit_IsAllowed()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, quantity: 5);
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId);

        // 2 active + 1 = 3 <= 5 → allowed.
        var service = Build(tenants, marketplace, PermissionsWithUserCount(2));

        await service.EnsureSeatAvailableAsync(tenantId); // no throw
    }

    [Fact]
    public async Task AtSeatLimit_RejectsTheNextUser()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, quantity: 2);
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId);

        // 2 active + 1 = 3 > 2 → rejected.
        var service = Build(tenants, marketplace, PermissionsWithUserCount(2));

        var ex = await Assert.ThrowsAsync<SeatLimitExceededException>(
            () => service.EnsureSeatAvailableAsync(tenantId));

        Assert.Equal(2, ex.Seats);
        Assert.Equal(2, ex.ActiveUsers);
    }
}
