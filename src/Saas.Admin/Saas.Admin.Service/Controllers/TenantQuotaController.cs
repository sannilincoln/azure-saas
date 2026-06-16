using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Publishes a tenant's student quota (plan/tier-derived ceiling) to the product app, which enforces
/// it by counting student rows. Read-only and small on purpose: the platform owns the ceiling number,
/// the product owns the count.
/// </summary>
/// <remarks>
/// Called service-to-service by the product API. Currently any authenticated caller is allowed;
/// Phase 4.3 tightens this to the service app-role (app-only token) once the Admin API accepts those.
/// </remarks>
[Authorize]
[ApiController]
[Route("api/tenants/{tenantId:guid}/quota")]
public class TenantQuotaController(ITenantQuotaService quotaService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TenantQuota>> Get(Guid tenantId)
    {
        var quota = await quotaService.GetQuotaAsync(tenantId);
        return quota is null ? NotFound() : Ok(quota);
    }
}
