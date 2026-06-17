using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Saas.Admin.Service.Data;

namespace Saas.Admin.Service.Tests.Fulfillment;

/// <summary>
/// Builds throwaway in-memory EF contexts for the marketplace unit tests. The InMemory provider is
/// used deliberately (rather than SQLite) because the vendored accelerator <see cref="SaasKitContext"/>
/// declares SQL-Server-specific column types and a <c>newid()</c> default that SQLite can't create;
/// InMemory ignores relational concerns, so the model loads cleanly.
/// </summary>
internal static class MarketplaceTestHelpers
{
    public static SaasKitContext NewMarketplaceDb() =>
        new(new DbContextOptionsBuilder<SaasKitContext>()
            .UseInMemoryDatabase($"mkt-{Guid.NewGuid()}")
            .Options);

    public static TenantsContext NewTenantsDb() =>
        new(new DbContextOptionsBuilder<TenantsContext>()
            .UseInMemoryDatabase($"tenants-{Guid.NewGuid()}")
            .Options);

    public static Subscriptions SeedSubscription(
        SaasKitContext db,
        Guid ampSubscriptionId,
        int quantity = 5,
        string status = "Subscribed",
        Guid? purchaserTenantId = null)
    {
        var subscription = new Subscriptions
        {
            AmpsubscriptionId = ampSubscriptionId,
            Ampquantity = quantity,
            SubscriptionStatus = status,
            AmpplanId = "plan-1",
            AmpOfferId = "offer-1",
            Name = "Test sub",
            // The vendored entity declares these as non-nullable; the InMemory provider enforces it.
            PurchaserEmail = "buyer@example.com",
            Term = "P1M",
            IsActive = status == "Subscribed",
            PurchaserTenantId = purchaserTenantId,
            CreateDate = DateTime.UtcNow,
        };
        db.Subscriptions.Add(subscription);
        db.SaveChanges();
        return subscription;
    }

    public static Tenant SeedTenant(
        TenantsContext db,
        Guid tenantId,
        Guid? subscriptionId = null,
        string? subscriptionStatus = null,
        int productTierId = 0,
        string? databaseName = null)
    {
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = $"tenant-{tenantId:N}",
            Route = $"route-{tenantId:N}",
            CreatorEmail = "owner@example.com",
            ProductTierId = productTierId,
            // Normally DB/SaveChanges-generated; the InMemory provider skips that, and the model
            // marks it required, so set it explicitly here.
            CreatedTime = DateTime.UtcNow,
            SubscriptionId = subscriptionId,
            SubscriptionStatus = subscriptionStatus,
            DatabaseName = databaseName,
        };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }
}
