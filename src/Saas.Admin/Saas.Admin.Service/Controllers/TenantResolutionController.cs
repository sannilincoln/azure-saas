using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Saas.Admin.Service.Authorization;
using Saas.Admin.Service.Fulfillment;

namespace Saas.Admin.Service.Controllers;

/// <summary>
/// Resolves a customer's Entra tenant id (token <c>tid</c>) to its provisioned tenant + per-tenant
/// database name. The multitenant product API calls this once per request (cached) to turn the
/// caller's <c>tid</c> into the tenant it should serve and the database it should connect to.
/// </summary>
/// <remarks>
/// Called service-to-service by the product API, authenticated with an app-only token carrying the
/// <c>Service.Access</c> app role (Phase 4.3). Kept in its own controller so the marketplace-gated
/// <see cref="ISubscriptionQueryService"/> dependency stays isolated to this endpoint (the always-on
/// <c>TenantsController</c> must not require it).
/// </remarks>
[Authorize(Policy = ServiceAccessPolicy.Name)]
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
