using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saas.Admin.Service.Membership;
using Xunit;

namespace Saas.Admin.Service.Tests.Membership;

public class TenantMembershipClientTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static TenantMembershipClient Build(string json) =>
        new(new HttpClient(new StubHandler(json)) { BaseAddress = new Uri("https://permissions.example/") });

    [Fact]
    public async Task GetMemberEmail_ReturnsTheMatchingMembersEmail()
    {
        var userId = Guid.NewGuid();
        var json = $$"""
            [
              {"userId":"{{Guid.NewGuid()}}","email":"other@x.edu","displayName":"Other","roles":["Admin"]},
              {"userId":"{{userId}}","email":"bursar@acme.edu","displayName":"Jane","roles":["Bursar"]}
            ]
            """;

        var email = await Build(json).GetMemberEmailAsync(Guid.NewGuid(), userId);

        Assert.Equal("bursar@acme.edu", email);
    }

    [Fact]
    public async Task GetMemberEmail_ReturnsNull_WhenMemberNotFound()
    {
        var json = """[{"userId":"11111111-1111-1111-1111-111111111111","email":"x@x.edu","roles":[]}]""";

        var email = await Build(json).GetMemberEmailAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(email);
    }
}
