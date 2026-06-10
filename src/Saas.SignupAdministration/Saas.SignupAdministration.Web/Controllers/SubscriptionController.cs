using Microsoft.AspNetCore.Mvc;
using Saas.SignupAdministration.Web.Authorization;
using Saas.SignupAdministration.Web.Services;

namespace Saas.SignupAdministration.Web.Controllers;

/// <summary>
/// Customer self-service: a signed-in customer views their own marketplace subscription(s). The
/// Admin API filters strictly by the caller's tenant (tid), so this surface can never enumerate
/// another customer's subscriptions even though the policy is just "authenticated".
/// </summary>
[Authorize(Policy = MarketplaceConsolePolicies.CustomerSelfService)]
[Route("subscription")]
public class SubscriptionController(IMarketplaceAdminClient marketplaceClient) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await marketplaceClient.GetMySubscriptionsAsync();
        return View(subscriptions);
    }
}
