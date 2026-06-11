using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.DataAccess.Entities;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saas.Admin.Service.Data;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

public class MarketplaceFulfillmentService(
    IFulfillmentApiService fulfillmentApi,
    SaasKitContext marketplaceDb,
    TenantsContext tenantsDb,
    IMarketplaceNotificationService notifications,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<MarketplaceFulfillmentService> logger) : IMarketplaceFulfillmentService
{
    private const string StatusPendingFulfillmentStart = "PendingFulfillmentStart";
    private const string StatusSubscribed = "Subscribed";

    public async Task<ResolvedSubscriptionDto> ResolveAsync(string marketplaceToken, Guid? customerTenantId)
    {
        var resolved = await fulfillmentApi.ResolveAsync(marketplaceToken)
            ?? throw new InvalidOperationException("Marketplace token could not be resolved (expired, already used, or invalid).");

        // Persist immediately and durably — the token is single-use/24h, so we never rely on
        // the onboarding session to carry the subscription forward.
        var subscription = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == resolved.SubscriptionId);

        if (subscription is null)
        {
            subscription = new Subscriptions
            {
                AmpsubscriptionId = resolved.SubscriptionId,
                CreateDate = DateTime.UtcNow,
            };
            marketplaceDb.Subscriptions.Add(subscription);
        }

        subscription.Name = resolved.SubscriptionName;
        subscription.AmpplanId = resolved.PlanId;
        subscription.AmpOfferId = resolved.OfferId;
        subscription.Ampquantity = resolved.Quantity;
        subscription.SubscriptionStatus = StatusPendingFulfillmentStart;
        subscription.IsActive = true;
        subscription.ModifyDate = DateTime.UtcNow;

        // The buyer administers their own purchase: record their home tenant so customer
        // self-service can later show them only their own subscription(s). Don't overwrite a
        // previously captured value with null on a re-resolve.
        if (customerTenantId is Guid tenantId)
        {
            subscription.PurchaserTenantId = tenantId;
        }

        await marketplaceDb.SaveChangesAsync();

        var productTierId = MapPlanToProductTier(resolved.PlanId);

        logger.LogInformation("Resolved + persisted marketplace subscription {SubscriptionId} (plan {PlanId} -> tier {ProductTierId}, qty {Quantity}).",
            resolved.SubscriptionId, resolved.PlanId, productTierId, resolved.Quantity);

        return new ResolvedSubscriptionDto
        {
            SubscriptionId = resolved.SubscriptionId,
            SubscriptionName = resolved.SubscriptionName,
            OfferId = resolved.OfferId,
            PlanId = resolved.PlanId,
            Quantity = resolved.Quantity,
            ProductTierId = productTierId,
        };
    }

    public async Task ActivateAsync(Guid subscriptionId, Guid tenantId)
    {
        var subscription = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} not found; resolve must run before activate.");

        // Activate with Microsoft — this starts billing, so it must happen only after
        // onboarding has succeeded (otherwise we'd charge for a tenant that never provisioned).
        await fulfillmentApi.ActivateSubscriptionAsync(subscriptionId, subscription.AmpplanId);

        subscription.SubscriptionStatus = StatusSubscribed;
        subscription.ModifyDate = DateTime.UtcNow;
        await marketplaceDb.SaveChangesAsync();

        var tenant = await tenantsDb.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found when linking subscription {subscriptionId}.");
        tenant.SubscriptionId = subscriptionId;
        tenant.SubscriptionStatus = StatusSubscribed;
        await tenantsDb.SaveChangesAsync();

        logger.LogInformation("Activated marketplace subscription {SubscriptionId} and linked it to tenant {TenantId}.",
            subscriptionId, tenantId);

        // Best-effort: tell the publisher a tenant just signed up. This never throws (the activation
        // itself is already committed; an email hiccup must not fail the customer's onboarding).
        await notifications.NotifySubscriptionActivatedAsync(new SubscriptionActivatedNotice(
            SubscriptionId: subscriptionId,
            SubscriptionName: subscription.Name,
            OfferId: subscription.AmpOfferId,
            PlanId: subscription.AmpplanId,
            Quantity: subscription.Ampquantity,
            TenantName: tenant.Name,
            TenantRoute: tenant.Route,
            CustomerEmail: tenant.CreatorEmail));
    }

    /// <summary>
    /// Maps the purchased marketplace plan id to this product's internal ProductTier id via the
    /// configured Marketplace:PlanToProductTier map. Returns 0 (default tier) when the map is
    /// absent or the plan isn't listed — onboarding still proceeds, just at the default tier.
    /// </summary>
    private int MapPlanToProductTier(string? planId)
    {
        var map = marketplaceOptions.Value.PlanToProductTier;
        if (map is null || string.IsNullOrWhiteSpace(planId))
        {
            return 0;
        }

        if (map.TryGetValue(planId, out var tier))
        {
            return tier;
        }

        logger.LogWarning("Marketplace plan '{PlanId}' is not in Marketplace:PlanToProductTier; defaulting to tier 0.", planId);
        return 0;
    }
}
