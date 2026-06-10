using Microsoft.AspNetCore.Mvc;
using Saas.SignupAdministration.Web.Authorization;
using Saas.SignupAdministration.Web.Services;

namespace Saas.SignupAdministration.Web.Areas.Publisher.Controllers;

/// <summary>
/// Publisher/owner console: manage ALL subscriptions across customers. Gated by the
/// <see cref="MarketplaceConsolePolicies.PublisherConsole"/> policy (publisher home tenant, and an
/// owner app-role if configured). The Admin API independently re-checks the publisher tenant on
/// every call, so a customer who somehow reached these routes still gets 403 server-side.
/// </summary>
[Area("Publisher")]
[Authorize(Policy = MarketplaceConsolePolicies.PublisherConsole)]
[Route("[area]/Subscriptions")]
public class SubscriptionsController(IMarketplaceAdminClient marketplaceClient) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await marketplaceClient.GetAllSubscriptionsAsync();
        return View(subscriptions);
    }

    [HttpPost("{id:guid}/refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(Guid id)
    {
        await marketplaceClient.RefreshSubscriptionAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OverrideStatus(Guid id, string status)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            await marketplaceClient.OverrideSubscriptionStatusAsync(id, status);
        }

        return RedirectToAction(nameof(Index));
    }
}
