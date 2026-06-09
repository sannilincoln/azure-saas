using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Server-side Azure Marketplace fulfillment endpoints. Called by the Sign-up/Admin web app's
/// landing page (with the signed-in user's bearer token). The actual server-to-server calls to
/// Microsoft run here, where the publisher service-principal credentials live.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MarketplaceController(IMarketplaceFulfillmentService fulfillment) : ControllerBase
{
    /// <summary>Resolve a marketplace token into a durable subscription (persisted immediately).</summary>
    [HttpPost("resolve")]
    public async Task<ActionResult<ResolvedSubscriptionDto>> Resolve([FromBody] ResolveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest("Marketplace token is required.");
        }

        // The signed-in caller is the buyer; their home tenant becomes the subscription's
        // customer-tenant key (used by customer self-service filtering).
        Guid? customerTenantId = Guid.TryParse(User.GetTenantId(), out var tid) ? tid : null;

        var resolved = await fulfillment.ResolveAsync(request.Token, customerTenantId);
        return Ok(resolved);
    }

    /// <summary>Activate a resolved subscription (starts billing) and link it to the onboarded tenant.</summary>
    [HttpPost("{subscriptionId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid subscriptionId, [FromBody] ActivateRequest request)
    {
        await fulfillment.ActivateAsync(subscriptionId, request.TenantId);
        return NoContent();
    }
}
