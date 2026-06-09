using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Saas.Identity.Authorization.Model.Claim;
using Saas.Identity.Authorization.Model.Kind;
using Saas.Identity.Claims;
using Saas.Permissions.Client;

namespace Saas.Admin.Service.Claims;

/// <summary>
/// Microsoft Entra External ID replacement for the B2C "permissions" claim injection.
///
/// Under Azure AD B2C, an IEF custom policy called the Permissions API at token issuance
/// and embedded the user's SaaS permission strings (e.g. <c>TenantPermissions.{guid}.Admin</c>)
/// into the token as repeated <c>permissions</c> claims. The <c>SaasAuthorize</c> handlers
/// (<see cref="Saas.Identity.Authorization.Handler.SaasPermissionAuthorizationHandlerBase{TReq,TKind}"/>)
/// read those claims to make authorization decisions.
///
/// Entra External ID issues no such claim, so rather than embedding permissions in the token
/// we resolve them server-side, per request, from the Permissions API keyed on the caller's
/// directory object id. The object id is read from <see cref="ClaimTypes.NameIdentifier"/>,
/// which <see cref="Saas.Identity.Claims.NameIdentifierClaimsTransformation"/> maps from the
/// External ID <c>oid</c> claim — so this transformation MUST be registered after it.
///
/// Resolved permissions are cached briefly per user so authorization stays close to live
/// (near-immediate revocation) without a Permissions API round-trip on every request. The
/// call reuses the same x-api-key <see cref="IPermissionsServiceClient"/> the Admin API
/// already uses for its other Permissions API calls.
/// </summary>
public class PermissionsClaimsTransformation(
    IPermissionsServiceClient permissionsClient,
    IMemoryCache cache,
    ILogger<PermissionsClaimsTransformation> logger) : IClaimsTransformation
{
    // Short enough that a revoked permission stops working almost immediately, long enough
    // that a burst of requests in a single page load doesn't hammer the Permissions API.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private static readonly string PermissionClaimType = SaasPermissionClaim<TenantPermissionKind>.PermissionClaimsIdentifier;

    private readonly IPermissionsServiceClient _permissionsClient = permissionsClient;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger _logger = logger;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
        {
            return principal;
        }

        // Map the External ID 'oid' into NameIdentifier ourselves. ASP.NET Core resolves a
        // SINGLE IClaimsTransformation from DI (the last one registered), so we cannot rely on
        // NameIdentifierClaimsTransformation also being registered — registering both would
        // silence one of them. This transformation owns both responsibilities.
        NameIdentifierClaimsTransformation.TryMapObjectIdToNameIdentifier(identity);

        // IClaimsTransformation can run more than once per request; don't duplicate claims.
        if (identity.HasClaim(claim => claim.Type == PermissionClaimType))
        {
            return principal;
        }

        // NameIdentifier carries the directory object id GUID (mapped from 'oid' above).
        // Without it we can't look up permissions, so add none and let authorization deny.
        if (!Guid.TryParse(identity.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return principal;
        }

        try
        {
            foreach (var permission in await GetPermissionsAsync(userId))
            {
                identity.AddClaim(new Claim(PermissionClaimType, permission));
            }
        }
        catch (Exception ex)
        {
            // Fail closed: on a Permissions API failure we add no permission claims, so the
            // SaasAuthorize handlers deny. Logged so the outage is diagnosable.
            _logger.LogError(ex,
                "Failed to resolve SaaS permissions for user {UserId}; proceeding with no permission claims.",
                userId);
        }

        return principal;
    }

    private async Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId) =>
        (await _cache.GetOrCreateAsync($"saas-permissions:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;

            var response = await _permissionsClient.PermissionsAsync(new ClaimsRequest { ObjectId = userId });
            return (response.Permissions ?? new List<string>()).ToArray();
        }))!;
}
