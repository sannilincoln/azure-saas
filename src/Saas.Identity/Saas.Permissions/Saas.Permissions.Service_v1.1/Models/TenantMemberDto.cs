namespace Saas.Permissions.Service.Models;

/// <summary>
/// A tenant member with the roles they currently hold (derived from their <c>Role:</c> permission tags).
/// Returned to the product BFF to render the team-management screen — identity comes from the durable
/// TenantMembers record (captured at JIT bind), so no cross-tenant Graph call is needed.
/// </summary>
public record TenantMemberDto
{
    public Guid UserId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string[] Roles { get; init; } = [];
}
