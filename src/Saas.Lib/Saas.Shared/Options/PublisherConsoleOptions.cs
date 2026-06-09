namespace Saas.Shared.Options;

/// <summary>
/// Secret-free configuration for the in-app publisher/owner console authorization boundary, bound
/// in the Sign-up/Admin web app. Only carries the publisher's Microsoft Entra tenant id (and an
/// optional owner app-role) — never the publisher service-principal secret, which stays in the
/// Admin API alone. Bound from the same "Marketplace" section; absent keys leave the console
/// inaccessible (deny-by-default), which is the correct behaviour in unconfigured environments.
/// </summary>
public record PublisherConsoleOptions
{
    public const string SectionName = MarketplaceOptions.SectionName;

    /// <summary>
    /// The publisher's home Entra tenant. A signed-in user whose <c>tid</c> matches this tenant is
    /// publisher staff and may reach the publisher console; everyone else is a customer.
    /// </summary>
    public string? PublisherTenantId { get; init; }

    /// <summary>
    /// Optional app-role (the "roles" claim value) required *within* the publisher tenant to reach
    /// the console. When null, membership in the publisher tenant is sufficient. Lets product #1
    /// ship without app-role setup while leaving room to tighten to true owners later.
    /// </summary>
    public string? OwnerRole { get; init; }
}
