using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Emails the publisher when a marketplace subscription activates (a new tenant signs up).
/// Optional and best-effort: nothing is sent unless <c>Marketplace:Notifications:Enabled</c> is true
/// and an SMTP host + from + recipient are configured. All failures are caught and logged so a mail
/// problem can never break activation/onboarding — by the time we notify, billing has already started.
/// </summary>
public interface IMarketplaceNotificationService
{
    Task NotifySubscriptionActivatedAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default);
}

/// <summary>Data for the "a tenant signed up" notification.</summary>
public record SubscriptionActivatedNotice(
    Guid SubscriptionId,
    string? SubscriptionName,
    string? OfferId,
    string? PlanId,
    int Quantity,
    string TenantName,
    string TenantRoute,
    string? CustomerEmail);

public class SmtpMarketplaceNotificationService(
    IMarketplaceNotificationSettingsStore settingsStore,
    IOptions<MarketplaceOptions> marketplaceOptions,
    ILogger<SmtpMarketplaceNotificationService> logger) : IMarketplaceNotificationService
{
    public async Task NotifySubscriptionActivatedAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default)
    {
        // SMTP transport (host/port/credentials) is infra + secret => config only. The enabled flag
        // and from/to addresses are publisher-editable from the console => read from the store.
        var transport = marketplaceOptions.Value.Notifications;
        var settings = await settingsStore.GetAsync(cancellationToken);

        if (!settings.Enabled)
        {
            return;
        }

        if (transport is null || string.IsNullOrWhiteSpace(transport.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.FromEmail)
            || string.IsNullOrWhiteSpace(settings.ToEmails))
        {
            logger.LogWarning(
                "Marketplace notifications are enabled but SMTP host (config) or From/To (console) are not all set; " +
                "skipping the activation email for subscription {SubscriptionId}.", notice.SubscriptionId);
            return;
        }

        try
        {
            using var message = new MailMessage { From = new MailAddress(settings.FromEmail), IsBodyHtml = true };
            foreach (var to in SplitAddresses(settings.ToEmails))
            {
                message.To.Add(to);
            }

            if (settings.CopyToCustomer && !string.IsNullOrWhiteSpace(notice.CustomerEmail))
            {
                message.CC.Add(notice.CustomerEmail);
            }

            message.Subject = $"New subscription: {notice.TenantName} ({notice.PlanId})";
            message.Body = BuildBody(notice);

            using var smtp = new SmtpClient(transport.SmtpHost, transport.SmtpPort)
            {
                EnableSsl = transport.SmtpUseSsl,
                UseDefaultCredentials = false,
            };
            if (!string.IsNullOrWhiteSpace(transport.SmtpUsername))
            {
                smtp.Credentials = new NetworkCredential(transport.SmtpUsername, transport.SmtpPassword);
            }

            await smtp.SendMailAsync(message, cancellationToken);

            logger.LogInformation(
                "Sent marketplace activation notification for subscription {SubscriptionId} to {Recipients}.",
                notice.SubscriptionId, settings.ToEmails);
        }
        catch (Exception ex)
        {
            // Never let a mail failure surface to onboarding — record it and move on.
            logger.LogError(ex,
                "Failed to send marketplace activation notification for subscription {SubscriptionId}.",
                notice.SubscriptionId);
        }
    }

    private static IEnumerable<string> SplitAddresses(string addresses) =>
        addresses.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string BuildBody(SubscriptionActivatedNotice notice) =>
        $"""
        <h2>New subscription activated</h2>
        <p>A customer has completed onboarding and their subscription is now active.</p>
        <table cellpadding="4" style="border-collapse:collapse">
          <tr><td><b>Tenant</b></td><td>{notice.TenantName}</td></tr>
          <tr><td><b>Route</b></td><td>{notice.TenantRoute}</td></tr>
          <tr><td><b>Offer</b></td><td>{notice.OfferId}</td></tr>
          <tr><td><b>Plan</b></td><td>{notice.PlanId}</td></tr>
          <tr><td><b>Seats (quantity)</b></td><td>{notice.Quantity}</td></tr>
          <tr><td><b>Subscription id</b></td><td>{notice.SubscriptionId}</td></tr>
          <tr><td><b>Subscription name</b></td><td>{notice.SubscriptionName}</td></tr>
        </table>
        """;
}
