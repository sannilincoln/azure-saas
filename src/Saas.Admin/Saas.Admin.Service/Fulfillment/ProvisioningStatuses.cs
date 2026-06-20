namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Lifecycle values for <see cref="Saas.Admin.Service.Data.Tenant.ProvisioningStatus"/>. Marketplace
/// onboarding links the subscription and marks the tenant <see cref="Provisioning"/>; the background
/// <c>TenantProvisioningWorker</c> performs the slow work (CREATE DATABASE + migrate + seed, then
/// Microsoft activation) out of band and advances to <see cref="Provisioned"/> or <see cref="Failed"/>.
/// </summary>
public static class ProvisioningStatuses
{
    public const string Provisioning = "Provisioning";
    public const string Provisioned = "Provisioned";
    public const string Failed = "Failed";
}
