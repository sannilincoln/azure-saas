using Microsoft.AspNetCore.Mvc;
using Saas.SignupAdministration.Web.Services;

namespace Saas.SignupAdministration.Web.Controllers;

/// <summary>
/// Azure Marketplace landing page. After a purchase, Microsoft redirects the buyer here with a
/// single-use token. We exchange it server-side (via the Admin API, which holds the publisher
/// credentials), seed the onboarding wizard from the purchased subscription, and hand off to the
/// existing flow. The subscription is persisted server-side on resolve, so onboarding can proceed
/// even if the session is later lost.
/// </summary>
[Authorize]
[Route("marketplace")]
public class MarketplaceController(
    IMarketplaceAdminClient marketplaceClient,
    OnboardingWorkflowService onboardingWorkflow,
    ILogger<MarketplaceController> logger) : Controller
{
    [HttpGet("landing")]
    public async Task<IActionResult> Landing([FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest("Missing Azure Marketplace token.");
        }

        var resolved = await marketplaceClient.ResolveAsync(token);

        onboardingWorkflow.OnboardingWorkflowItem.OrganizationName = resolved.SubscriptionName ?? string.Empty;
        onboardingWorkflow.OnboardingWorkflowItem.SubscriptionId = resolved.SubscriptionId;
        onboardingWorkflow.PersistToSession();

        logger.LogInformation("Resolved marketplace subscription {SubscriptionId}; starting onboarding.", resolved.SubscriptionId);

        return RedirectToAction(SR.OrganizationNameAction, SR.OnboardingWorkflowController);
    }
}
