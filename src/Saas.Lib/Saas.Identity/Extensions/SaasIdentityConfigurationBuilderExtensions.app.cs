using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Saas.Identity.Claims;
using Saas.Shared.Options;

namespace Saas.Identity.Extensions;
public static partial class SaasIdentityConfigurationBuilderExtensions
{
    public static SaasWebAppClientCredentialBuilder AddSaasWebAppAuthentication(
        this IServiceCollection services,
        string configSectionName,
        ConfigurationManager configuration,
        IEnumerable<string> scopes)
    {
        // Registerer scopes to the Options collection
        services.Configure<SaasAppScopeOptions>(saasAppScopeOptions =>
            saasAppScopeOptions.Scopes = scopes.ToArray());

        // Entra External ID: map the 'oid' claim into NameIdentifier (B2C used to put
        // the object-id GUID in 'sub'/NameIdentifier; External ID does not). Also kept
        // as an IClaimsTransformation for the bearer-token (API) path. See
        // NameIdentifierClaimsTransformation.
        services.AddScoped<IClaimsTransformation, NameIdentifierClaimsTransformation>();

        var authenticationBuilder = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(options =>
            {
                configuration.Bind(configSectionName, options);
            });

        // For the interactive web-app sign-in, the cookie/OIDC scheme means an
        // IClaimsTransformation doesn't reliably see the signed-in principal. Map
        // 'oid' -> NameIdentifier at token validation instead, so it is persisted into
        // the auth cookie. PostConfigure runs after Microsoft.Identity.Web wires its own
        // events, so we chain rather than replace OnTokenValidated.
        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                if (previous is not null)
                {
                    await previous(context);
                }

                var logger = context.HttpContext.RequestServices
                    .GetService<ILoggerFactory>()?.CreateLogger("NameIdentifierMapping");

                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    var mapped = NameIdentifierClaimsTransformation.TryMapObjectIdToNameIdentifier(identity);
                    // TEMP DIAGNOSTIC (Warning so it surfaces at default log levels):
                    // confirm the event fires, whether it mapped, and what the token carries.
                    logger?.LogWarning(
                        "OnTokenValidated fired. mapped={Mapped}. claim types: [{Types}]",
                        mapped,
                        string.Join(", ", identity.Claims.Select(c => c.Type)));
                }
                else
                {
                    logger?.LogWarning("OnTokenValidated fired but Principal/Identity was null.");
                }
            };
        });

        return new SaasWebAppClientCredentialBuilder(authenticationBuilder, scopes);
    }

    public static SaasWebAppClientCredentialBuilder AddSaasWebAppAuthentication(
    this IServiceCollection services,
    IEnumerable<string> scopes,
    Action<MicrosoftIdentityOptions> configureMicrosoftIdentityOptions)
    {
        // Registerer scopes to the Options collection
        services.Configure<SaasAppScopeOptions>(saasAppScopeOptions =>
            saasAppScopeOptions.Scopes = scopes.ToArray());


        var authenticationBuilder = services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(configureMicrosoftIdentityOptions);

        return new SaasWebAppClientCredentialBuilder(authenticationBuilder, scopes);
    }

    public class SaasWebAppClientCredentialBuilder(
        MicrosoftIdentityWebAppAuthenticationBuilder authenticationBuilder,
        IEnumerable<string> scopes)
    {
        private readonly MicrosoftIdentityWebAppAuthenticationBuilder _authenticationBuilder = authenticationBuilder;
        private readonly IEnumerable<string> _scopes = scopes;

        public MicrosoftIdentityAppCallsWebApiAuthenticationBuilder SaaSAppCallDownstreamApi(IEnumerable<string>? scopes = default)
        {
            return _authenticationBuilder
                .EnableTokenAcquisitionToCallDownstreamApi(
                    options =>
                    {
                        // In case of wanting to make changes to the ConfidentialClientApplicationOptions
                    },
                    scopes ?? _scopes);
        }}
    }
