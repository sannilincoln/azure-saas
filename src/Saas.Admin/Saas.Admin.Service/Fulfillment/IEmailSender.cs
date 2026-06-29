namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Sends the marketplace's transactional emails (publisher alerts + customer-facing notices) via
/// Microsoft Graph. Each flow is independently gated by the publisher-editable settings and is
/// best-effort: a mail failure is logged, never thrown, so it can't break the operation that triggered
/// it. (The invite flow is the exception — it reports delivery so the caller can warn the admin.)
/// </summary>
public interface IEmailSender
{
    /// <summary>Flow 1 — alert the publisher that a tenant signed up (to the configured recipients).</summary>
    Task NotifySubscriptionActivatedAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default);

    /// <summary>Flow 2 — welcome the newly onboarded tenant (to the customer's email).</summary>
    Task NotifyTenantWelcomeAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flow 3 — tell an invited user they've been granted access. Returns whether the email was
    /// delivered so the caller can warn the admin ("invited, but the email failed") — never throws.
    /// </summary>
    Task<bool> NotifyUserInvitedAsync(UserInvitedNotice notice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flow 4 — tell a member their role changed. The recipient email is resolved by the caller (the
    /// role endpoint) and may be null when the member's identity isn't captured yet, in which case
    /// nothing is sent.
    /// </summary>
    Task NotifyRoleChangedAsync(RoleChangedNotice notice, CancellationToken cancellationToken = default);
}

/// <summary>Data for the "a tenant signed up" event — drives the publisher alert (Flow 1) and the
/// customer welcome (Flow 2).</summary>
public record SubscriptionActivatedNotice(
    Guid SubscriptionId,
    string? SubscriptionName,
    string? OfferId,
    string? PlanId,
    int Quantity,
    string TenantName,
    string TenantRoute,
    string? CustomerEmail);

/// <summary>Data for the "you've been invited" email (Flow 3).</summary>
public record UserInvitedNotice(string Email, string Role, string TenantName, Guid TenantId);

/// <summary>Data for the "your role changed" email (Flow 4). Email may be null (unresolved member).</summary>
public record RoleChangedNotice(string? Email, string Role, string TenantName);

public class GraphEmailSender(
    IGraphMailClient mail,
    IMarketplaceNotificationSettingsStore settingsStore,
    Microsoft.Extensions.Options.IOptions<NotificationBrandingOptions> brandingOptions,
    ILogger<GraphEmailSender> logger) : IEmailSender
{
    private readonly NotificationBrandingOptions _branding = brandingOptions.Value;

    public async Task NotifySubscriptionActivatedAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.Enabled || !settings.SignupAlert || string.IsNullOrWhiteSpace(settings.ToEmails))
        {
            return;
        }

        var message = new EmailMessage(
            To: SplitAddresses(settings.ToEmails),
            Subject: $"New subscription: {notice.TenantName} ({notice.PlanId})",
            HtmlBody: BuildPublisherBody(notice));

        await TrySendAsync(message, $"publisher activation alert for subscription {notice.SubscriptionId}", cancellationToken);
    }

    public async Task NotifyTenantWelcomeAsync(SubscriptionActivatedNotice notice, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.Enabled || !settings.Welcome || string.IsNullOrWhiteSpace(notice.CustomerEmail))
        {
            return;
        }

        var message = new EmailMessage(
            To: new[] { notice.CustomerEmail! },
            Subject: $"Welcome to {_branding.ProductName}, {notice.TenantName}",
            HtmlBody: BuildWelcomeBody(notice));

        await TrySendAsync(message, $"tenant welcome for subscription {notice.SubscriptionId}", cancellationToken);
    }

    public async Task<bool> NotifyUserInvitedAsync(UserInvitedNotice notice, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.Enabled || !settings.Invite || string.IsNullOrWhiteSpace(notice.Email))
        {
            return false;
        }

        var message = new EmailMessage(
            To: new[] { notice.Email },
            Subject: $"You've been invited to {notice.TenantName}",
            HtmlBody: BuildInviteBody(notice));

        return await TrySendAsync(message, $"invitation email to {notice.Email}", cancellationToken);
    }

    public async Task NotifyRoleChangedAsync(RoleChangedNotice notice, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.Enabled || !settings.RoleChange || string.IsNullOrWhiteSpace(notice.Email))
        {
            return;
        }

        var message = new EmailMessage(
            To: new[] { notice.Email! },
            Subject: $"Your role in {notice.TenantName} changed",
            HtmlBody: BuildRoleChangedBody(notice));

        await TrySendAsync(message, $"role-change email to {notice.Email}", cancellationToken);
    }

    /// <summary>Best-effort send: a transport failure is logged, never propagated. Returns delivery.</summary>
    private async Task<bool> TrySendAsync(EmailMessage message, string description, CancellationToken cancellationToken)
    {
        try
        {
            await mail.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {Description}.", description);
            return false;
        }
    }

    private static IReadOnlyList<string> SplitAddresses(string addresses) =>
        addresses.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string BuildPublisherBody(SubscriptionActivatedNotice notice) =>
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

    private string BuildWelcomeBody(SubscriptionActivatedNotice notice) =>
        $"""
        <h2>Welcome to {_branding.ProductName}, {notice.TenantName}!</h2>
        <p>Your subscription is active and your workspace is ready.</p>
        {SignInSection()}
        """;

    private string BuildInviteBody(UserInvitedNotice notice) =>
        $"""
        <h2>You've been invited to {notice.TenantName} on {_branding.ProductName}</h2>
        <p>You've been granted the <b>{notice.Role}</b> role. Sign in with your work account to get
        started — your access is activated on first sign-in.</p>
        {SignInSection()}
        """;

    private string BuildRoleChangedBody(RoleChangedNotice notice) =>
        $"""
        <h2>Your role in {notice.TenantName} changed</h2>
        <p>You've been granted the <b>{notice.Role}</b> role on {_branding.ProductName}. The change takes
        effect the next time you sign in.</p>
        {SignInSection()}
        """;

    /// <summary>The "sign in here" call-to-action, pointing at the product front-end (when configured).</summary>
    private string SignInSection() =>
        string.IsNullOrWhiteSpace(_branding.AppBaseUrl)
            ? string.Empty
            : $"""<p><a href="{_branding.AppBaseUrl}">Sign in to {_branding.ProductName}</a></p>""";
}
