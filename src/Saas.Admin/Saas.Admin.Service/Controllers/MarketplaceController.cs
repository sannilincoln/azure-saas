using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saas.Admin.Service.Authorization;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Server-side Azure Marketplace fulfillment endpoints. Called service-to-service by the Sign-up/Admin
/// web app's onboarding flow with an <b>app-only</b> token bearing the <c>Service.Access</c> app role —
/// not the customer's user token. This keeps the customer's tenant off the Admin API consent surface
/// (the web app runs the interactive sign-in for them, requesting only user-consentable Graph scopes),
/// which is essential because customers are typically non-admins in their tenant. The publisher
/// service-principal credentials used for the Microsoft-facing calls live here.
/// </summary>
[Authorize(Policy = ServiceAccessPolicy.Name)]
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

        // The buyer's tenant (the subscription's customer-tenant key, used for runtime tenant
        // resolution + self-service filtering) is taken from the resolved subscription's beneficiary,
        // not from an interactive sign-in — this call is app-only.
        var resolved = await fulfillment.ResolveAsync(request.Token);
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
