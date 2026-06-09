namespace Saas.Shared.Options;

public record EntraPermissionsApiOptions : EntraIdentityOptions
{
    // NOTE: config-key still ":AzureB2C" until the bicep/App-Config/Key-Vault rename in Phase H.
    public const string SectionName = "PermissionsApi:AzureB2C";
}
