using System.Linq;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class SubscriptionQueryServiceTests
{
    private static SubscriptionQueryService Build(
        Marketplace.SaaS.Accelerator.DataAccess.Context.SaasKitContext marketplace,
        Saas.Admin.Service.Data.TenantsContext tenants) =>
        new(marketplace, tenants, Substitute.For<IFulfillmentApiService>(), NullLogger<SubscriptionQueryService>.Instance);

    [Fact]
    public async Task GetByCustomerTenant_ReturnsOnlyThatTenantsSubscriptions()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();

        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: customerA);
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: customerA);
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: customerB);

        var service = Build(marketplace, tenants);

        var aSubs = await service.GetByCustomerTenantAsync(customerA);
        var bSubs = await service.GetByCustomerTenantAsync(customerB);

        Assert.Equal(2, aSubs.Count);
        Assert.Single(bSubs);
        Assert.All(aSubs, s => Assert.Equal(customerA, s.CustomerTenantId));
    }

    [Fact]
    public async Task GetAll_ReturnsEverySubscription_WithLinkedTenantName()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();

        var subId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, purchaserTenantId: Guid.NewGuid());
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: Guid.NewGuid());
        var tenant = MarketplaceTestHelpers.SeedTenant(tenants, Guid.NewGuid(), subscriptionId: subId);

        var service = Build(marketplace, tenants);

        var all = await service.GetAllAsync();

        Assert.Equal(2, all.Count);
        var linked = all.Single(s => s.SubscriptionId == subId);
        Assert.Equal(tenant.Id, linked.TenantId);
        Assert.Equal(tenant.Name, linked.TenantName);
    }

    [Fact]
    public async Task GetByCustomerTenant_NoMatches_ReturnsEmpty()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: Guid.NewGuid());

        var service = Build(marketplace, tenants);

        var result = await service.GetByCustomerTenantAsync(Guid.NewGuid());

        Assert.Empty(result);
    }
}
