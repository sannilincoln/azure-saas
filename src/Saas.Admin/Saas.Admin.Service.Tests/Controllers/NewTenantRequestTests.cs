namespace Saas.Admin.Service.Tests;

public class NewTenantRequestTests
{
    [Theory, AutoDataNSubstitute]
    public void All_Values_Are_Copied_To_Tenant(NewTenantRequest tenantRequest)
    {
        Tenant tenant = tenantRequest.ToTenant();

        // SubscriptionId/SubscriptionStatus are marketplace-linkage columns set during fulfillment,
        // not part of the new-tenant request copy contract — skip them like the other non-copied fields.
        AssertAdditions.AllPropertiesAreEqual(tenant, tenantRequest, nameof(tenant.ConcurrencyToken), nameof(tenant.CreatedTime), nameof(tenant.Id), nameof(tenant.SubscriptionId), nameof(tenant.SubscriptionStatus));
    }
}
