using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Microsoft.EntityFrameworkCore;
using Saas.Admin.Service.Data;
using Saas.Permissions.Client;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Real seat guard, registered only when the marketplace feature is configured. Compares the
/// tenant's current assigned-user count against the purchased seat quantity on its linked
/// Marketplace subscription.
/// </summary>
public class MarketplaceSeatService(
    TenantsContext tenantsDb,
    SaasKitContext marketplaceDb,
    IPermissionsServiceClient permissions,
    ILogger<MarketplaceSeatService> logger) : IMarketplaceSeatService
{
    public async Task EnsureSeatAvailableAsync(Guid tenantId)
    {
        var tenant = await tenantsDb.Tenants.FindAsync(tenantId);

        // Not a marketplace-provisioned tenant → no seat ceiling to enforce.
        if (tenant?.SubscriptionId is not Guid subscriptionId)
        {
            return;
        }

        var subscription = await marketplaceDb.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == subscriptionId);

        if (subscription is null)
        {
            logger.LogWarning(
                "Tenant {TenantId} is linked to subscription {SubscriptionId} but no marketplace row was found; skipping seat check.",
                tenantId, subscriptionId);
            return;
        }

        var seats = subscription.Ampquantity;

        // Quantity unknown/unset (0 or negative) → don't block; treat as unlimited.
        if (seats <= 0)
        {
            return;
        }

        var activeUsers = (await permissions.GetTenantUsersAsync(tenantId))?.Count ?? 0;

        if (activeUsers + 1 > seats)
        {
            logger.LogInformation(
                "Seat limit reached for tenant {TenantId}: {ActiveUsers}/{Seats} assigned; add-user rejected.",
                tenantId, activeUsers, seats);
            throw new SeatLimitExceededException(seats, activeUsers);
        }
    }
}
