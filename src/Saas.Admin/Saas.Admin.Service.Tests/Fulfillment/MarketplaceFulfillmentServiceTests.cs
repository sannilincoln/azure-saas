using System.Collections.Generic;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Marketplace.SaaS.Accelerator.Services.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class MarketplaceFulfillmentServiceTests
{
    private static MarketplaceFulfillmentService Build(
        IFulfillmentApiService fulfillment,
        MarketplaceOptions options,
        Marketplace.SaaS.Accelerator.DataAccess.Context.SaasKitContext marketplace,
        Saas.Admin.Service.Data.TenantsContext tenants) =>
        new(fulfillment, marketplace, tenants, Options.Create(options),
            NullLogger<MarketplaceFulfillmentService>.Instance);

    private static IFulfillmentApiService FulfillmentResolving(string planId) =>
        ResolvingWith(new ResolvedSubscriptionResult
        {
            SubscriptionId = Guid.NewGuid(),
            SubscriptionName = "Acme",
            OfferId = "offer-1",
            PlanId = planId,
            Quantity = 5,
        });

    private static IFulfillmentApiService ResolvingWith(ResolvedSubscriptionResult result)
    {
        var fulfillment = Substitute.For<IFulfillmentApiService>();
        fulfillment.ResolveAsync(Arg.Any<string>()).Returns(result);
        return fulfillment;
    }

    [Fact]
    public async Task Resolve_MapsPurchasedPlan_ToConfiguredProductTier()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var options = new MarketplaceOptions
        {
            PlanToProductTier = new Dictionary<string, int> { ["premium"] = 3, ["basic"] = 1 },
        };

        var service = Build(FulfillmentResolving("premium"), options, marketplace, tenants);

        var dto = await service.ResolveAsync("token", customerTenantId: Guid.NewGuid());

        Assert.Equal(3, dto.ProductTierId);
    }

    [Fact]
    public async Task Resolve_UnmappedPlan_DefaultsToTierZero()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var options = new MarketplaceOptions
        {
            PlanToProductTier = new Dictionary<string, int> { ["premium"] = 3 },
        };

        var service = Build(FulfillmentResolving("not-in-map"), options, marketplace, tenants);

        var dto = await service.ResolveAsync("token", customerTenantId: Guid.NewGuid());

        Assert.Equal(0, dto.ProductTierId);
    }

    [Fact]
    public async Task Resolve_NoMapConfigured_DefaultsToTierZero()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var options = new MarketplaceOptions(); // PlanToProductTier null

        var service = Build(FulfillmentResolving("premium"), options, marketplace, tenants);

        var dto = await service.ResolveAsync("token", customerTenantId: Guid.NewGuid());

        Assert.Equal(0, dto.ProductTierId);
    }

    [Fact]
    public async Task Resolve_PersistsCustomerTenantId_ForSelfServiceFiltering()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var customerTenant = Guid.NewGuid();

        var service = Build(FulfillmentResolving("premium"), new MarketplaceOptions(), marketplace, tenants);

        var dto = await service.ResolveAsync("token", customerTenant);

        var persisted = Assert.Single(marketplace.Subscriptions);
        Assert.Equal(customerTenant, persisted.PurchaserTenantId);
        Assert.Equal("PendingFulfillmentStart", persisted.SubscriptionStatus);
        Assert.Equal(dto.SubscriptionId, persisted.AmpsubscriptionId);
    }
}
