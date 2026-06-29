using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Saas.Admin.Service.Fulfillment;
using Saas.Shared.Options;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class MarketplaceNotificationSettingsStoreTests
{
    private static MarketplaceNotificationSettingsStore Build(
        Marketplace.SaaS.Accelerator.DataAccess.Context.SaasKitContext db) =>
        new(db, Options.Create(new MarketplaceOptions()));

    [Fact]
    public async Task SaveThenGet_RoundTripsMasterAndPerFlowToggles()
    {
        using var db = MarketplaceTestHelpers.NewMarketplaceDb();
        var store = Build(db);
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: "ops@lagetronix.com", CopyToCustomer: false,
            SignupAlert: true, Welcome: true, Invite: false, RoleChange: true);

        await store.SaveAsync(settings);
        var loaded = await store.GetAsync();

        Assert.True(loaded.Enabled);
        Assert.True(loaded.SignupAlert);
        Assert.True(loaded.Welcome);
        Assert.False(loaded.Invite);
        Assert.True(loaded.RoleChange);
        Assert.Equal("ops@lagetronix.com", loaded.ToEmails);
    }

    [Fact]
    public async Task Get_DefaultsTogglesOff_WhenUnset()
    {
        using var db = MarketplaceTestHelpers.NewMarketplaceDb();

        var loaded = await Build(db).GetAsync();

        Assert.False(loaded.SignupAlert);
        Assert.False(loaded.Welcome);
        Assert.False(loaded.Invite);
        Assert.False(loaded.RoleChange);
    }
}
