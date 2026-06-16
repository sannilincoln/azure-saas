using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saas.Admin.Service.Data;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class TenantQuotaServiceTests
{
    private static TenantQuotaService Build(
        TenantsContext tenants,
        SaasKitContext marketplace,
        MarketplaceOptions options) =>
        new(tenants, marketplace, Options.Create(options), NullLogger<TenantQuotaService>.Instance);

    [Fact]
    public async Task MappedTier_ReturnsItsStudentCeiling()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId);
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId, productTierId: 7);

        var options = new MarketplaceOptions
        {
            TierMaxStudents = new Dictionary<int, int> { [6] = 500, [7] = 2000, [8] = 10000 },
        };
        var service = Build(tenants, marketplace, options);

        var quota = await service.GetQuotaAsync(tenantId);

        Assert.NotNull(quota);
        Assert.Equal(2000, quota!.MaxStudents);
    }

    [Fact]
    public async Task TierNotInMap_FailsClosed_BlocksAllStudents()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, productTierId: 99);

        var options = new MarketplaceOptions
        {
            TierMaxStudents = new Dictionary<int, int> { [6] = 500, [7] = 2000 },
        };
        var quota = await Build(tenants, marketplace, options).GetQuotaAsync(tenantId);

        Assert.NotNull(quota);
        Assert.Equal(0, quota!.MaxStudents);   // 0 == no students may be registered
    }

    [Fact]
    public async Task AbsentMap_FailsClosed_BlocksAllStudents()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, productTierId: 7);

        // No TierMaxStudents configured at all.
        var quota = await Build(tenants, marketplace, new MarketplaceOptions()).GetQuotaAsync(tenantId);

        Assert.NotNull(quota);
        Assert.Equal(0, quota!.MaxStudents);   // 0 == no students may be registered
    }

    [Fact]
    public async Task UnknownTenant_ReturnsNull()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();

        var quota = await Build(tenants, marketplace, new MarketplaceOptions())
            .GetQuotaAsync(Guid.NewGuid());

        Assert.Null(quota);
    }

    [Fact]
    public async Task SurfacesPlanIdAndSubscriptionStatus()
    {
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, status: "Suspended");
        MarketplaceTestHelpers.SeedTenant(
            tenants, tenantId, subscriptionId: subId, subscriptionStatus: "Suspended", productTierId: 7);

        var options = new MarketplaceOptions
        {
            TierMaxStudents = new Dictionary<int, int> { [7] = 2000 },
        };
        var quota = await Build(tenants, marketplace, options).GetQuotaAsync(tenantId);

        Assert.NotNull(quota);
        Assert.Equal("plan-1", quota!.PlanId);          // seeded AmpplanId
        Assert.Equal("Suspended", quota.SubscriptionStatus);
        Assert.Equal(7, quota.ProductTierId);
    }
}
