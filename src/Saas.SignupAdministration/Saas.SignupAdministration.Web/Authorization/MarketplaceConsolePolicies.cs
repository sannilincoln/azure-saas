namespace Saas.SignupAdministration.Web.Authorization;

/// <summary>
/// Authorization policy names for the in-app marketplace consoles. These are the UX/defense-in-depth
/// boundary in the web app; the authoritative boundary is enforced server-side in the Admin API.
/// </summary>
public static class MarketplaceConsolePolicies
{
    /// <summary>Publisher/owner console — caller must belong to the publisher tenant (and owner role, if configured).</summary>
    public const string PublisherConsole = "PublisherConsole";

    /// <summary>Customer self-service — any authenticated customer; the Admin API filters to their own tid.</summary>
    public const string CustomerSelfService = "CustomerSelfService";
}
