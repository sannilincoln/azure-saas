using Saas.Admin.Service.Data;

namespace Saas.Admin.Service.Controllers;

public class NewTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string CreatorEmail { get; set; } = string.Empty;

    /// <summary>
    /// Object id (Entra <c>oid</c>) of the user who is being made admin of the new tenant. Supplied
    /// explicitly because the creating call is app-only (service-to-service from the Sign-up/Admin web
    /// app) — there is no user token to read the identity from. The web app captures it from the
    /// signed-in customer and passes it here.
    /// </summary>
    public Guid CreatorObjectId { get; set; }

    public int ProductTierId { get; set; }
    public int CategoryId { get; set; }

    internal Tenant ToTenant()
    {
        Tenant tenant = new()
        {
            Name = Name,
            Route = Route,
            CreatorEmail = CreatorEmail,
            ConcurrencyToken = null,
            CreatedTime = null,
            CategoryId = CategoryId,
            ProductTierId = ProductTierId,
        };
        return tenant;
    }
}
