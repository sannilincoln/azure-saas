using Azure.Identity;
using Marketplace.SaaS.Accelerator.Services.Configurations;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Marketplace.SaaS.Accelerator.Services.Services;
using Marketplace.SaaS.Accelerator.Services.Utilities;
using Microsoft.Marketplace.SaaS;
using Saas.Shared.Options;
using AcceleratorILogger = Marketplace.SaaS.Accelerator.Services.Contracts.ILogger;

namespace Saas.Admin.Service.Fulfillment;

public static class MarketplaceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the vendored accelerator fulfillment client (authenticating as the publisher
    /// service principal) and our fulfillment glue. Call only when the marketplace feature is
    /// configured (publisher credentials + marketplace DB present).
    /// </summary>
    public static IServiceCollection AddMarketplaceFulfillment(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection(MarketplaceOptions.SectionName).Get<MarketplaceOptions>()
            ?? throw new InvalidOperationException("Marketplace section is missing from configuration.");

        var tenantId = options.PublisherTenantId ?? throw new InvalidOperationException("Marketplace:PublisherTenantId is required.");
        var clientId = options.PublisherClientId ?? throw new InvalidOperationException("Marketplace:PublisherClientId is required.");
        var clientSecret = options.PublisherClientSecret ?? throw new InvalidOperationException("Marketplace:PublisherClientSecret is required.");

        // Publisher service principal — client-credentials against the SaaS Fulfillment API.
        // The fixed marketplace resource scope is applied internally by the SDK.
        services.AddSingleton<IMarketplaceSaaSClient>(_ =>
            new MarketplaceSaaSClient(new ClientSecretCredential(tenantId, clientId, clientSecret)));

        // FulfillmentApiService only reads ClientConfiguration for GetSaaSAppURL(); everything
        // else goes through the SDK client above.
        services.AddSingleton(new SaaSApiClientConfiguration
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            TenantId = tenantId,
            SaaSAppUrl = options.SaaSAppUrl,
        });

        services.AddSingleton<AcceleratorILogger>(new SaaSClientLogger<FulfillmentApiService>());
        services.AddScoped<IFulfillmentApiService, FulfillmentApiService>();
        services.AddScoped<IMarketplaceFulfillmentService, MarketplaceFulfillmentService>();

        return services;
    }
}
