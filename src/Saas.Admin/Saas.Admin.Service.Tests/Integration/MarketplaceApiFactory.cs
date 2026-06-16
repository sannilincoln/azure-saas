using System;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Marketplace.SaaS.Accelerator.DataAccess.Context;
using Marketplace.SaaS.Accelerator.Services.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Saas.Admin.Service.Controllers;
using Saas.Admin.Service.Data;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;

namespace Saas.Admin.Service.Tests.Integration;

/// <summary>
/// A self-contained in-process host for integration-testing the marketplace HTTP surface WITHOUT
/// Azure App Configuration, Key Vault, real Entra tokens, or SQL Server. It boots a <see cref="TestServer"/>
/// that registers the <em>real</em> <see cref="MarketplaceSubscriptionsController"/> +
/// <see cref="SubscriptionQueryService"/> + EF contexts (InMemory) behind the real authentication and
/// authorization pipeline. The only stand-ins are:
/// <list type="bullet">
///   <item>a <see cref="StubAuthHandler"/> that mints a principal carrying the <c>tid</c> sent in the
///   <c>X-Test-Tid</c> header (so a test can act as the publisher tenant or any customer tenant), and</item>
///   <item>a substituted <see cref="IFulfillmentApiService"/> (the only thing that would otherwise call
///   Microsoft).</item>
/// </list>
/// This exercises real routing, model binding, <c>[Authorize]</c>, DI resolution, the
/// <c>User.GetTenantId()</c> plumbing, and EF reads/writes end-to-end over HTTP — the wiring the
/// controller-level unit tests deliberately bypass.
/// </summary>
internal sealed class MarketplaceApiFactory : IDisposable
{
    private readonly IHost _host;
    private readonly string _dbId = Guid.NewGuid().ToString("N");

    /// <summary>The tenant id that is treated as the publisher (matches <c>MarketplaceOptions.PublisherTenantId</c>).</summary>
    public Guid PublisherTenantId { get; } = Guid.NewGuid();

    public MarketplaceApiFactory()
    {
        _host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Real data layer, on InMemory keyed per-factory so each test is isolated but all
                    // scopes inside this host share the same store (seed once, read from requests).
                    services.AddDbContext<SaasKitContext>(o => o.UseInMemoryDatabase($"mkt-{_dbId}"));
                    services.AddDbContext<TenantsContext>(o => o.UseInMemoryDatabase($"tenants-{_dbId}"));

                    // The only external boundary is stubbed; everything else is the production type.
                    services.AddScoped(_ => Substitute.For<IFulfillmentApiService>());
                    services.AddScoped<ISubscriptionQueryService, SubscriptionQueryService>();
                    services.AddScoped<ITenantQuotaService, TenantQuotaService>();

                    services.AddSingleton(Options.Create(new MarketplaceOptions
                    {
                        PublisherTenantId = PublisherTenantId.ToString(),
                        TierMaxStudents = new System.Collections.Generic.Dictionary<int, int> { [7] = 2000 },
                    }));

                    services.AddAuthentication(StubAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, StubAuthHandler>(StubAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();

                    services.AddControllers()
                        .AddApplicationPart(typeof(MarketplaceSubscriptionsController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Start();
    }

    /// <summary>An HTTP client whose requests are authenticated as <paramref name="callerTenantId"/>,
    /// or anonymous when null.</summary>
    public HttpClient CreateClient(Guid? callerTenantId)
    {
        var client = _host.GetTestClient();
        if (callerTenantId is Guid tid)
        {
            client.DefaultRequestHeaders.Add(StubAuthHandler.TenantHeader, tid.ToString());
        }

        return client;
    }

    /// <summary>Seed the marketplace + tenants stores in their own scope.</summary>
    public void Seed(Action<SaasKitContext, TenantsContext> seed)
    {
        using var scope = _host.Services.CreateScope();
        seed(
            scope.ServiceProvider.GetRequiredService<SaasKitContext>(),
            scope.ServiceProvider.GetRequiredService<TenantsContext>());
    }

    /// <summary>Read back persisted state in a fresh scope (to assert write-through).</summary>
    public T Query<T>(Func<SaasKitContext, TenantsContext, T> query)
    {
        using var scope = _host.Services.CreateScope();
        return query(
            scope.ServiceProvider.GetRequiredService<SaasKitContext>(),
            scope.ServiceProvider.GetRequiredService<TenantsContext>());
    }

    public void Dispose() => _host.Dispose();
}

/// <summary>
/// Test authentication scheme: if the request carries an <c>X-Test-Tid</c> header, the request is
/// authenticated as a user of that tenant (claims mirror what Entra issues: the SAML-style and short
/// <c>tid</c> claims plus a NameIdentifier). No header → unauthenticated, so <c>[Authorize]</c> yields 401.
/// </summary>
internal sealed class StubAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-Tid";

    public StubAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(TenantHeader, out var tid) || string.IsNullOrWhiteSpace(tid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim("http://schemas.microsoft.com/identity/claims/tenantid", tid!),
            new Claim("tid", tid!),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
