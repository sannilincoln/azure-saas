using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Saas.Admin.Service.Data;

namespace Saas.Admin.Service.Fulfillment;

public class SubscriptionQueryService(
    SaasKitContext marketplaceDb,
    TenantsContext tenantsDb,
    IFulfillmentApiService fulfillmentApi,
    ILogger<SubscriptionQueryService> logger) : ISubscriptionQueryService
{
    public async Task<IReadOnlyList<SubscriptionDto>> GetAllAsync()
    {
        var subscriptions = await marketplaceDb.Subscriptions.AsNoTracking().ToListAsync();
        return await ProjectAsync(subscriptions);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetByCustomerTenantAsync(Guid customerTenantId)
    {
        var subscriptions = await marketplaceDb.Subscriptions.AsNoTracking()
            .Where(s => s.PurchaserTenantId == customerTenantId)
            .ToListAsync();
        return await ProjectAsync(subscriptions);
    }

    public async Task<SubscriptionDto?> RefreshFromMarketplaceAsync(Guid subscriptionId)
    {
        var subscription = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId);
        if (subscription is null)
        {
            return null;
        }

        var live = await fulfillmentApi.GetSubscriptionByIdAsync(subscriptionId);
        if (live is null)
        {
            logger.LogWarning("Refresh: Microsoft returned no subscription for {SubscriptionId}.", subscriptionId);
            return await ToDtoAsync(subscription);
        }

        subscription.SubscriptionStatus = live.SaasSubscriptionStatus.ToString();
        subscription.AmpplanId = live.PlanId ?? subscription.AmpplanId;
        subscription.Ampquantity = live.Quantity > 0 ? live.Quantity : subscription.Ampquantity;
        subscription.IsActive = subscription.SubscriptionStatus == "Subscribed";
        subscription.ModifyDate = DateTime.UtcNow;
        await marketplaceDb.SaveChangesAsync();

        await SyncTenantStatusAsync(subscriptionId, subscription.SubscriptionStatus);

        logger.LogInformation("Refreshed subscription {SubscriptionId} from Microsoft -> {Status}.",
            subscriptionId, subscription.SubscriptionStatus);

        return await ToDtoAsync(subscription);
    }

    public async Task<SubscriptionDto?> OverrideStatusAsync(Guid subscriptionId, string status)
    {
        var subscription = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId);
        if (subscription is null)
        {
            return null;
        }

        subscription.SubscriptionStatus = status;
        subscription.IsActive = status == "Subscribed";
        subscription.ModifyDate = DateTime.UtcNow;
        await marketplaceDb.SaveChangesAsync();

        await SyncTenantStatusAsync(subscriptionId, status);

        logger.LogWarning("Publisher override: subscription {SubscriptionId} status set to {Status}.",
            subscriptionId, status);

        return await ToDtoAsync(subscription);
    }

    private async Task SyncTenantStatusAsync(Guid subscriptionId, string status)
    {
        var tenant = await tenantsDb.Tenants.FirstOrDefaultAsync(t => t.SubscriptionId == subscriptionId);
        if (tenant is not null)
        {
            tenant.SubscriptionStatus = status;
            await tenantsDb.SaveChangesAsync();
        }
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ProjectAsync(
        IReadOnlyList<Marketplace.SaaS.Accelerator.DataAccess.Entities.Subscriptions> subscriptions)
    {
        if (subscriptions.Count == 0)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var ampIds = subscriptions.Select(s => s.AmpsubscriptionId).ToList();
        var tenants = await tenantsDb.Tenants.AsNoTracking()
            .Where(t => t.SubscriptionId != null && ampIds.Contains(t.SubscriptionId!.Value))
            .ToDictionaryAsync(t => t.SubscriptionId!.Value, t => t);

        return subscriptions.Select(s =>
        {
            tenants.TryGetValue(s.AmpsubscriptionId, out var tenant);
            return Map(s, tenant);
        }).ToList();
    }

    private async Task<SubscriptionDto> ToDtoAsync(Marketplace.SaaS.Accelerator.DataAccess.Entities.Subscriptions s)
    {
        var tenant = await tenantsDb.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.SubscriptionId == s.AmpsubscriptionId);
        return Map(s, tenant);
    }

    private static SubscriptionDto Map(
        Marketplace.SaaS.Accelerator.DataAccess.Entities.Subscriptions s,
        Tenant? tenant) => new()
        {
            SubscriptionId = s.AmpsubscriptionId,
            Name = s.Name,
            OfferId = s.AmpOfferId,
            PlanId = s.AmpplanId,
            Quantity = s.Ampquantity,
            Status = s.SubscriptionStatus,
            PurchaserEmail = s.PurchaserEmail,
            CustomerTenantId = s.PurchaserTenantId,
            TenantId = tenant?.Id,
            TenantName = tenant?.Name,
            CreatedTime = s.CreateDate,
        };
}
