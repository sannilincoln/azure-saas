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

    [Fact]
    public async Task GetTenantByCustomerTenant_ResolvesTenantViaSubscriptionPurchaserTenant()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();

        var customerTenantId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, purchaserTenantId: customerTenantId);
        var tenant = MarketplaceTestHelpers.SeedTenant(tenants, Guid.NewGuid(), subscriptionId: subId);

        var service = Build(marketplace, tenants);

        var resolved = await service.GetTenantByCustomerTenantAsync(customerTenantId);

        Assert.NotNull(resolved);
        Assert.Equal(tenant.Id, resolved!.Id);
        Assert.Equal(tenant.Route, resolved.Route);
    }

    [Fact]
    public async Task GetTenantByCustomerTenant_PrefersActiveSubscription_WhenTenantHasMultiple()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();

        var customerTenantId = Guid.NewGuid();

        // A cancelled-then-rebought customer: an Unsubscribed sub (seeded first) + an active one.
        var staleSubId = Guid.NewGuid();
        var activeSubId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, staleSubId, status: "Unsubscribed", purchaserTenantId: customerTenantId);
        MarketplaceTestHelpers.SeedSubscription(marketplace, activeSubId, status: "Subscribed", purchaserTenantId: customerTenantId);
        MarketplaceTestHelpers.SeedTenant(tenants, Guid.NewGuid(), subscriptionId: staleSubId, subscriptionStatus: "Unsubscribed");
        var activeTenant = MarketplaceTestHelpers.SeedTenant(tenants, Guid.NewGuid(), subscriptionId: activeSubId, subscriptionStatus: "Subscribed");

        var service = Build(marketplace, tenants);

        var resolved = await service.GetTenantByCustomerTenantAsync(customerTenantId);

        Assert.NotNull(resolved);
        Assert.Equal(activeTenant.Id, resolved!.Id);
    }

    [Fact]
    public async Task GetTenantByCustomerTenant_UnknownEntraTenant_ReturnsNull()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        MarketplaceTestHelpers.SeedSubscription(marketplace, Guid.NewGuid(), purchaserTenantId: Guid.NewGuid());

        var service = Build(marketplace, tenants);

        var resolved = await service.GetTenantByCustomerTenantAsync(Guid.NewGuid());

        Assert.Null(resolved);
    }
}
