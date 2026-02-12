namespace MailboxCleaner.Web.Infrastructure.Google;

public sealed record GmailMessageMetadata(
    string Id,
    string FromHeader,
    string Subject,
    DateTimeOffset? ReceivedAt,
    bool IsRead);
