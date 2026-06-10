using Marketplace.SaaS.Accelerator.Services.Utilities;
using Marketplace.SaaS.Accelerator.Services.WebHook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Azure Marketplace connection webhook. Microsoft POSTs subscription lifecycle notifications here.
/// This endpoint must be publicly reachable and up 24/7, and must NOT depend on an interactive
/// session — which is exactly why it lives in the (stateless, always-on) Admin API rather than the
/// cookie/session-bound Sign-up web app.
///
/// Security is the inbound Microsoft JWT (validated for aud/tid/azp), not the Admin API's normal
/// Entra bearer scheme, hence [AllowAnonymous].
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/marketplace/webhook")]
public class MarketplaceWebhookController(
    ValidateJwtToken jwtValidator,
    IWebhookHandler handler,
    ILogger<MarketplaceWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] WebhookPayload payload)
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Marketplace webhook rejected: missing bearer token.");
            return Unauthorized();
        }

        var token = authorization["Bearer ".Length..].Trim();
        try
        {
            await jwtValidator.ValidateTokenAsync(token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Marketplace webhook rejected: JWT validation failed.");
            return Unauthorized();
        }

        logger.LogInformation("Marketplace webhook {Action} for subscription {SubscriptionId} (operation {OperationId}).",
            payload.Action, payload.SubscriptionId, payload.OperationId);

        switch (payload.Action)
        {
            case WebhookAction.ChangePlan:
                await handler.ChangePlanAsync(payload);
                break;
            case WebhookAction.ChangeQuantity:
                await handler.ChangeQuantityAsync(payload);
                break;
            case WebhookAction.Suspend:
                await handler.SuspendedAsync(payload);
                break;
            case WebhookAction.Unsubscribe:
                await handler.UnsubscribedAsync(payload);
                break;
            case WebhookAction.Reinstate:
                await handler.ReinstatedAsync(payload);
                break;
            case WebhookAction.Renew:
                await handler.RenewedAsync();
                break;
            default:
                await handler.UnknownActionAsync(payload);
                break;
        }

        return Ok();
    }
}
