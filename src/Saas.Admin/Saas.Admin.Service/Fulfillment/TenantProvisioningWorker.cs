namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Background worker that drains tenants queued for provisioning (marked <c>Provisioning</c> by
/// <see cref="IMarketplaceFulfillmentService.ActivateAsync"/>) out of band. Moving the slow work
/// (CREATE DATABASE + migrate + seed, ~30–60s) here is what keeps the onboarding HTTP request fast
/// and free of the gateway timeout that previously surfaced as a 502.
/// </summary>
/// <remarks>
/// Polling (rather than an in-memory queue) is deliberate: it is self-healing across restarts — on
/// start it simply re-discovers any tenants still in <c>Provisioning</c>. <see cref="IMarketplaceFulfillmentService"/>
/// is scoped (it owns scoped DbContexts), so each pass runs inside its own DI scope.
/// </remarks>
public sealed class TenantProvisioningWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TenantProvisioningWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TenantProvisioningWorker started; polling every {Seconds}s.", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var fulfillment = scope.ServiceProvider.GetRequiredService<IMarketplaceFulfillmentService>();
                await fulfillment.ProcessPendingProvisioningAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Never let a bad pass kill the worker — log and try again next interval.
                logger.LogError(ex, "TenantProvisioningWorker iteration failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("TenantProvisioningWorker stopping.");
    }
}
