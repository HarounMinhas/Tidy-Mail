namespace MailboxCleaner.Web.Infrastructure.Google;

public interface IGmailClient
{
    Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken);
}
