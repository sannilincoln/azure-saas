namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Guards the per-seat limit of a tenant's Azure Marketplace subscription. The buyer pays for a
/// quantity of seats on Azure; we must not let a tenant assign more users than it bought.
/// Enforced in the add-user path (Admin API invite) — the server-side boundary where the seat
/// count and the active-user count both live.
/// </summary>
public interface IMarketplaceSeatService
{
    /// <summary>
    /// Throws <see cref="SeatLimitExceededException"/> when adding one more user would exceed the
    /// tenant's purchased seat quantity. No-ops for tenants that aren't linked to a Marketplace
    /// subscription (legacy / non-marketplace) or when the marketplace feature isn't configured.
    /// </summary>
    Task EnsureSeatAvailableAsync(Guid tenantId);
}

/// <summary>Raised when an add-user would push the tenant past its purchased seat count.</summary>
public class SeatLimitExceededException(int seats, int activeUsers)
    : Exception($"This subscription includes {seats} seat(s) and already has {activeUsers} assigned user(s). "
        + "Remove a user or increase the seat quantity on Azure before adding another.")
{
    public int Seats { get; } = seats;
    public int ActiveUsers { get; } = activeUsers;
}
