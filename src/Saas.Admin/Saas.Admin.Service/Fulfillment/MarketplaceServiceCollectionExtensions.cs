using Azure.Identity;
using Marketplace.SaaS.Accelerator.Services.Configurations;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Marketplace.SaaS.Accelerator.Services.Services;
using Marketplace.SaaS.Accelerator.Services.Utilities;
using Marketplace.SaaS.Accelerator.Services.WebHook;
using Microsoft.Marketplace.SaaS;
using Saas.Shared.Options;
using AcceleratorILogger = Marketplace.SaaS.Accelerator.Services.Contracts.ILogger;

namespace Saas.Admin.Service.Fulfillment;

public static class MarketplaceServiceCollectionExtensions
{
    /// <summary>
    /// The fixed Azure Marketplace SaaS API resource (app) id. Inbound connection-webhook JWTs
    /// carry this as their azp/appid; we validate against it.
    /// </summary>
    public const string MarketplaceSaaSApiResourceId = "20e940b3-4c77-4b0b-9a53-9e16a1b010a7";

    /// <summary>
    /// Registers the vendored accelerator fulfillment client (authenticating as the publisher
    /// service principal) and our fulfillment glue. Call only when the marketplace feature is
    /// configured (publisher credentials + marketplace DB present).
    /// </summary>
    public static IServiceCollection AddMarketplaceFulfillment(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection(MarketplaceOptions.SectionName).Get<MarketplaceOptions>()
            ?? throw new InvalidOperationException("Marketplace section is missing from configuration.");

        // Make the options injectable (e.g. the subscriptions console needs PublisherTenantId for
        // the server-side publisher check).
        services.Configure<MarketplaceOptions>(configuration.GetSection(MarketplaceOptions.SectionName));

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
            // Resource is the marketplace SaaS API app id — used by ValidateJwtToken to check the
            // inbound webhook JWT's azp/appid claim.
            Resource = MarketplaceSaaSApiResourceId,
            SaaSAppUrl = options.SaaSAppUrl,
        });

        services.AddSingleton<AcceleratorILogger>(new SaaSClientLogger<FulfillmentApiService>());
        services.AddScoped<IFulfillmentApiService, FulfillmentApiService>();
        services.AddScoped<IMarketplaceFulfillmentService, MarketplaceFulfillmentService>();

        // Optional publisher email on activation (a tenant signing up). The enabled flag + from/to
        // are publisher-editable (settings store); SMTP transport stays in config/Key Vault.
        services.AddScoped<IMarketplaceNotificationSettingsStore, MarketplaceNotificationSettingsStore>();
        services.AddScoped<IMarketplaceNotificationService, SmtpMarketplaceNotificationService>();

        // Read/manage layer behind the in-app publisher console + customer self-service.
        services.AddScoped<ISubscriptionQueryService, SubscriptionQueryService>();

        // Tenant student-quota resolution (plan/tier -> ceiling), published to the product app.
        services.AddScoped<ITenantQuotaService, TenantQuotaService>();

        // Connection webhook: inbound-JWT validator + our lifecycle handler.
        services.AddSingleton<ValidateJwtToken>();
        services.AddScoped<IWebhookHandler, MarketplaceWebhookHandler>();

        return services;
    }
}
