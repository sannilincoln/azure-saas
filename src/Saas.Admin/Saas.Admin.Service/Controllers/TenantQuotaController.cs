using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saas.Admin.Service.Authorization;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Publishes a tenant's student quota (plan/tier-derived ceiling) to the product app, which enforces
/// it by counting student rows. Read-only and small on purpose: the platform owns the ceiling number,
/// the product owns the count.
/// </summary>
/// <remarks>
/// Called service-to-service by the product API, authenticated with an app-only token carrying the
/// <c>Service.Access</c> app role (Phase 4.3).
/// </remarks>
[Authorize(Policy = ServiceAccessPolicy.Name)]
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
