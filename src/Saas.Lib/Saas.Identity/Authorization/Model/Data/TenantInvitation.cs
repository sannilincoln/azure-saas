using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Saas.Identity.Authorization.Model.Data;

/// <summary>
/// A pending grant of access to a tenant, keyed by email. Created by a tenant admin before the invitee
/// has ever signed in (so there is no user id yet, and — under Workforce multitenant — the invitee
/// lives in the customer's own directory and cannot be looked up via the publisher's Graph). On the
/// invitee's first sign-in it is matched by email and turned into real permission grants for their
/// object id (JIT binding). Email is the matching key only; identity upgrades to the immutable user id
/// at bind time.
/// </summary>
[Table("TenantInvitations")]
public record TenantInvitation
{
    [Key]
    [Column("Id")]
    public int Id { get; init; }

    [Required]
    [Column("TenantId")]
    public Guid TenantId { get; init; }

    /// <summary>Invitee email, stored normalized (trimmed, lower-invariant) for case-insensitive match.</summary>
    [Required]
    [Column("Email")]
    public string Email { get; init; } = null!;

    /// <summary>Permission strings to grant on bind, ';'-delimited (mirrors the existing invite shape).</summary>
    [Required]
    [Column("PermissionsCsv")]
    public string PermissionsCsv { get; init; } = "";

    [Required]
    [Column("Status")]
    public TenantInvitationStatus Status { get; set; } = TenantInvitationStatus.Pending;

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; init; }
}
