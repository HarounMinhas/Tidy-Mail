namespace MailboxCleaner.Web.Infrastructure.Google;

public sealed record GmailMessageMetadata(
    string Id,
    string FromHeader,
    string Subject,
    DateTimeOffset? ReceivedAt,
    bool IsRead,
    bool HasAttachment,
    IReadOnlyCollection<string> Labels,
    string? ThreadId = null,
    long? SizeEstimate = null,
    string? ListUnsubscribe = null,
    string? Precedence = null,
    DateTimeOffset? ScannedAt = null);
