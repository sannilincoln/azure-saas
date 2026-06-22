using Saas.Identity.Authorization.Attribute;
using Saas.Identity.Authorization.Model.Claim;
using Saas.Identity.Authorization.Model.Data;
using Saas.Identity.Authorization.Model.Kind;
using Saas.Identity.Authorization.Requirement;
using Saas.Admin.Service.Authorization;
using Saas.Admin.Service.Fulfillment;
using Saas.Admin.Service.Membership;
using Saas.Permissions.Client;
using Microsoft.Identity.Web;
using System.Net.Mime;
using System.Security.Claims;

namespace Saas.Admin.Service.Controllers;

[Route("api/[controller]")]
[Authorize]
[ApiController]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly IPermissionsServiceClient _permissionsServiceClient;
    private readonly IMarketplaceSeatService _seatService;
    private readonly ITenantMembershipClient _membershipClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger _logger;

    public TenantsController(
        ITenantService tenantService,
        IPermissionsServiceClient permissionService,
        IMarketplaceSeatService seatService,
        ITenantMembershipClient membershipClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<TenantsController> logger)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tenantService = tenantService;
        _permissionsServiceClient = permissionService;
        _seatService = seatService;
        _membershipClient = membershipClient;
    }

    /// <summary>
    /// Get all tenants in the system
    /// </summary>
    /// <returns>List of all tenants</returns>
    /// <remarks>
    /// <para><b>Requires:</b> admin.tenant.read</para>
    /// <para>This call will return all the tenants in the system.</para>
    /// </remarks>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Read)]
    public async Task<ActionResult<IEnumerable<TenantDTO>>> GetAllTenants()
    {
        try
        {
            _logger.LogDebug("{UserName} is requesting all tenants.", User?.Identity?.Name);

            List<TenantDTO> allTenants = (await _tenantService.GetAllTenantsAsync()).ToList();

            _logger.LogDebug("Returning {ReturnCount} tenants", allTenants.Count);
            return Ok(allTenants);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem retrieving all tenants");
            throw;
        }
    }

    /// <summary>
    /// Get a tenant by tenant ID
    /// </summary>
    /// <param name="tenantId">Guid representing the tenant</param>
    /// <returns>Information about the tenant</returns>
    /// <remarks>
    /// <para><b>Requires:</b> admin.tenant.read  or  {tenantID}.tenant.read</para>
    /// <para>Will return details of a single tenant, if user has access.</para>
    /// </remarks>
    [HttpGet("{tenantId}")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Read, routingRestrictionKeyName: "tenantId")]
    public async Task<ActionResult<TenantDTO>> GetTenant(Guid tenantId)
    {
        _logger.LogDebug("{User} requested tenant with ID {TenantID}", User?.Identity?.Name, tenantId);
        try
        {
            TenantDTO tenant = await _tenantService.GetTenantAsync(tenantId);
            _logger.LogDebug("Found {TenantName} with {TenantID}", tenant.Name, tenantId);

            return Ok(tenant);
        }
        catch (ItemNotFoundExcepton)
        {
            _logger.LogDebug("Was not able to find tenant with ID {TeantntID}", tenantId);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem retrieving tenant with ID {TeantntID}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Add a new tenant
    /// </summary>
    /// <param name="tenantRequest"></param>
    /// <returns></returns>
    /// <remarks>
    /// <para><b>Requires:</b> the <c>Service.Access</c> app role (app-only token).</para>
    /// <para>Called service-to-service by the Sign-up/Admin web app during onboarding. The web app runs
    /// the interactive sign-in (so customer tenants only ever consent to user-consentable Graph scopes,
    /// never to this Admin API) and then creates the tenant app-only. The user to make tenant admin is
    /// therefore passed explicitly in <see cref="NewTenantRequest.CreatorObjectId"/>, not read from a
    /// user token.</para>
    /// </remarks>
    [HttpPost()]
    [Produces(MediaTypeNames.Application.Json)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(TenantDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]

    [Authorize(Policy = ServiceAccessPolicy.Name)]
    public async Task<ActionResult<TenantDTO>> PostTenant(NewTenantRequest tenantRequest)
    {
        try
        {
            _logger.LogInformation("Creating a new tenant: {NewTenantName} for {OwnerID} (creator {CreatorObjectId})", tenantRequest.Name, tenantRequest.CreatorEmail, tenantRequest.CreatorObjectId);

            if (tenantRequest.CreatorObjectId == Guid.Empty)
            {
                return BadRequest("CreatorObjectId is required (the user to make admin of the new tenant).");
            }

            TenantDTO tenant = await _tenantService.AddTenantAsync(tenantRequest, tenantRequest.CreatorObjectId);

            _logger.LogInformation("Created a new tenant {NewTenantName} with URL {NewTenantRoute}, and ID {NewTenantID}", tenant.Name, tenant.Route, tenant.Id);
            
            return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.Id }, tenant);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest((ex.InnerException ?? ex).Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem creating tenant with ID {TenantName}", tenantRequest.Name);
            throw;
        }
    }

    /// <summary>
    /// Update an existing tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="tenantDTO"></param>
    /// <returns></returns>
    /// <remarks>
    /// <para><b>Requires:</b> admin.tenant.write  or  {tenantID}.tenant.write</para>
    /// </remarks>
    [HttpPut("{tenantId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Update, "tenantId")]
    public async Task<IActionResult> PutTenant(Guid tenantId, TenantDTO tenantDTO)
    {
        _logger.LogDebug("Updating tenant {TenantID} by {User}", tenantId, User?.Identity?.Name);
        if (tenantId != tenantDTO.Id)
        {
            _logger.LogInformation("Requested Id {TenantID} did not match request data {DTOTenantID}", tenantId, tenantDTO.Id);
            return BadRequest();
        }
        try
        {
            await _tenantService.UpdateTenantAsync(tenantDTO);
            _logger.LogInformation("Updated tenant {TenantName} with id {TenantID}", tenantDTO.Name, tenantDTO.Id);
        }
        catch (ItemNotFoundExcepton ex)
        {
            _logger.LogWarning(ex, "Unable to find tenant {TenantID}", tenantId);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem updating tenant {TenantID}", tenantId);
            throw;
        }

        return NoContent();
    }

    /// <summary>
    /// Deletes a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    [HttpDelete("{tenantId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Delete, "tenantId")]
    public async Task<IActionResult> DeleteTenant(Guid tenantId)
    {
        try
        {
            _logger.LogDebug("Deleting tenant {TenantID} by {User}", tenantId, User?.Identity?.Name);
            await _tenantService.DeleteTenantAsync(tenantId);
        }
        catch (ItemNotFoundExcepton ex)
        {
            _logger.LogWarning(ex, "Unable to find tenant {TenantID}", tenantId);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to delete tenant {TeanantID}", tenantId);
            throw;
        }

        return NoContent();
    }

    /// <summary>
    /// Get public tenant info by route
    /// </summary>
    /// <param name="route">String route of tenant</param>
    /// <returns>Information about the tenant</returns>
    /// <remarks>
    /// <para><b>Requires:</b>Authorize</para>
    /// <para>Will return public details of a single tenant</para>
    /// </remarks>
    [HttpGet("tenantinfo/{route}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Read)]
    public async Task<ActionResult<TenantInfoDTO>> GetTenantInfoByRoute(string route)
    {
        _logger.LogDebug("{User} requested tenant for route {Route}", User?.Identity?.Name, route);

        try
        {
            var tenantPermissions = _httpContextAccessor?.HttpContext?.User.Claims
                .Where(c => c.Type == SaasPermissionClaim<TenantPermissionKind>.PermissionClaimsIdentifier)
                .Select(claim => new SaasPermissionClaim<TenantPermissionKind>(claim.Value, TenantPermission.EntityName))
                .Where(permission => permission.IsValid);

            if (tenantPermissions is null)
            {
                _logger.LogDebug("No tenant permissions for looking up {Route}", route);
                return NotFound();
            }

            var tenant = await _tenantService.GetTenantInfoByRouteAsync(route);

            if (tenant is null)
            {
                _logger.LogDebug("Was not able to find tenant with route {Route}", route);
                return NotFound();
            }

            if (tenantPermissions.Any(permission => permission.Entity == tenant.Id))
            {
                _logger.LogDebug("Found {TenantName} with route {Route}", tenant.Name, route);

                return Ok(tenant);
            }
            else
            {
                _logger.LogDebug("Found {TenantName} with route {Route}, but requesting user does not have access to it.", tenant.Name, route);
                return NotFound();
            }
        }
        catch (ItemNotFoundExcepton)
        {
            _logger.LogDebug("Was not able to find tenant with route {Route}", route);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem retrieving tenant with route {Route}", route);
            throw;
        }
    }

    /// <summary>
    /// Get all users associated with a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    /// <remarks>
    /// <para>Right now only returns user IDs, should consider returning a user object with 
    /// user info + permissions for the tenant</para>
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [Route("{tenantId}/users")]
    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(permissionValue: TenantPermissionKind.Read, "tenantId")]
    public async Task<ActionResult<IEnumerable<UserDTO>>> GetTenantUsers(Guid tenantId)
    {
        try
        {
            _logger.LogDebug("Retrieving users for tenant {TenantID} by {User}", tenantId, User?.Identity?.Name);

            ICollection<User>? users = await _permissionsServiceClient.GetTenantUsersAsync(tenantId);

            List<UserDTO> returnValue = users.Select(u => new UserDTO(u.UserId, u.DisplayName)).ToList();

            _logger.LogDebug("Returning {UserCount} users for tenant {TenantID} to {User}", returnValue.Count, tenantId, User?.Identity?.Name);
            return Ok(returnValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem retrieving users for {TenantID}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Get user associated with a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="userId"></param>
    /// <returns></returns>
    /// <remarks>
    /// <para>Right now only returns the user ID, should consider returning a user object with 
    /// user info + permissions for the tenant</para>
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [Route("{tenantId}/User/{userId}")]
    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(permissionValue: TenantPermissionKind.Read, "tenantId")]
    public async Task<ActionResult<UserDTO>> GetTenantUser(Guid tenantId, Guid userId)
    {
        try
        {
            _logger.LogDebug("Retrieving user {UserID} for tenant {TenantID} by {User}", userId, tenantId, User?.Identity?.Name);

            User user = await _permissionsServiceClient.GetTenantUserAsync(tenantId, userId);

            UserDTO returnValue = new UserDTO(user.UserId, user.DisplayName);
            return Ok(returnValue);
        }
        catch (ItemNotFoundExcepton)
        {
            _logger.LogDebug("Was not able to find user {UserID} in tenant {TeantntID}", userId, tenantId);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Problem retrieving user {UserID} for {TenantID}", userId, tenantId);
            throw;
        }
    }

    /// <summary>
    /// Get all permissions a user has in a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="userId"></param>
    /// <returns></returns>
    /// <remarks>This might be better combined with GetTenantUsers, all usescases seem like they would need both</remarks>
    [HttpGet("{tenantId}/Users/{userId}/permissions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Read, "tenantId")]
    [SaasAuthorize<SaasUserPermissionRequirement, UserPermissionKind>(UserPermissionKind.Read, "userId")]
    public async Task<ActionResult<IEnumerable<string>>> GetUserPermissions(Guid tenantId, Guid userId)
    {
        IEnumerable<string> permissions = await _permissionsServiceClient.GetUserPermissionsForTenantAsync(tenantId, userId);
        return permissions.ToList();
    }

    /// <summary>
    /// Add a set of permissions for a user on a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="userId"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    [HttpPost("{tenantId}/Users/{userId}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Admin, "tenantId")]
    [SaasAuthorize<SaasUserPermissionRequirement, UserPermissionKind>(UserPermissionKind.Create, "userId")]
    public async Task<IActionResult> PostUserPermissions(Guid tenantId, Guid userId, [FromBody] string[] permissions)
    {
        await _permissionsServiceClient.AddUserPermissionsToTenantAsync(tenantId, userId, permissions);
        return NoContent();
    }

    /// <summary>
    /// Add a set of permissions for a user on a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="userEmail"></param>
    /// <returns></returns>
    [HttpPost("{tenantId}/invite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]

    [Authorize(Policy = TenantAdminOrServicePolicy.Name)]
    public async Task<IActionResult> InviteUserToTenant(Guid tenantId, string userEmail, string? role = null)
    {
        // A Super-Admin invites users with a specific tenant role (Bursar, Billing-Accountant, ...).
        // When no role is supplied we preserve the previous behavior (invitee becomes a tenant Admin)
        // so older callers keep working; the FE always passes an explicit role.
        role ??= TenantRole.Admin;
        if (!TenantRole.IsKnown(role))
        {
            return BadRequest($"Unknown tenant role '{role}'.");
        }

        try
        {
            // Enforce the purchased seat count before adding a user (no-op for non-marketplace tenants).
            await _seatService.EnsureSeatAvailableAsync(tenantId);
        }
        catch (SeatLimitExceededException ex)
        {
            _logger.LogInformation(ex, "Add-user rejected for tenant {TenantId}: seat limit reached.", tenantId);
            return Conflict(ex.Message);
        }

        // Record a pending invitation by email. Under Workforce multitenant the invitee lives in the
        // customer's own directory and cannot be resolved via the publisher's Graph, so we do NOT look
        // them up here — they are bound to their real object id on first sign-in (JIT). The role expands
        // to its backing CRUD permission + Role: tag, both granted on bind.
        await _membershipClient.CreateInvitationAsync(
            tenantId,
            userEmail,
            TenantRole.ToPermissionStrings(role));

        return NoContent();
    }

    /// <summary>
    /// Assign a tenant role to an existing member (already bound to the tenant).
    /// </summary>
    /// <remarks>
    /// <para><b>Requires:</b> {tenantID}.tenant.admin — a Super-Admin (or Admin) manages roles.</para>
    /// <para>The role expands to its backing CRUD permission + <c>Role:</c> tag, both added for the user
    /// on this tenant. Roles are additive; use the permissions delete endpoint to remove a grant.</para>
    /// </remarks>
    [HttpPost("{tenantId}/Users/{userId}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]

    [Authorize(Policy = TenantAdminOrServicePolicy.Name)]
    public async Task<IActionResult> AssignTenantRole(Guid tenantId, Guid userId, [FromQuery] string role)
    {
        if (!TenantRole.IsKnown(role))
        {
            return BadRequest($"Unknown tenant role '{role}'.");
        }

        await _permissionsServiceClient.AddUserPermissionsToTenantAsync(
            tenantId, userId, TenantRole.ToPermissionStrings(role));

        return NoContent();
    }

    /// <summary>
    /// List the tenant roles a Super-Admin may assign (drives the invite / assign-role UI).
    /// </summary>
    [HttpGet("roles")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IEnumerable<string>> GetAssignableRoles() => Ok(TenantRole.All);

    /// <summary>
    /// Bind the signed-in user to a pending invitation on first sign-in (JIT membership).
    /// </summary>
    /// <remarks>
    /// <para>Identity is taken from the caller's validated token — never from client params — so a
    /// user can only bind themselves. Requires authentication only: the user has no tenant permissions
    /// yet, and access is granted solely by a matching pending invitation. Assumes the caller presents
    /// the user's token (passthrough/OBO), not an app-only token. Returns the bind outcome
    /// (Bound / AlreadyMember / NoInvitation).</para>
    /// </remarks>
    [HttpPost("{tenantId}/members/bind")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BindMember(Guid tenantId)
    {
        if (!Guid.TryParse(User.GetObjectId(), out var userId))
        {
            return Unauthorized();
        }

        var email = User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest("The token does not contain an email / preferred_username claim.");
        }

        var displayName = User.FindFirstValue("name");

        var outcome = await _membershipClient.BindMemberAsync(tenantId, userId, email, displayName);
        return Ok(outcome);
    }

    /// <summary>
    /// Delete a set of permissions for a user on a tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="userId"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    [HttpDelete("{tenantId}/Users/{userId}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasTenantPermissionRequirement, TenantPermissionKind>(TenantPermissionKind.Admin, "tenantId")]
    [SaasAuthorize<SaasUserPermissionRequirement, UserPermissionKind>(UserPermissionKind.Delete, "userId")]
    public async Task<IActionResult> DeleteUserPermissions(Guid tenantId, Guid userId, [FromBody] string[] permissions)
    {
        await _permissionsServiceClient.RemoveUserPermissionsFromTenantAsync(tenantId, userId, permissions);
        return NoContent();
    }

    /// <summary>
    /// Get all tenant IDs that a user has access to
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    [HttpGet("user/{userId}/tenants")]
    [Produces("application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]

    [SaasAuthorize<SaasUserPermissionRequirement, UserPermissionKind>(UserPermissionKind.Self, "userId")]
    public async Task<ActionResult<IEnumerable<TenantDTO>>> UserTenants(Guid userId)
    {
        _logger.LogDebug("Getting all tenants for user {userID}", userId);

        IEnumerable<Guid> tenantIds = await _permissionsServiceClient.GetTenantsForUserAsync(userId);
        IEnumerable<TenantDTO>? tenants = await _tenantService.GetTenantsByIdAsync(tenantIds);
        return tenants.ToList();
    }

    [HttpGet("IsValidPath/{path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]

    [Authorize(Policy = ServiceAccessPolicy.Name)]
    public async Task<ActionResult<bool>> IsValidPath(string path)
    {
        _logger.LogDebug("Validating Path {path}", path);

        bool pathExists = await _tenantService.CheckPathExists(path);
        return !pathExists;
    }
}
