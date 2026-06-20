using System.Collections.Generic;
using System.Linq;
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
        Saas.Admin.Service.Data.TenantsContext tenants,
        IProductProvisioningService? provisioning = null) =>
        new(fulfillment, marketplace, tenants, Substitute.For<IMarketplaceNotificationService>(),
            provisioning ?? Substitute.For<IProductProvisioningService>(),
            Options.Create(options), NullLogger<MarketplaceFulfillmentService>.Instance);

    private static IFulfillmentApiService FulfillmentResolving(string planId, Guid? beneficiaryTenantId = null) =>
        ResolvingWith(new ResolvedSubscriptionResult
        {
            SubscriptionId = Guid.NewGuid(),
            SubscriptionName = "Acme",
            OfferId = "offer-1",
            PlanId = planId,
            Quantity = 5,
        }, beneficiaryTenantId);

    private static IFulfillmentApiService ResolvingWith(ResolvedSubscriptionResult result, Guid? beneficiaryTenantId = null)
    {
        var fulfillment = Substitute.For<IFulfillmentApiService>();
        fulfillment.ResolveAsync(Arg.Any<string>()).Returns(result);
        // The service reads the customer-tenant key from the full subscription's beneficiary.
        fulfillment.GetSubscriptionByIdAsync(Arg.Any<Guid>()).Returns(new SubscriptionResult
        {
            Beneficiary = new BeneficiaryResult { TenantId = beneficiaryTenantId ?? Guid.NewGuid() },
        });
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

        var dto = await service.ResolveAsync("token");

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

        var dto = await service.ResolveAsync("token");

        Assert.Equal(0, dto.ProductTierId);
    }

    [Fact]
    public async Task Resolve_NoMapConfigured_DefaultsToTierZero()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var options = new MarketplaceOptions(); // PlanToProductTier null

        var service = Build(FulfillmentResolving("premium"), options, marketplace, tenants);

        var dto = await service.ResolveAsync("token");

        Assert.Equal(0, dto.ProductTierId);
    }

    [Fact]
    public async Task Resolve_PersistsCustomerTenantId_ForSelfServiceFiltering()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var customerTenant = Guid.NewGuid();

        var service = Build(FulfillmentResolving("premium", beneficiaryTenantId: customerTenant), new MarketplaceOptions(), marketplace, tenants);

        var dto = await service.ResolveAsync("token");

        var persisted = Assert.Single(marketplace.Subscriptions);
        Assert.Equal(customerTenant, persisted.PurchaserTenantId);
        Assert.Equal("PendingFulfillmentStart", persisted.SubscriptionStatus);
        Assert.Equal(dto.SubscriptionId, persisted.AmpsubscriptionId);
    }

    [Fact]
    public async Task Activate_WithDatabasePrefix_PersistsDatabaseName_AndProvisions()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId);
        var tenant = MarketplaceTestHelpers.SeedTenant(tenants, tenantId);

        var provisioning = Substitute.For<IProductProvisioningService>();
        var options = new MarketplaceOptions { TenantDatabaseNamePrefix = "edulynk" };
        var service = Build(Substitute.For<IFulfillmentApiService>(), options, marketplace, tenants, provisioning);

        await service.ActivateAsync(subId, tenantId);

        var expectedDb = $"edulynk-{tenant.Route}";
        var persisted = tenants.Tenants.Single(t => t.Id == tenantId);
        Assert.Equal(expectedDb, persisted.DatabaseName);
        await provisioning.Received(1).ProvisionAsync(tenantId, expectedDb);
    }

    [Fact]
    public async Task Activate_WithoutDatabasePrefix_DoesNotSetNameOrProvision()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId);
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId);

        var provisioning = Substitute.For<IProductProvisioningService>();
        var service = Build(Substitute.For<IFulfillmentApiService>(), new MarketplaceOptions(), marketplace, tenants, provisioning);

        await service.ActivateAsync(subId, tenantId);

        var persisted = tenants.Tenants.Single(t => t.Id == tenantId);
        Assert.Null(persisted.DatabaseName);
        await provisioning.DidNotReceive().ProvisionAsync(Arg.Any<Guid>(), Arg.Any<string>());
    }
}
