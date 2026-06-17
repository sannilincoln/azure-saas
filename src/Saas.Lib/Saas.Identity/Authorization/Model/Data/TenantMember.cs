using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Saas.Identity.Authorization.Model.Data;

/// <summary>
/// A confirmed member of a tenant — the durable "who belongs" record, created at JIT bind (or for the
/// tenant creator at tenant creation). Captures the identity (email + display name) from the user's own
/// token at first sign-in so member lists never need a cross-tenant Graph call. The actual permission
/// grants live in <see cref="SaasPermission"/>; this is identity only.
/// </summary>
[Table("TenantMembers")]
public record TenantMember
{
    [Key]
    [Column("Id")]
    public int Id { get; init; }

    [Required]
    [Column("TenantId")]
    public Guid TenantId { get; init; }

    [Required]
    [Column("UserId")]
    public Guid UserId { get; init; }

    [Column("Email")]
    public string? Email { get; set; }

    [Column("DisplayName")]
    public string? DisplayName { get; set; }
}
