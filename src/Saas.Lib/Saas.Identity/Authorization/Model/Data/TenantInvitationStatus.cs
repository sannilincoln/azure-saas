namespace Saas.Identity.Authorization.Model.Data;

/// <summary>
/// Lifecycle of a <see cref="TenantInvitation"/>. Pending = created by an admin, awaiting the invitee's
/// first sign-in. Bound = matched to a real user and turned into permission grants. Revoked = withdrawn.
/// </summary>
public enum TenantInvitationStatus
{
    Pending,
    Bound,
    Revoked,
}
