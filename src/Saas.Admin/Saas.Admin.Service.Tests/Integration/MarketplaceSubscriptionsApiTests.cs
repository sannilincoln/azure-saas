using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Saas.Admin.Service.Fulfillment;
using Saas.Admin.Service.Tests.Fulfillment;
using Xunit;

namespace Saas.Admin.Service.Tests.Integration;

/// <summary>
/// End-to-end HTTP tests of the marketplace subscriptions authorization boundary. Unlike the
/// controller unit tests (which new-up the controller and hand it a fabricated principal), these go
/// through the real Kestrel-less pipeline: authentication → <c>[Authorize]</c> → routing → model
/// binding → the controller → <see cref="SubscriptionQueryService"/> → EF. The caller's tenant is
/// chosen per request via <see cref="MarketplaceApiFactory.CreateClient"/>.
/// </summary>
public class MarketplaceSubscriptionsApiTests
{
    [Fact]
    public async Task GetAll_AnonymousCaller_Returns401()
    {
        using var factory = new MarketplaceApiFactory();
        using var client = factory.CreateClient(callerTenantId: null);

        var response = await client.GetAsync("api/marketplace/subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_PublisherCaller_ReturnsEveryTenantsSubscriptions()
    {
        using var factory = new MarketplaceApiFactory();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        factory.Seed((mkt, tenants) =>
        {
            MarketplaceTestHelpers.SeedSubscription(mkt, Guid.NewGuid(), purchaserTenantId: customerA);
            MarketplaceTestHelpers.SeedSubscription(mkt, Guid.NewGuid(), purchaserTenantId: customerB);
        });

        using var client = factory.CreateClient(factory.PublisherTenantId);
        var response = await client.GetAsync("api/marketplace/subscriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var subs = await response.Content.ReadFromJsonAsync<List<SubscriptionDto>>();
        Assert.NotNull(subs);
        Assert.Equal(2, subs!.Count);
    }

    [Fact]
    public async Task GetAll_CustomerCaller_Returns403()
    {
        using var factory = new MarketplaceApiFactory();
        factory.Seed((mkt, _) => MarketplaceTestHelpers.SeedSubscription(mkt, Guid.NewGuid(), purchaserTenantId: Guid.NewGuid()));

        // A non-publisher tenant is authenticated, but must be forbidden from the publisher endpoint.
        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.GetAsync("api/marketplace/subscriptions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyTheCallersOwnSubscriptions()
    {
        using var factory = new MarketplaceApiFactory();
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var mySubId = Guid.NewGuid();
        factory.Seed((mkt, _) =>
        {
            MarketplaceTestHelpers.SeedSubscription(mkt, mySubId, purchaserTenantId: me);
            MarketplaceTestHelpers.SeedSubscription(mkt, Guid.NewGuid(), purchaserTenantId: someoneElse);
        });

        using var client = factory.CreateClient(callerTenantId: me);
        var response = await client.GetAsync("api/marketplace/subscriptions/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var subs = await response.Content.ReadFromJsonAsync<List<SubscriptionDto>>();
        Assert.NotNull(subs);
        var only = Assert.Single(subs!);
        Assert.Equal(mySubId, only.SubscriptionId);
        Assert.Equal(me, only.CustomerTenantId);
    }

    [Fact]
    public async Task OverrideStatus_CustomerCaller_Returns403_AndLeavesStateUntouched()
    {
        using var factory = new MarketplaceApiFactory();
        var subId = Guid.NewGuid();
        factory.Seed((mkt, _) => MarketplaceTestHelpers.SeedSubscription(mkt, subId, status: "Subscribed", purchaserTenantId: Guid.NewGuid()));

        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.PostAsJsonAsync(
            $"api/marketplace/subscriptions/{subId}/status",
            new OverrideStatusRequest("Suspended"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var stored = factory.Query((mkt, _) => mkt.Subscriptions.Single(s => s.AmpsubscriptionId == subId).SubscriptionStatus);
        Assert.Equal("Subscribed", stored);
    }

    [Fact]
    public async Task OverrideStatus_PublisherCaller_WritesThroughToSubscriptionAndTenant()
    {
        using var factory = new MarketplaceApiFactory();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        factory.Seed((mkt, tenants) =>
        {
            MarketplaceTestHelpers.SeedSubscription(mkt, subId, status: "Subscribed", purchaserTenantId: Guid.NewGuid());
            MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId, subscriptionStatus: "Subscribed");
        });

        using var client = factory.CreateClient(factory.PublisherTenantId);
        var response = await client.PostAsJsonAsync(
            $"api/marketplace/subscriptions/{subId}/status",
            new OverrideStatusRequest("Suspended"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The denormalized tenant status is kept in sync — this is what the product app's gate reads.
        var (subStatus, tenantStatus) = factory.Query((mkt, tenants) => (
            mkt.Subscriptions.Single(s => s.AmpsubscriptionId == subId).SubscriptionStatus,
            tenants.Tenants.Single(t => t.Id == tenantId).SubscriptionStatus));
        Assert.Equal("Suspended", subStatus);
        Assert.Equal("Suspended", tenantStatus);
    }
}
