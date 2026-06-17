using Saas.Identity.Authorization.Model.Data;
using Saas.Permissions.Service.Data.Context;
using Saas.Permissions.Service.Interfaces;

namespace Saas.Permissions.Service.Services;

public class TenantMembershipService(
    SaasPermissionsContext permissionsContext,
    ILogger<TenantMembershipService> logger) : ITenantMembershipService
{
    private const char PermissionDelimiter = ';';

    public async Task CreateInvitationAsync(Guid tenantId, string email, IEnumerable<string> permissions)
    {
        permissionsContext.TenantInvitations.Add(new TenantInvitation
        {
            TenantId = tenantId,
            Email = Normalize(email),
            PermissionsCsv = string.Join(PermissionDelimiter, permissions),
            Status = TenantInvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });

        await permissionsContext.SaveChangesAsync();
    }

    public async Task<TenantBindResult> BindMemberAsync(Guid tenantId, Guid userId, string email, string? displayName)
    {
        var alreadyMember = await permissionsContext.TenantMembers
            .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId);
        if (alreadyMember)
        {
            return TenantBindResult.AlreadyMember;
        }

        var normalized = Normalize(email);
        var invitation = await permissionsContext.TenantInvitations
            .FirstOrDefaultAsync(i =>
                i.TenantId == tenantId &&
                i.Email == normalized &&
                i.Status == TenantInvitationStatus.Pending);

        if (invitation is null)
        {
            logger.LogInformation(
                "No pending invitation for tenant {TenantId} matched the signed-in user; access denied.", tenantId);
            return TenantBindResult.NoInvitation;
        }

        var permissions = invitation.PermissionsCsv
            .Split(PermissionDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        permissionsContext.SaasPermissions.Add(new SaasPermission
        {
            TenantId = tenantId,
            UserId = userId,
            TenantPermissions = permissions.Select(p => new TenantPermission { PermissionStr = p }).ToArray(),
        });

        permissionsContext.TenantMembers.Add(new TenantMember
        {
            TenantId = tenantId,
            UserId = userId,
            Email = normalized,
            DisplayName = displayName,
        });

        invitation.Status = TenantInvitationStatus.Bound;

        await permissionsContext.SaveChangesAsync();
        return TenantBindResult.Bound;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
