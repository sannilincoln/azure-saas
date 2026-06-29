using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Saas.Admin.Service.Fulfillment;
using Xunit;

namespace Saas.Admin.Service.Tests.Fulfillment;

public class GraphEmailSenderTests
{
    private static SubscriptionActivatedNotice SampleNotice() =>
        new(SubscriptionId: Guid.NewGuid(), SubscriptionName: "Acme", OfferId: "offer-1",
            PlanId: "premium", Quantity: 5, TenantName: "Acme School", TenantRoute: "acme",
            CustomerEmail: "owner@acme.edu");

    private static GraphEmailSender Build(
        IGraphMailClient mail, MarketplaceNotificationSettings settings, NotificationBrandingOptions? branding = null)
    {
        var store = Substitute.For<IMarketplaceNotificationSettingsStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(settings);
        return new GraphEmailSender(mail, store,
            Microsoft.Extensions.Options.Options.Create(branding ?? new NotificationBrandingOptions()),
            NullLogger<GraphEmailSender>.Instance);
    }

    private static MarketplaceNotificationSettings Settings(
        bool enabled = true, bool signupAlert = false, bool welcome = false,
        bool invite = false, bool roleChange = false, string? to = null) =>
        new(Enabled: enabled, FromEmail: null, ToEmails: to, CopyToCustomer: false,
            SignupAlert: signupAlert, Welcome: welcome, Invite: invite, RoleChange: roleChange);

    private static UserInvitedNotice SampleInvite() =>
        new(Email: "bursar@acme.edu", Role: "Bursar", TenantName: "Acme School", TenantId: Guid.NewGuid());

    [Fact]
    public async Task SubscriptionActivated_SendsPublisherAlert_ToConfiguredRecipients()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: "ops@lagetronix.com", CopyToCustomer: false,
            SignupAlert: true);
        var sender = Build(mail, settings);

        await sender.NotifySubscriptionActivatedAsync(SampleNotice());

        await mail.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Contains("ops@lagetronix.com") && m.Subject.Contains("Acme School")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriptionActivated_SendsNothing_WhenNotificationsDisabled()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: false, FromEmail: null, ToEmails: "ops@lagetronix.com", CopyToCustomer: false);

        await Build(mail, settings).NotifySubscriptionActivatedAsync(SampleNotice());

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriptionActivated_SendsNothing_WhenNoRecipientsConfigured()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: "  ", CopyToCustomer: false, SignupAlert: true);

        await Build(mail, settings).NotifySubscriptionActivatedAsync(SampleNotice());

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscriptionActivated_SendsNothing_WhenSignupAlertToggleOff()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: "ops@lagetronix.com", CopyToCustomer: false,
            SignupAlert: false);

        await Build(mail, settings).NotifySubscriptionActivatedAsync(SampleNotice());

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Welcome_SendsToTheCustomer_WhenWelcomeEnabled()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: null, CopyToCustomer: false, Welcome: true);

        await Build(mail, settings).NotifyTenantWelcomeAsync(SampleNotice());

        await mail.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Contains("owner@acme.edu")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Welcome_SendsNothing_WhenCustomerEmailMissing()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: null, CopyToCustomer: false, Welcome: true);
        var notice = SampleNotice() with { CustomerEmail = null };

        await Build(mail, settings).NotifyTenantWelcomeAsync(notice);

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Welcome_SendsNothing_WhenWelcomeToggleOff()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var settings = new MarketplaceNotificationSettings(
            Enabled: true, FromEmail: null, ToEmails: null, CopyToCustomer: false, Welcome: false);

        await Build(mail, settings).NotifyTenantWelcomeAsync(SampleNotice());

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invite_SendsToInvitee_AndReportsDelivered()
    {
        var mail = Substitute.For<IGraphMailClient>();

        var delivered = await Build(mail, Settings(invite: true)).NotifyUserInvitedAsync(SampleInvite());

        Assert.True(delivered);
        await mail.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Contains("bursar@acme.edu")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invite_ReportsNotDelivered_WhenTransportThrows_AndDoesNotPropagate()
    {
        var mail = Substitute.For<IGraphMailClient>();
        mail.When(m => m.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("graph down"));

        var delivered = await Build(mail, Settings(invite: true)).NotifyUserInvitedAsync(SampleInvite());

        Assert.False(delivered);
    }

    [Fact]
    public async Task Invite_ReportsNotDelivered_AndSendsNothing_WhenInviteToggleOff()
    {
        var mail = Substitute.For<IGraphMailClient>();

        var delivered = await Build(mail, Settings(invite: false)).NotifyUserInvitedAsync(SampleInvite());

        Assert.False(delivered);
        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleChanged_SendsToTheResolvedMember_WhenRoleChangeEnabled()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var notice = new RoleChangedNotice(Email: "bursar@acme.edu", Role: "Billing-Accountant", TenantName: "Acme School");

        await Build(mail, Settings(roleChange: true)).NotifyRoleChangedAsync(notice);

        await mail.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To.Contains("bursar@acme.edu")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleChanged_SendsNothing_WhenMemberEmailUnknown()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var notice = new RoleChangedNotice(Email: null, Role: "Bursar", TenantName: "Acme School");

        await Build(mail, Settings(roleChange: true)).NotifyRoleChangedAsync(notice);

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleChanged_SendsNothing_WhenRoleChangeToggleOff()
    {
        var mail = Substitute.For<IGraphMailClient>();
        var notice = new RoleChangedNotice(Email: "bursar@acme.edu", Role: "Bursar", TenantName: "Acme School");

        await Build(mail, Settings(roleChange: false)).NotifyRoleChangedAsync(notice);

        await mail.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Welcome_BodyCarriesProductBrandingAndSignInLink()
    {
        var mail = Substitute.For<IGraphMailClient>();
        EmailMessage? sent = null;
        mail.When(m => m.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
            .Do(ci => sent = ci.Arg<EmailMessage>());
        var branding = new NotificationBrandingOptions { ProductName = "Edulynk", AppBaseUrl = "https://app.edulynk.io" };

        await Build(mail, Settings(welcome: true), branding).NotifyTenantWelcomeAsync(SampleNotice());

        Assert.NotNull(sent);
        Assert.Contains("Edulynk", sent!.Subject);
        Assert.Contains("https://app.edulynk.io", sent.HtmlBody);
    }

    [Fact]
    public async Task Invite_BodyCarriesTheSignInLink()
    {
        var mail = Substitute.For<IGraphMailClient>();
        EmailMessage? sent = null;
        mail.When(m => m.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
            .Do(ci => sent = ci.Arg<EmailMessage>());
        var branding = new NotificationBrandingOptions { ProductName = "Edulynk", AppBaseUrl = "https://app.edulynk.io" };

        await Build(mail, Settings(invite: true), branding).NotifyUserInvitedAsync(SampleInvite());

        Assert.NotNull(sent);
        Assert.Contains("https://app.edulynk.io", sent!.HtmlBody);
    }
}
