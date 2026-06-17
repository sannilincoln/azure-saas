using Saas.Admin.Service.Controllers;
using Saas.Admin.Service.Data;
using Xunit;

namespace Saas.Admin.Service.Tests.Controllers;

public class TenantInfoDTOTests
{
    [Fact]
    public void Constructor_CopiesDatabaseName()
    {
        var tenant = new Tenant
        {
            Name = "Greenfield",
            Route = "greenfield",
            DatabaseName = "edulynk-greenfield",
        };

        var dto = new TenantInfoDTO(tenant);

        Assert.Equal("edulynk-greenfield", dto.DatabaseName);
    }
}
