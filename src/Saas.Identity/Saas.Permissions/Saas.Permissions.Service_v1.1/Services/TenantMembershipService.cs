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
        var normalized = Normalize(email);

        var existingMember = await permissionsContext.TenantMembers
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId);
        if (existingMember is not null)
        {
            // Already a member (e.g. the tenant creator, recorded before first sign-in). Capture their
            // identity from the token if we don't have it yet, but don't re-grant permissions.
            var changed = false;
            if (string.IsNullOrWhiteSpace(existingMember.Email) && !string.IsNullOrWhiteSpace(normalized))
            {
                existingMember.Email = normalized;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(existingMember.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
            {
                existingMember.DisplayName = displayName;
                changed = true;
            }
            if (changed)
            {
                await permissionsContext.SaveChangesAsync();
            }

            return TenantBindResult.AlreadyMember;
        }
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
