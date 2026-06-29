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
    IGraphMailClient mailClient,
    IOptions<NotificationBrandingOptions> branding,
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
        return Ok(new NotificationSettingsDto(
            s.Enabled, s.ToEmails, s.SignupAlert, s.Welcome, s.Invite, s.RoleChange));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] NotificationSettingsDto request)
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        await store.SaveAsync(new MarketplaceNotificationSettings(
            Enabled: request.Enabled, FromEmail: null, ToEmails: request.ToEmails, CopyToCustomer: false,
            SignupAlert: request.SignupAlert, Welcome: request.Welcome,
            Invite: request.Invite, RoleChange: request.RoleChange));
        return NoContent();
    }

    /// <summary>
    /// Send a one-off test email to confirm the Graph transport (managed identity + shared mailbox) is
    /// working — independent of the toggles, so the publisher can validate setup before turning flows on.
    /// Surfaces the transport error verbatim (e.g. a Graph 403) so misconfiguration is visible.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromQuery] string to)
    {
        if (!IsPublisher())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return BadRequest("A 'to' address is required.");
        }

        var product = branding.Value.ProductName;
        try
        {
            await mailClient.SendAsync(new EmailMessage(
                To: new[] { to },
                Subject: $"Test email from {product}",
                HtmlBody: $"<p>This is a test email from {product}. If you received it, Graph <code>sendMail</code> is working.</p>"));
            return Ok($"Test email sent to {to}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test email to {Recipient} failed.", to);
            return StatusCode(StatusCodes.Status502BadGateway, $"Send failed: {ex.Message}");
        }
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

public record NotificationSettingsDto(
    bool Enabled,
    string? ToEmails,
    bool SignupAlert,
    bool Welcome,
    bool Invite,
    bool RoleChange);
