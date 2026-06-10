namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Default seat guard used when the marketplace feature isn't configured for this environment.
/// Enforces nothing — the Admin API must keep working (and stay deployable incrementally) before
/// the marketplace database and publisher credentials are provisioned.
/// </summary>
public sealed class NoopMarketplaceSeatService : IMarketplaceSeatService
{
    public Task EnsureSeatAvailableAsync(Guid tenantId) => Task.CompletedTask;
}
