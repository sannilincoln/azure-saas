using System.Linq;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Marketplace.SaaS.Accelerator.Services.Models;
using Marketplace.SaaS.Accelerator.Services.WebHook;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Marketplace.SaaS.Models;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Xunit;
using WebhookAction = Marketplace.SaaS.Accelerator.Services.WebHook.WebhookAction;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class MarketplaceWebhookHandlerTests
{
    private static IFulfillmentApiService FulfillmentWithValidOperation()
    {
        var fulfillment = Substitute.For<IFulfillmentApiService>();
        fulfillment.GetOperationStatusResultAsync(Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns(new OperationResult());
        return fulfillment;
    }

    [Fact]
    public async Task ChangeQuantity_ValidOperation_UpdatesQuantityAndAcknowledges()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        var opId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, quantity: 3);

        var fulfillment = FulfillmentWithValidOperation();
        var handler = new MarketplaceWebhookHandler(fulfillment, marketplace, tenants, NullLogger<MarketplaceWebhookHandler>.Instance);

        await handler.ChangeQuantityAsync(new WebhookPayload
        {
            Action = WebhookAction.ChangeQuantity,
            SubscriptionId = subId,
            OperationId = opId,
            Quantity = 9,
        });

        var updated = marketplace.Subscriptions.Single(s => s.AmpsubscriptionId == subId);
        Assert.Equal(9, updated.Ampquantity);

        // Acknowledged the operation back to Microsoft as Success (the ~10s ACK).
        await fulfillment.Received(1).PatchOperationStatusResultAsync(
            subId, opId, UpdateOperationStatusEnum.Success);
    }

    [Fact]
    public async Task ChangeQuantity_InvalidOperation_DoesNotUpdateOrAcknowledge()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, quantity: 3);

        var fulfillment = Substitute.For<IFulfillmentApiService>();
        fulfillment.GetOperationStatusResultAsync(Arg.Any<Guid>(), Arg.Any<Guid>())
            .Returns((OperationResult)null!); // operation can't be validated

        var handler = new MarketplaceWebhookHandler(fulfillment, marketplace, tenants, NullLogger<MarketplaceWebhookHandler>.Instance);

        await handler.ChangeQuantityAsync(new WebhookPayload
        {
            Action = WebhookAction.ChangeQuantity,
            SubscriptionId = subId,
            OperationId = Guid.NewGuid(),
            Quantity = 9,
        });

        var unchanged = marketplace.Subscriptions.Single(s => s.AmpsubscriptionId == subId);
        Assert.Equal(3, unchanged.Ampquantity);

        await fulfillment.DidNotReceive().PatchOperationStatusResultAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateOperationStatusEnum>());
    }

    [Fact]
    public async Task UnknownSubscription_IsIgnored()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();

        var fulfillment = FulfillmentWithValidOperation();
        var handler = new MarketplaceWebhookHandler(fulfillment, marketplace, tenants, NullLogger<MarketplaceWebhookHandler>.Instance);

        // No subscription seeded — must no-op (and never acknowledge).
        await handler.ChangeQuantityAsync(new WebhookPayload
        {
            Action = WebhookAction.ChangeQuantity,
            SubscriptionId = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            Quantity = 9,
        });

        await fulfillment.DidNotReceive().PatchOperationStatusResultAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateOperationStatusEnum>());
    }

    [Fact]
    public async Task Suspend_FlipsStatusOnBothStores()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, status: "Subscribed");
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId, subscriptionStatus: "Subscribed");

        var handler = new MarketplaceWebhookHandler(
            Substitute.For<IFulfillmentApiService>(), marketplace, tenants, NullLogger<MarketplaceWebhookHandler>.Instance);

        await handler.SuspendedAsync(new WebhookPayload { Action = WebhookAction.Suspend, SubscriptionId = subId });

        var sub = marketplace.Subscriptions.Single(s => s.AmpsubscriptionId == subId);
        var tenant = tenants.Tenants.Single(t => t.Id == tenantId);
        Assert.Equal("Suspended", sub.SubscriptionStatus);
        Assert.False(sub.IsActive);
        Assert.Equal("Suspended", tenant.SubscriptionStatus);
    }

    [Fact]
    public async Task Unsubscribe_FlipsStatusOnBothStores()
    {
        using var marketplace = MarketplaceTestHelpers.NewMarketplaceDb();
        using var tenants = MarketplaceTestHelpers.NewTenantsDb();
        var subId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        MarketplaceTestHelpers.SeedSubscription(marketplace, subId, status: "Subscribed");
        MarketplaceTestHelpers.SeedTenant(tenants, tenantId, subscriptionId: subId, subscriptionStatus: "Subscribed");

        var handler = new MarketplaceWebhookHandler(
            Substitute.For<IFulfillmentApiService>(), marketplace, tenants, NullLogger<MarketplaceWebhookHandler>.Instance);

        await handler.UnsubscribedAsync(new WebhookPayload { Action = WebhookAction.Unsubscribe, SubscriptionId = subId });

        var sub = marketplace.Subscriptions.Single(s => s.AmpsubscriptionId == subId);
        var tenant = tenants.Tenants.Single(t => t.Id == tenantId);
        Assert.Equal("Unsubscribed", sub.SubscriptionStatus);
        Assert.Equal("Unsubscribed", tenant.SubscriptionStatus);
    }
}
