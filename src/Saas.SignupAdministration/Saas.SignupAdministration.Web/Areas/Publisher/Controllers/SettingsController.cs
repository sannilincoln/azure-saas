using Microsoft.AspNetCore.Mvc;
using Saas.SignupAdministration.Web.Authorization;
using Saas.SignupAdministration.Web.Services;

namespace Saas.SignupAdministration.Web.Areas.Publisher.Controllers;

/// <summary>
/// Publisher notification settings — the from/to addresses (and enable/copy toggles) used when a
/// tenant signs up. Owner-gated via <see cref="MarketplaceConsolePolicies.PublisherConsole"/>; the
/// Admin API re-checks the publisher tenant server-side. SMTP transport is config-only and not shown.
/// </summary>
[Area("Publisher")]
[Authorize(Policy = MarketplaceConsolePolicies.PublisherConsole)]
[Route("[area]/Settings")]
public class SettingsController(IMarketplaceAdminClient marketplaceClient) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var settings = await marketplaceClient.GetNotificationSettingsAsync();
        return View(settings);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(NotificationSettings settings)
    {
        await marketplaceClient.UpdateNotificationSettingsAsync(settings);
        TempData["SettingsSaved"] = true;
        return RedirectToAction(nameof(Index));
    }
}
