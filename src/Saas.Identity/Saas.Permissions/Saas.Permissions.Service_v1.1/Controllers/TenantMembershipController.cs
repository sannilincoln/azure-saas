using Microsoft.AspNetCore.Mvc;
using Saas.Permissions.Service.Interfaces;
using Saas.Permissions.Service.Models;

namespace Saas.Permissions.Service.Controllers;

/// <summary>
/// JIT tenant membership endpoints (Workforce multitenant): create an invitation by email, and bind a
/// signed-in user to a pending invitation on first sign-in. Called service-to-service by the Admin API
/// — these replace the Graph-based email lookup, which can't see users in a customer's own directory.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class TenantMembershipController(ITenantMembershipService membership) : ControllerBase
{
    /// <summary>Records a pending invitation for an email with the given permission strings.</summary>
    [HttpPost]
    [Route("CreateInvitation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateInvitation(Guid tenantId, string email, [FromBody] string[] permissions)
    {
        await membership.CreateInvitationAsync(tenantId, email, permissions);
        return Ok();
    }

    /// <summary>
    /// Binds the signed-in user to a pending invitation (matched by email) on first sign-in. Idempotent.
    /// Returns the bind outcome: Bound, AlreadyMember, or NoInvitation.
    /// </summary>
    [HttpPost]
    [Route("BindMember")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<string>> BindMember(Guid tenantId, Guid userId, string email, string? displayName)
    {
        var result = await membership.BindMemberAsync(tenantId, userId, email, displayName);
        return Ok(result.ToString());
    }

    /// <summary>Lists the tenant's members with the roles each holds (drives the team-management screen).</summary>
    [HttpGet]
    [Route("GetMembers")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantMemberDto>>> GetMembers(Guid tenantId)
    {
        return Ok(await membership.GetMembersAsync(tenantId));
    }
}
