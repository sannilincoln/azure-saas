namespace Saas.Permissions.Service.Interfaces;

/// <summary>
/// JIT tenant membership: invite by email (no directory lookup), then bind the invitation to a real
/// user on their first sign-in. Replaces the Graph-based <c>AddUserPermissionsToTenantByEmail</c> for
/// Workforce-multitenant tenants, whose users live in the customer's own directory.
/// </summary>
public interface ITenantMembershipService
{
    /// <summary>Records a pending invitation for <paramref name="email"/> with the given permission strings.</summary>
    Task CreateInvitationAsync(Guid tenantId, string email, IEnumerable<string> permissions);

    /// <summary>
    /// Called on a user's first sign-in. Matches a pending invitation by email and turns it into real
    /// permission grants for the user's object id, recording their identity. Idempotent.
    /// </summary>
    Task<TenantBindResult> BindMemberAsync(Guid tenantId, Guid userId, string email, string? displayName);
}

/// <summary>Outcome of a bind attempt.</summary>
public enum TenantBindResult
{
    /// <summary>A pending invitation was matched and the user is now a member with permissions.</summary>
    Bound,

    /// <summary>The user was already a member of this tenant; nothing changed.</summary>
    AlreadyMember,

    /// <summary>No pending invitation matched this email; the user has no access.</summary>
    NoInvitation,
}
