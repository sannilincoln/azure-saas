namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Provisions the product side of a newly activated tenant — most importantly its dedicated
/// per-tenant database. Called once, synchronously, at the end of marketplace activation. The
/// platform stays product-agnostic: it passes the tenant id and the database name it has chosen
/// (from <c>Marketplace:TenantDatabaseNamePrefix</c>); the concrete implementation (an HTTP call to
/// the product API) creates and migrates that database. Implementations MUST be idempotent so a
/// retried activation is safe.
/// </summary>
public interface IProductProvisioningService
{
    Task ProvisionAsync(Guid tenantId, string databaseName);
}

/// <summary>
/// Default provisioning service used when no product provisioning is configured. Does nothing, so the
/// platform (and non-product deployments) keep working before the product API exists.
/// </summary>
public sealed class NoopProductProvisioningService : IProductProvisioningService
{
    public Task ProvisionAsync(Guid tenantId, string databaseName) => Task.CompletedTask;
}
