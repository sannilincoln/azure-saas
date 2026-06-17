using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Saas.Admin.Service.Controllers;
using Saas.Admin.Service.Tests.Fulfillment;
using Xunit;

namespace Saas.Admin.Service.Tests.Integration;

/// <summary>
/// End-to-end HTTP tests of the by-tid tenant lookup the multitenant product API uses to turn a
/// request's Entra tenant id (token <c>tid</c>) into its provisioned tenant + per-tenant database
/// name. Service-to-service endpoint: any authenticated caller is allowed (Phase 4.3 tightens to the
/// service app-role), unlike the publisher/customer-scoped subscriptions endpoints.
/// </summary>
public class TenantResolutionApiTests
{
    [Fact]
    public async Task ByTid_AnonymousCaller_Returns401()
    {
        using var factory = new MarketplaceApiFactory();
        using var client = factory.CreateClient(callerTenantId: null);

        var response = await client.GetAsync($"api/tenants/by-tid/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ByTid_ProvisionedCustomer_ReturnsTenantInfoWithDatabaseName()
    {
        using var factory = new MarketplaceApiFactory();
        var customerTenantId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        factory.Seed((mkt, tenants) =>
        {
            MarketplaceTestHelpers.SeedSubscription(mkt, subId, status: "Subscribed", purchaserTenantId: customerTenantId);
            MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId,
                subscriptionStatus: "Subscribed", databaseName: "edulynk-route-x");
        });

        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.GetAsync($"api/tenants/by-tid/{customerTenantId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<TenantInfoDTO>();
        Assert.NotNull(info);
        Assert.Equal(tenantId, info!.Id);
        Assert.Equal("edulynk-route-x", info.DatabaseName);
        Assert.Equal("Subscribed", info.SubscriptionStatus);
    }

    [Fact]
    public async Task ByTid_UnknownCustomer_Returns404()
    {
        using var factory = new MarketplaceApiFactory();
        factory.Seed((mkt, _) => MarketplaceTestHelpers.SeedSubscription(mkt, Guid.NewGuid(), purchaserTenantId: Guid.NewGuid()));

        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.GetAsync($"api/tenants/by-tid/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
