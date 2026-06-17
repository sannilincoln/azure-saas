using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class HttpProductProvisioningServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Records each request and replays a queued sequence of responses (last one repeats).</summary>
    private sealed class StubHandler(IEnumerable<HttpStatusCode> statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var status = _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
            return new HttpResponseMessage(status);
        }
    }

    private static (HttpProductProvisioningService svc, StubHandler handler, IServiceTokenProvider tokens) Build(
        params HttpStatusCode[] statuses)
    {
        var handler = new StubHandler(statuses);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://product.example/") };
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAppTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("test-token");
        var options = Options.Create(new ProductProvisioningOptions
        {
            BaseUrl = "https://product.example/",
            Scope = "api://6a3e6083/.default",
            MaxRetries = 2,
            RetryDelaySeconds = 0, // keep tests fast
        });
        var svc = new HttpProductProvisioningService(http, tokens, options, NullLogger<HttpProductProvisioningService>.Instance);
        return (svc, handler, tokens);
    }

    [Fact]
    public async Task Provision_PostsToInternalEndpoint_WithDatabaseName_AndBearerToken()
    {
        var (svc, handler, _) = Build(HttpStatusCode.OK);

        await svc.ProvisionAsync(TenantId, "edulynk-acme");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"https://product.example/internal/tenants/{TenantId}/provision", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization.Parameter);
        Assert.Contains("edulynk-acme", handler.Bodies[0]);
    }

    [Fact]
    public async Task Provision_RetriesOnTransientError_ThenSucceeds()
    {
        var (svc, handler, _) = Build(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        await svc.ProvisionAsync(TenantId, "edulynk-acme");

        Assert.Equal(2, handler.Requests.Count); // one failed transient attempt, then success
    }

    [Fact]
    public async Task Provision_ThrowsAndDoesNotRetry_OnTerminalError()
    {
        var (svc, handler, _) = Build(HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<ProductProvisioningException>(() => svc.ProvisionAsync(TenantId, "edulynk-acme"));

        Assert.Single(handler.Requests); // 4xx is terminal — no retry
    }

    [Fact]
    public async Task Provision_ThrowsAfterExhaustingRetries_OnPersistentTransientError()
    {
        var (svc, handler, _) = Build(HttpStatusCode.ServiceUnavailable); // always 503

        await Assert.ThrowsAsync<ProductProvisioningException>(() => svc.ProvisionAsync(TenantId, "edulynk-acme"));

        Assert.Equal(3, handler.Requests.Count); // 1 + MaxRetries(2)
    }
}
