namespace Saas.Shared.Options;

public record SqlOptions
{
    public const string SectionName = "Sql";

    public string? SQLAdministratorLoginName { get; init; }
    public string? TenantSQLConnectionString { get; init; }
    public string? PermissionsSQLConnectionString { get; init; }

    /// <summary>
    /// Connection string for the Azure Marketplace fulfillment store (the vendored accelerator
    /// SaasKitContext). Optional: when absent, the marketplace feature stays inert so
    /// environments without the marketplace DB provisioned keep working.
    /// </summary>
    public string? MarketplaceSQLConnectionString { get; init; }
}
