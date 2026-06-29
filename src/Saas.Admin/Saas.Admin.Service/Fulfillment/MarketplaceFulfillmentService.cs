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
    IEmailSender emailSender,
    IProductProvisioningService provisioning,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<MarketplaceFulfillmentService> logger) : IMarketplaceFulfillmentService
{
    private const string StatusPendingFulfillmentStart = "PendingFulfillmentStart";
    private const string StatusSubscribed = "Subscribed";

    public async Task<ResolvedSubscriptionDto> ResolveAsync(string marketplaceToken)
    {
        var resolved = await fulfillmentApi.ResolveAsync(marketplaceToken)
            ?? throw new InvalidOperationException("Marketplace token could not be resolved (expired, already used, or invalid).");

        // The customer-tenant key is the subscription's BENEFICIARY tenant (the org whose users sign in
        // to the product). It is NOT on the resolve result, so fetch the full subscription. This is what
        // runtime tenant resolution matches the inbound token 'tid' against — so it must be the real
        // customer directory, never the interactive caller's tenant (this call is app-only anyway).
        Guid? customerTenantId = null;
        try
        {
            var full = await fulfillmentApi.GetSubscriptionByIdAsync(resolved.SubscriptionId);
            var beneficiaryTenantId = full?.Beneficiary?.TenantId ?? Guid.Empty;
            if (beneficiaryTenantId != Guid.Empty)
            {
                customerTenantId = beneficiaryTenantId;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read beneficiary tenant for subscription {SubscriptionId}; customer-tenant key left unset.", resolved.SubscriptionId);
        }

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

        // Record the beneficiary (customer) tenant so runtime tenant resolution and customer
        // self-service can key off it. Don't overwrite a previously captured value with null on a
        // re-resolve (e.g. if the beneficiary lookup transiently failed).
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
        // Resolve must have run first; the subscription row is the durable source of truth.
        _ = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId)
            ?? throw new InvalidOperationException($"Subscription {subscriptionId} not found; resolve must run before activate.");

        var tenant = await tenantsDb.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found when linking subscription {subscriptionId}.");

        // Link the subscription and queue provisioning for the background worker. The slow work
        // (Microsoft activation, CREATE DATABASE + migrate + seed) runs out of band so the onboarding
        // HTTP request returns immediately instead of blocking ~60s and timing out (502). DatabaseName
        // stays null until provisioning succeeds, so tenant resolution fails closed (graceful 403) until
        // the tenant is actually ready to serve traffic.
        tenant.SubscriptionId = subscriptionId;
        tenant.SubscriptionStatus = StatusPendingFulfillmentStart;
        tenant.ProvisioningStatus = ProvisioningStatuses.Provisioning;
        await tenantsDb.SaveChangesAsync();

        logger.LogInformation("Linked subscription {SubscriptionId} to tenant {TenantId}; queued for background activation + provisioning.",
            subscriptionId, tenantId);
    }

    public async Task ProcessPendingProvisioningAsync(CancellationToken cancellationToken = default)
    {
        var pending = await tenantsDb.Tenants
            .Where(t => t.ProvisioningStatus == ProvisioningStatuses.Provisioning)
            .OrderBy(t => t.CreatedTime)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var tenant in pending)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProvisionAndActivateAsync(tenant, cancellationToken);
        }
    }

    /// <summary>
    /// Performs the deferred, slow part of onboarding for a single queued tenant: provision the
    /// per-tenant database, then activate the subscription with Microsoft (billing starts only after
    /// provisioning succeeds), then mark the tenant ready. Idempotent: <c>ProvisionAsync</c> is
    /// CREATE-IF-NOT-EXISTS + EF migrate (re-runnable), and a re-resolve/re-activate is harmless.
    /// </summary>
    private async Task ProvisionAndActivateAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        if (tenant.SubscriptionId is not Guid subscriptionId)
        {
            logger.LogWarning("Tenant {TenantId} is marked Provisioning but has no linked subscription; marking Failed.", tenant.Id);
            tenant.ProvisioningStatus = ProvisioningStatuses.Failed;
            await tenantsDb.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var subscription = await marketplaceDb.Subscriptions
                .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId, cancellationToken)
                ?? throw new InvalidOperationException($"Subscription {subscriptionId} not found for tenant {tenant.Id}.");

            // 1) Provision the per-tenant database (product-agnostic: prefix is config, not a literal).
            //    When no prefix is configured this product doesn't use database-per-tenant, so skip it.
            var databaseNamePrefix = marketplaceOptions.Value.TenantDatabaseNamePrefix;
            string? databaseName = null;
            if (!string.IsNullOrWhiteSpace(databaseNamePrefix))
            {
                databaseName = $"{databaseNamePrefix}-{tenant.Route}";
                await provisioning.ProvisionAsync(tenant.Id, databaseName);
            }

            // 2) Activate with Microsoft (starts billing) only AFTER provisioning succeeded, so we never
            //    charge for a tenant whose database failed to come up.
            await fulfillmentApi.ActivateSubscriptionAsync(subscriptionId, subscription.AmpplanId);
            subscription.SubscriptionStatus = StatusSubscribed;
            subscription.ModifyDate = DateTime.UtcNow;
            await marketplaceDb.SaveChangesAsync(cancellationToken);

            // 3) Mark the tenant ready. Setting DatabaseName is the signal that runtime resolution may
            //    now serve this tenant.
            tenant.DatabaseName = databaseName;
            tenant.SubscriptionStatus = StatusSubscribed;
            tenant.ProvisioningStatus = ProvisioningStatuses.Provisioned;
            await tenantsDb.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Provisioned + activated tenant {TenantId} (subscription {SubscriptionId}, db {DatabaseName}).",
                tenant.Id, subscriptionId, databaseName);

            // 4) Best-effort emails — must never fail the (already committed) onboarding. The email sender
            //    is itself best-effort, but guard the whole block too in case notice construction throws.
            try
            {
                var notice = new SubscriptionActivatedNotice(
                    SubscriptionId: subscriptionId,
                    SubscriptionName: subscription.Name,
                    OfferId: subscription.AmpOfferId,
                    PlanId: subscription.AmpplanId,
                    Quantity: subscription.Ampquantity,
                    TenantName: tenant.Name,
                    TenantRoute: tenant.Route,
                    CustomerEmail: tenant.CreatorEmail);

                await emailSender.NotifySubscriptionActivatedAsync(notice, cancellationToken); // Flow 1: publisher alert
                await emailSender.NotifyTenantWelcomeAsync(notice, cancellationToken);          // Flow 2: welcome the tenant
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Activation emails failed for tenant {TenantId} (non-fatal).", tenant.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning/activation failed for tenant {TenantId}; marking Failed.", tenant.Id);
            tenant.ProvisioningStatus = ProvisioningStatuses.Failed;
            try
            {
                await tenantsDb.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Could not persist Failed provisioning status for tenant {TenantId}.", tenant.Id);
            }
        }
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
