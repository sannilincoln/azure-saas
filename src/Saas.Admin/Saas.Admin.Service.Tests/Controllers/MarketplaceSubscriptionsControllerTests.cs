using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Saas.Admin.Service.Controllers;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;
using Xunit;

namespace Saas.Admin.Service.Tests.Controllers;

public class MarketplaceSubscriptionsControllerTests
{
    private const string TidClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    private static MarketplaceSubscriptionsController BuildController(
        ISubscriptionQueryService service,
        string? publisherTenantId,
        Guid callerTenantId)
    {
        var options = Options.Create(new MarketplaceOptions { PublisherTenantId = publisherTenantId });
        var controller = new MarketplaceSubscriptionsController(
            service, options, NullLogger<MarketplaceSubscriptionsController>.Instance);

        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim("tid", callerTenantId.ToString()));
        identity.AddClaim(new Claim(TidClaimType, callerTenantId.ToString()));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    [Fact]
    public async Task GetAll_PublisherCaller_ReturnsOk()
    {
        var publisherTenant = Guid.NewGuid();
        var service = Substitute.For<ISubscriptionQueryService>();
        service.GetAllAsync().Returns(new List<SubscriptionDto> { new() { SubscriptionId = Guid.NewGuid() } });

        var controller = BuildController(service, publisherTenant.ToString(), callerTenantId: publisherTenant);

        var result = await controller.GetAll();

        Assert.IsType<OkObjectResult>(result.Result);
        await service.Received(1).GetAllAsync();
    }

    [Fact]
    public async Task GetAll_CustomerCaller_IsForbidden()
    {
        var service = Substitute.For<ISubscriptionQueryService>();

        // Caller's tenant differs from the configured publisher tenant.
        var controller = BuildController(service, Guid.NewGuid().ToString(), callerTenantId: Guid.NewGuid());

        var result = await controller.GetAll();

        Assert.IsType<ForbidResult>(result.Result);
        await service.DidNotReceive().GetAllAsync();
    }

    [Fact]
    public async Task GetAll_PublisherTenantNotConfigured_IsForbidden()
    {
        var service = Substitute.For<ISubscriptionQueryService>();

        var controller = BuildController(service, publisherTenantId: null, callerTenantId: Guid.NewGuid());

        var result = await controller.GetAll();

        Assert.IsType<ForbidResult>(result.Result);
        await service.DidNotReceive().GetAllAsync();
    }

    [Fact]
    public async Task GetMine_FiltersByCallersOwnTenant()
    {
        var callerTenant = Guid.NewGuid();
        var service = Substitute.For<ISubscriptionQueryService>();
        service.GetByCustomerTenantAsync(callerTenant).Returns(new List<SubscriptionDto>());

        // Publisher tenant is something else entirely — irrelevant to the customer endpoint.
        var controller = BuildController(service, Guid.NewGuid().ToString(), callerTenantId: callerTenant);

        var result = await controller.GetMine();

        Assert.IsType<OkObjectResult>(result.Result);
        // The caller can only ever query their OWN tenant id — never a client-supplied one.
        await service.Received(1).GetByCustomerTenantAsync(callerTenant);
    }

    [Fact]
    public async Task Refresh_CustomerCaller_IsForbidden()
    {
        var service = Substitute.For<ISubscriptionQueryService>();

        var controller = BuildController(service, Guid.NewGuid().ToString(), callerTenantId: Guid.NewGuid());

        var result = await controller.Refresh(Guid.NewGuid());

        Assert.IsType<ForbidResult>(result.Result);
        await service.DidNotReceive().RefreshFromMarketplaceAsync(Arg.Any<Guid>());
    }
}
