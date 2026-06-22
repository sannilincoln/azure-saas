namespace Saas.Identity.Authorization.Model.Kind;

/// <summary>
/// The product's tenant-level <em>roles</em> — a vocabulary layered on top of the CRUD
/// <see cref="TenantPermissionKind"/> permissions. A role is a named bundle that, when granted to a user
/// on a tenant, expands to two permission strings stored against that tenant:
/// <list type="bullet">
///   <item>the mapped CRUD permission (e.g. <c>Admin</c> / <c>Read</c>) — what the API authorizes on;</item>
///   <item>a <c>Role:&lt;name&gt;</c> tag — what the product UI reads back to drive role-based display.</item>
/// </list>
/// Storing the role as a <c>Role:</c>-prefixed permission string (rather than an Entra app role) is what
/// lets marketplace customers — who cannot be assigned app roles in their own directory — still get roles:
/// roles become tenant-managed data, surfaced through the same permissions→claims path. The prefix keeps
/// role tags unambiguously distinct from CRUD permissions, and the CRUD-enum claim parser simply ignores
/// them (they never parse to a <see cref="TenantPermissionKind"/>). This is the Edulynk role set; a
/// different product swaps this list out.
/// </summary>
public static class TenantRole
{
    /// <summary>Prefix marking a permission string as a role tag, e.g. <c>Role:Super-Admin</c>.</summary>
    public const string ClaimPrefix = "Role:";

    public const string SuperAdmin = "Super-Admin";
    public const string Admin = "Admin";
    public const string Bursar = "Bursar";
    public const string BillingAccountant = "Billing-Accountant";
    public const string ReceivableAccountant = "Receivable-Accountant";
    public const string Student = "student";
    public const string BoardMember = "board-members";
    public const string Reviewer = "reviewer";

    /// <summary>The role granted to the tenant creator at onboarding (full control of the tenant).</summary>
    public const string Bootstrap = SuperAdmin;

    /// <summary>All roles a Super-Admin may assign (and that the product UI may surface).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SuperAdmin, Admin, Bursar, BillingAccountant, ReceivableAccountant, Student, BoardMember, Reviewer,
    };

    /// <summary>Roles that carry tenant administration (manage users, invite, assign roles).</summary>
    private static readonly HashSet<string> AdminRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        SuperAdmin, Admin,
    };

    public static bool IsKnown(string? role) =>
        role is not null && All.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    /// <summary>The role-tag permission string for a role, e.g. <c>Role:Bursar</c>.</summary>
    public static string ToClaimTag(string role) => $"{ClaimPrefix}{role}";

    /// <summary>
    /// Given a role tag (e.g. <c>Role:Bursar</c>) returns the bare role name (<c>Bursar</c>); returns
    /// <c>null</c> for a permission string that is not a role tag. Used to read roles back from claims.
    /// </summary>
    public static string? FromClaimTag(string? permissionStr) =>
        permissionStr is not null && permissionStr.StartsWith(ClaimPrefix, StringComparison.Ordinal)
            ? permissionStr[ClaimPrefix.Length..]
            : null;

    /// <summary>
    /// Expands a role to the permission strings to persist for a user on a tenant: the backing CRUD
    /// permission (for API authorization) plus the <c>Role:</c> tag (for product-UI role display).
    /// </summary>
    public static string[] ToPermissionStrings(string role)
    {
        if (!IsKnown(role))
        {
            throw new ArgumentException($"Unknown tenant role '{role}'.", nameof(role));
        }

        // Normalize to the canonical casing/spelling in All so the stored tag is stable.
        var canonical = All.First(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

        var crud = AdminRoles.Contains(canonical)
            ? TenantPermissionKind.Admin
            : TenantPermissionKind.Read;

        return new[] { crud.ToString(), ToClaimTag(canonical) };
    }
}
