using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Marketplace subscription read/manage endpoints backing the in-app consoles. This is the real,
/// server-side authorization boundary (the web app's policies are defense-in-depth / UX):
///   - Publisher endpoints require the caller's home tenant to equal the configured publisher
///     tenant. A customer-tenant token gets 403 here even if it reached the route.
///   - The customer endpoint never trusts a client-supplied tenant id — it filters strictly by the
///     caller's own <c>tid</c> claim, so one customer can never enumerate another's subscriptions.
/// </summary>
[Authorize]
[ApiController]
[Route("api/marketplace/subscriptions")]
public class MarketplaceSubscriptionsController(
    ISubscriptionQueryService subscriptions,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<MarketplaceSubscriptionsController> logger) : ControllerBase
{
    /// <summary>All subscriptions — publisher console only.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> GetAll()
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        return Ok(await subscriptions.GetAllAsync());
    }

    /// <summary>The caller's own subscriptions — customer self-service.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> GetMine()
    {
        if (CallerTenantId is not Guid tenantId)
        {
            return Forbid();
        }

        return Ok(await subscriptions.GetByCustomerTenantAsync(tenantId));
    }

    /// <summary>Re-pull live state from Microsoft — publisher console only.</summary>
    [HttpPost("{subscriptionId:guid}/refresh")]
    public async Task<ActionResult<SubscriptionDto>> Refresh(Guid subscriptionId)
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        var result = await subscriptions.RefreshFromMarketplaceAsync(subscriptionId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Administratively override stored status — publisher console only.</summary>
    [HttpPost("{subscriptionId:guid}/status")]
    public async Task<ActionResult<SubscriptionDto>> OverrideStatus(Guid subscriptionId, [FromBody] OverrideStatusRequest request)
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest("Status is required.");
        }

        var result = await subscriptions.OverrideStatusAsync(subscriptionId, request.Status);
        return result is null ? NotFound() : Ok(result);
    }

    private Guid? CallerTenantId => Guid.TryParse(User.GetTenantId(), out var tid) ? tid : null;

    private bool IsPublisher()
    {
        if (!Guid.TryParse(marketplaceOptions.Value.PublisherTenantId, out var publisherTenantId))
        {
            logger.LogWarning("Publisher tenant id is not configured; denying publisher-scoped request.");
            return false;
        }

        var isPublisher = CallerTenantId == publisherTenantId;
        if (!isPublisher)
        {
            logger.LogInformation("Publisher-scoped request denied for tenant {CallerTenant}.", CallerTenantId);
        }

        return isPublisher;
    }
}
