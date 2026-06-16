using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saas.Admin.Service.Data;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Resolves a tenant's student quota — the plan-derived ceiling the product (Edulynk) enforces by
/// counting student rows. The platform owns the plan/tier → ceiling map and publishes the number; it
/// does not count students itself (it has no knowledge of the product schema).
/// </summary>
public interface ITenantQuotaService
{
    /// <summary>Returns the tenant's quota, or null if the tenant does not exist.</summary>
    Task<TenantQuota?> GetQuotaAsync(Guid tenantId);
}

/// <summary>
/// A tenant's resolved quota. <see cref="MaxStudents"/> is a hard cap; <c>0</c> means <b>no students
/// may be registered</b> (fail-closed — the tenant's tier is not mapped in Marketplace:TierMaxStudents).
/// There is no "unlimited" sentinel: an uncapped plan must be mapped to an explicit high ceiling.
/// </summary>
public record TenantQuota(
    Guid TenantId,
    string? PlanId,
    int ProductTierId,
    int MaxStudents,
    string? SubscriptionStatus);

public class TenantQuotaService(
    TenantsContext tenantsDb,
    SaasKitContext marketplaceDb,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<TenantQuotaService> logger) : ITenantQuotaService
{
    public async Task<TenantQuota?> GetQuotaAsync(Guid tenantId)
    {
        var tenant = await tenantsDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant is null)
        {
            return null;
        }

        var maxStudents = ResolveMaxStudents(tenant.ProductTierId);

        string? planId = null;
        if (tenant.SubscriptionId is Guid subscriptionId)
        {
            planId = await marketplaceDb.Subscriptions
                .AsNoTracking()
                .Where(s => s.AmpsubscriptionId == subscriptionId)
                .Select(s => s.AmpplanId)
                .FirstOrDefaultAsync();
        }

        return new TenantQuota(
            TenantId: tenant.Id,
            PlanId: planId,
            ProductTierId: tenant.ProductTierId,
            MaxStudents: maxStudents,
            SubscriptionStatus: tenant.SubscriptionStatus);
    }

    /// <summary>
    /// Tier ceiling from config. Fail-closed: an unmapped tier / absent map ⇒ 0, i.e. no students may
    /// be registered. Logged as a warning because this actively blocks a (likely paying) tenant and
    /// almost always indicates a missing Marketplace:TierMaxStudents entry.
    /// </summary>
    private int ResolveMaxStudents(int productTierId)
    {
        var map = marketplaceOptions.Value.TierMaxStudents;
        if (map is not null && map.TryGetValue(productTierId, out var ceiling))
        {
            return ceiling;
        }

        logger.LogWarning(
            "ProductTier {ProductTierId} is not in Marketplace:TierMaxStudents; student quota is 0 "
            + "(fail-closed — students cannot be registered until this tier is mapped).",
            productTierId);
        return 0;
    }
}
