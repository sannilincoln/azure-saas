using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// The publisher-editable notification settings. <see cref="Enabled"/> is the master switch; each flow
/// has its own toggle (<see cref="SignupAlert"/>/<see cref="Welcome"/>/<see cref="Invite"/>/
/// <see cref="RoleChange"/>) so they can be turned on independently. <see cref="ToEmails"/> is the
/// recipient list for the publisher signup alert. The sending identity (From) is the shared mailbox in
/// config, not editable here. (<see cref="FromEmail"/>/<see cref="CopyToCustomer"/> are legacy SMTP
/// fields, retired with the SMTP sender.)
/// </summary>
public record MarketplaceNotificationSettings(
    bool Enabled,
    string? FromEmail,
    string? ToEmails,
    bool CopyToCustomer,
    bool SignupAlert = false,
    bool Welcome = false,
    bool Invite = false,
    bool RoleChange = false);

/// <summary>
/// Reads/writes the runtime-editable notification settings (enabled flag, from/to addresses, copy
/// flag) so a publisher can change them from the console without a redeploy. Persisted as key/value
/// rows in the marketplace store's existing <c>ApplicationConfiguration</c> table (no new schema).
/// On read, any unset value falls back to the <c>Marketplace:Notifications:*</c> App Config defaults.
/// </summary>
public interface IMarketplaceNotificationSettingsStore
{
    Task<MarketplaceNotificationSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MarketplaceNotificationSettings settings, CancellationToken cancellationToken = default);
}

public class MarketplaceNotificationSettingsStore(
    SaasKitContext db,
    IOptions<MarketplaceOptions> marketplaceOptions) : IMarketplaceNotificationSettingsStore
{
    // Kept <= 50 chars to fit ApplicationConfiguration.Name (nvarchar(50)).
    private const string KeyEnabled = "NotifEnabled";
    private const string KeyFromEmail = "NotifFromEmail";
    private const string KeyToEmails = "NotifToEmails";
    private const string KeyCopyToCustomer = "NotifCopyToCustomer";
    private const string KeySignupAlert = "NotifSignupAlert";
    private const string KeyWelcome = "NotifWelcome";
    private const string KeyInvite = "NotifInvite";
    private const string KeyRoleChange = "NotifRoleChange";
    private const string Description = "Marketplace notification setting";

    public async Task<MarketplaceNotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ApplicationConfiguration
            .Where(c => c.Name.StartsWith("Notif"))
            .ToDictionaryAsync(c => c.Name, c => c.Value, cancellationToken);

        var fallback = marketplaceOptions.Value.Notifications;

        return new MarketplaceNotificationSettings(
            Enabled: GetBool(rows, KeyEnabled, fallback?.Enabled ?? false),
            FromEmail: GetString(rows, KeyFromEmail, fallback?.FromEmail),
            ToEmails: GetString(rows, KeyToEmails, fallback?.ToEmails),
            CopyToCustomer: GetBool(rows, KeyCopyToCustomer, fallback?.CopyToCustomer ?? false),
            // Per-flow toggles default off until a publisher turns them on in the console.
            SignupAlert: GetBool(rows, KeySignupAlert, false),
            Welcome: GetBool(rows, KeyWelcome, false),
            Invite: GetBool(rows, KeyInvite, false),
            RoleChange: GetBool(rows, KeyRoleChange, false));
    }

    public async Task SaveAsync(MarketplaceNotificationSettings settings, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(KeyEnabled, settings.Enabled.ToString(), cancellationToken);
        await UpsertAsync(KeyToEmails, settings.ToEmails ?? string.Empty, cancellationToken);
        await UpsertAsync(KeySignupAlert, settings.SignupAlert.ToString(), cancellationToken);
        await UpsertAsync(KeyWelcome, settings.Welcome.ToString(), cancellationToken);
        await UpsertAsync(KeyInvite, settings.Invite.ToString(), cancellationToken);
        await UpsertAsync(KeyRoleChange, settings.RoleChange.ToString(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertAsync(string name, string value, CancellationToken cancellationToken)
    {
        var row = await db.ApplicationConfiguration.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
        if (row is null)
        {
            db.ApplicationConfiguration.Add(new ApplicationConfiguration { Name = name, Value = value, Description = Description });
        }
        else
        {
            row.Value = value;
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, string> rows, string key, string? fallback) =>
        rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> rows, string key, bool fallback) =>
        rows.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
}
