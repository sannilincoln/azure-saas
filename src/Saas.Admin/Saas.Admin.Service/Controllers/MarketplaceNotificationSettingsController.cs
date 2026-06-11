using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Read/update the publisher-editable marketplace notification settings (enabled flag + from/to
/// addresses + copy-to-customer). Publisher-only — the caller's home tenant must equal the configured
/// publisher tenant. SMTP transport is intentionally not exposed here (config/Key Vault only).
/// </summary>
[Authorize]
[ApiController]
[Route("api/marketplace/notifications/settings")]
public class MarketplaceNotificationSettingsController(
    IMarketplaceNotificationSettingsStore store,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<MarketplaceNotificationSettingsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationSettingsDto>> Get()
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        var s = await store.GetAsync();
        return Ok(new NotificationSettingsDto(s.Enabled, s.FromEmail, s.ToEmails, s.CopyToCustomer));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] NotificationSettingsDto request)
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        await store.SaveAsync(new MarketplaceNotificationSettings(
            request.Enabled, request.FromEmail, request.ToEmails, request.CopyToCustomer));
        return NoContent();
    }

    private Guid? CallerTenantId => Guid.TryParse(User.GetTenantId(), out var tid) ? tid : null;

    private bool IsPublisher()
    {
        if (!Guid.TryParse(marketplaceOptions.Value.PublisherTenantId, out var publisherTenantId))
        {
            logger.LogWarning("Publisher tenant id is not configured; denying publisher-scoped request.");
            return false;
        }

        return CallerTenantId == publisherTenantId;
    }
}

public record NotificationSettingsDto(bool Enabled, string? FromEmail, string? ToEmails, bool CopyToCustomer);
