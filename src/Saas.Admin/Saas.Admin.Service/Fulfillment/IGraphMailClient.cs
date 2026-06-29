namespace Saas.Admin.Service.Fulfillment;

/// <summary>
/// A composed email ready for transport. The sending identity (From) and Reply-To are pinned by the
/// transport to the shared mailbox, so an individual flow only specifies recipients + content.
/// </summary>
public record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<string>? Cc = null);

/// <summary>
/// Low-level transport: sends a composed <see cref="EmailMessage"/> via Microsoft Graph <c>sendMail</c>
/// as the configured shared mailbox (app-only, managed identity). This is the only Graph-touching seam,
/// which keeps the per-flow orchestration in <see cref="IEmailSender"/> unit-testable.
/// </summary>
public interface IGraphMailClient
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
