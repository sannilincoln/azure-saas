using Microsoft.AspNetCore.Mvc;
using Saas.SignupAdministration.Web.Services;
using Saas.SignupAdministration.Web.Services.StateMachine;

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
    IOnboardingAdminClient onboardingClient,
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

        var resolved = await onboardingClient.ResolveAsync(token);

        // Start a CLEAN onboarding. A stale session from a prior attempt would otherwise leave the
        // workflow state machine in a non-initial state (e.g. TenantDeploymentRequested), so the first
        // wizard POST throws "no transition". Reset both the state machine and the item, then seed the
        // marketplace fields on the fresh item.
        onboardingWorkflow.OnboardingWorkflowState = new OnboardingWorkflowState();
        onboardingWorkflow.OnboardingWorkflowItem = new OnboardingWorkflowItem
        {
            OrganizationName = resolved.SubscriptionName ?? string.Empty,
            SubscriptionId = resolved.SubscriptionId,
            // Pre-set the tier from the purchased plan so the wizard can skip the service-plan step
            // (the buyer already chose a plan on Azure; they must not be able to override it here).
            ProductId = resolved.ProductTierId,
        };
        onboardingWorkflow.PersistToSession();

        logger.LogInformation("Resolved marketplace subscription {SubscriptionId} (tier {ProductTierId}); starting onboarding.",
            resolved.SubscriptionId, resolved.ProductTierId);

        return RedirectToAction(SR.OrganizationNameAction, SR.OnboardingWorkflowController);
    }
}
