using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Saas.Admin.Service.Fulfillment;
using Saas.Admin.Service.Tests.Fulfillment;
using Xunit;

namespace Saas.Admin.Service.Tests.Integration;

/// <summary>
/// End-to-end HTTP tests of the tenant quota endpoint through the real authentication → [Authorize] →
/// routing → controller → <see cref="TenantQuotaService"/> → EF pipeline.
/// </summary>
public class TenantQuotaApiTests
{
    [Fact]
    public async Task Get_AnonymousCaller_Returns401()
    {
        using var factory = new MarketplaceApiFactory();
        using var client = factory.CreateClient(callerTenantId: null);

        var response = await client.GetAsync($"api/tenants/{Guid.NewGuid()}/quota");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuthenticatedCaller_ReturnsTheTenantsCeiling()
    {
        using var factory = new MarketplaceApiFactory();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        factory.Seed((mkt, tenants) =>
        {
            MarketplaceTestHelpers.SeedSubscription(mkt, subId);
            MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId, productTierId: 7);
        });

        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.GetAsync($"api/tenants/{tenantId}/quota");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quota = await response.Content.ReadFromJsonAsync<TenantQuota>();
        Assert.NotNull(quota);
        Assert.Equal(tenantId, quota!.TenantId);
        Assert.Equal(2000, quota.MaxStudents);   // tier 7 -> 2000 in the factory's TierMaxStudents
    }

    [Fact]
    public async Task Get_UnknownTenant_Returns404()
    {
        using var factory = new MarketplaceApiFactory();

        using var client = factory.CreateClient(callerTenantId: Guid.NewGuid());
        var response = await client.GetAsync($"api/tenants/{Guid.NewGuid()}/quota");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
