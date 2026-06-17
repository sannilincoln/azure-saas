using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Resolves a customer's Entra tenant id (token <c>tid</c>) to its provisioned tenant + per-tenant
/// database name. The multitenant product API calls this once per request (cached) to turn the
/// caller's <c>tid</c> into the tenant it should serve and the database it should connect to.
/// </summary>
/// <remarks>
/// Called service-to-service by the product API. Currently any authenticated caller is allowed;
/// Phase 4.3 tightens this to the service app-role (app-only token) once the Admin API accepts those —
/// the same posture as <see cref="TenantQuotaController"/>. Kept in its own controller so the
/// marketplace-gated <see cref="ISubscriptionQueryService"/> dependency stays isolated to this
/// endpoint (the always-on <c>TenantsController</c> must not require it).
/// </remarks>
[Authorize]
[ApiController]
[Route("api/tenants/by-tid")]
public class TenantResolutionController(ISubscriptionQueryService subscriptionQuery) : ControllerBase
{
    [HttpGet("{customerTenantId:guid}")]
    public async Task<ActionResult<TenantInfoDTO>> GetByCustomerTenant(Guid customerTenantId)
    {
        var tenant = await subscriptionQuery.GetTenantByCustomerTenantAsync(customerTenantId);
        return tenant is null ? NotFound() : Ok(new TenantInfoDTO(tenant));
    }
}
