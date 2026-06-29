using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;

namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// Sends mail via Microsoft Graph <c>sendMail</c> as the configured shared mailbox, authenticating
/// app-only on the App Service managed identity (which holds the Graph <c>Mail.Send</c> app role).
/// The From and Reply-To are the shared mailbox; a flow only supplies recipients + content.
/// </summary>
public class GraphMailClient(GraphServiceClient graph, string sharedMailbox) : IGraphMailClient
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mail = new Message
        {
            Subject = message.Subject,
            Body = new ItemBody { ContentType = BodyType.Html, Content = message.HtmlBody },
            ToRecipients = message.To.Select(ToRecipient).ToList(),
            ReplyTo = new List<Recipient> { ToRecipient(sharedMailbox) },
        };

        if (message.Cc is { Count: > 0 })
        {
            mail.CcRecipients = message.Cc.Select(ToRecipient).ToList();
        }

        await graph.Users[sharedMailbox].SendMail.PostAsync(
            new SendMailPostRequestBody { Message = mail, SaveToSentItems = true },
            cancellationToken: cancellationToken);
    }

    private static Recipient ToRecipient(string address) =>
        new() { EmailAddress = new EmailAddress { Address = address } };
}

/// <summary>
/// Inert transport used when no shared mailbox is configured (email feature off). Lets callers depend
/// on <see cref="IGraphMailClient"/>/<see cref="IEmailSender"/> unconditionally without sending anything.
/// </summary>
public class NoOpGraphMailClient : IGraphMailClient
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
