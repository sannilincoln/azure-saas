using Marketplace.SaaS.Accelerator.DataAccess.Context;

namespace Saas.Admin.Service.Data;

public static class TenantDbInitializer
{
    public static void ConfigureDatabase(this IHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();

        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<TenantsContext>>();
        TenantsContext tenantsContext = scope.ServiceProvider.GetRequiredService<TenantsContext>();

        CreateDatabase(tenantsContext, logger);
        SeedDatabase(tenantsContext, logger);

        // Migrate the marketplace store too, when it's registered (a marketplace connection
        // string is configured). Uses the vendored accelerator's own migration history.
        SaasKitContext? marketplaceContext = scope.ServiceProvider.GetService<SaasKitContext>();
        if (marketplaceContext is not null)
        {
            CreateMarketplaceDatabase(marketplaceContext, logger);
        }
    }

    private static void CreateMarketplaceDatabase(SaasKitContext marketplaceContext, ILogger logger)
    {
        try
        {
            marketplaceContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to create the marketplace database");
            throw;
        }
    }

    private static void CreateDatabase(TenantsContext tenantsContext, ILogger logger)
    {
        try
        {
            /////////////////////////////////////////////////////////////////////////////////////////
            // In a production environment, use EF tools to apply migrations during deployment
            // This is here to simplify the demo application
            ////////////////////////////////////////////////////////////////////////////////////////
            tenantsContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unable to create the database");
            throw;
        }
    }

    private static void SeedDatabase(TenantsContext tenantsContext, ILogger logger)
    {
        try
        {
            if (tenantsContext.Tenants.Any())
            {
                return;   // DB has been seeded
            }

            //Add any code required to seed the database here
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Error while seeding the database");
            throw;
        }
    }
}
