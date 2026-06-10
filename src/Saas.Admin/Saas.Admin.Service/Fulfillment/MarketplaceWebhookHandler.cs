using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Marketplace.SaaS.Accelerator.Services.WebHook;
using Microsoft.EntityFrameworkCore;
using Microsoft.Marketplace.SaaS.Models;
using Saas.Admin.Service.Data;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Handles Azure Marketplace connection-webhook lifecycle events: keeps the marketplace store and
/// the denormalized tenant status in sync, and acknowledges the actionable operations
/// (ChangePlan / ChangeQuantity / Reinstate) back to Microsoft via the Operations API.
///
/// Idempotency is best-effort: ChangePlan/ChangeQuantity are validated against the Operations API
/// before acting, and the ack PATCH is wrapped so a retry that hits an already-completed operation
/// doesn't surface as an error. Suspend/Unsubscribe/Renew are notify-only (no ack required).
/// </summary>
public class MarketplaceWebhookHandler(
    IFulfillmentApiService fulfillment,
    SaasKitContext marketplaceDb,
    TenantsContext tenantsDb,
    ILogger<MarketplaceWebhookHandler> logger) : IWebhookHandler
{
    private const string StatusSubscribed = "Subscribed";
    private const string StatusSuspended = "Suspended";
    private const string StatusUnsubscribed = "Unsubscribed";

    public async Task ChangePlanAsync(WebhookPayload payload)
    {
        var subscription = await GetSubscriptionAsync(payload.SubscriptionId);
        if (subscription is null || !await ValidateOperationAsync(payload))
        {
            return;
        }

        subscription.AmpplanId = payload.PlanId;
        subscription.ModifyDate = DateTime.UtcNow;
        await marketplaceDb.SaveChangesAsync();

        await AcknowledgeAsync(payload, success: true);
        logger.LogInformation("ChangePlan applied for subscription {SubscriptionId} -> plan {PlanId}.", payload.SubscriptionId, payload.PlanId);
    }

    public async Task ChangeQuantityAsync(WebhookPayload payload)
    {
        var subscription = await GetSubscriptionAsync(payload.SubscriptionId);
        if (subscription is null || !await ValidateOperationAsync(payload))
        {
            return;
        }

        subscription.Ampquantity = payload.Quantity;
        subscription.ModifyDate = DateTime.UtcNow;
        await marketplaceDb.SaveChangesAsync();

        await AcknowledgeAsync(payload, success: true);
        logger.LogInformation("ChangeQuantity applied for subscription {SubscriptionId} -> qty {Quantity}.", payload.SubscriptionId, payload.Quantity);
    }

    public async Task ReinstatedAsync(WebhookPayload payload)
    {
        await SetStatusAsync(payload.SubscriptionId, StatusSubscribed);
        await AcknowledgeAsync(payload, success: true);
    }

    public Task RenewedAsync()
    {
        // Notify-only; the subscription stays Subscribed.
        logger.LogInformation("Subscription renewed.");
        return Task.CompletedTask;
    }

    public Task SuspendedAsync(WebhookPayload payload) => SetStatusAsync(payload.SubscriptionId, StatusSuspended);

    public Task UnsubscribedAsync(WebhookPayload payload) => SetStatusAsync(payload.SubscriptionId, StatusUnsubscribed);

    public Task UnknownActionAsync(WebhookPayload payload)
    {
        logger.LogWarning("Unknown marketplace webhook action {Action} for subscription {SubscriptionId}; ignored.",
            payload.Action, payload.SubscriptionId);
        return Task.CompletedTask;
    }

    private async Task<Marketplace.SaaS.Accelerator.DataAccess.Entities.Subscriptions?> GetSubscriptionAsync(Guid ampSubscriptionId)
    {
        var subscription = await marketplaceDb.Subscriptions
            .FirstOrDefaultAsync(s => s.AmpsubscriptionId == ampSubscriptionId);

        if (subscription is null)
        {
            logger.LogWarning("Webhook references unknown subscription {SubscriptionId}; ignored.", ampSubscriptionId);
        }

        return subscription;
    }

    /// <summary>Validate the operation is genuine by calling back the Operations API.</summary>
    private async Task<bool> ValidateOperationAsync(WebhookPayload payload)
    {
        var operation = await fulfillment.GetOperationStatusResultAsync(payload.SubscriptionId, payload.OperationId);
        if (operation is null)
        {
            logger.LogWarning("Could not validate operation {OperationId} for subscription {SubscriptionId}; ignored.",
                payload.OperationId, payload.SubscriptionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ack an actionable operation. ChangePlan/ChangeQuantity must be patched within ~10s or
    /// Microsoft auto-succeeds them; wrapped so a retry hitting an already-acked operation is benign.
    /// </summary>
    private async Task AcknowledgeAsync(WebhookPayload payload, bool success)
    {
        try
        {
            await fulfillment.PatchOperationStatusResultAsync(
                payload.SubscriptionId,
                payload.OperationId,
                success ? UpdateOperationStatusEnum.Success : UpdateOperationStatusEnum.Failure);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Acknowledging operation {OperationId} failed (it may already be acknowledged).", payload.OperationId);
        }
    }

    private async Task SetStatusAsync(Guid ampSubscriptionId, string status)
    {
        var subscription = await GetSubscriptionAsync(ampSubscriptionId);
        if (subscription is not null)
        {
            subscription.SubscriptionStatus = status;
            subscription.IsActive = status == StatusSubscribed;
            subscription.ModifyDate = DateTime.UtcNow;
            await marketplaceDb.SaveChangesAsync();
        }

        // Keep the denormalized tenant status in sync so access gating reacts immediately.
        var tenant = await tenantsDb.Tenants.FirstOrDefaultAsync(t => t.SubscriptionId == ampSubscriptionId);
        if (tenant is not null)
        {
            tenant.SubscriptionStatus = status;
            await tenantsDb.SaveChangesAsync();
        }

        logger.LogInformation("Subscription {SubscriptionId} status -> {Status}.", ampSubscriptionId, status);
    }
}
