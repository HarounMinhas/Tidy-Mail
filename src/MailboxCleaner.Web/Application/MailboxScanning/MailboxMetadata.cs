namespace MailboxCleaner.Web.Application.MailboxScanning;

public sealed record MailboxMetadata(
    string MessageId,
    string ThreadId,
    string FromName,
    string FromEmail,
    string Domain,
    string Subject,
    DateTimeOffset? ReceivedAt,
    bool IsRead,
    IReadOnlyCollection<string> Labels,
    bool HasAttachment,
    long? SizeEstimate,
    DateTimeOffset ScannedAt,
    string? ListUnsubscribe = null,
    string? Precedence = null)
{
    public bool IsNoreply => FromEmail.Contains("noreply", StringComparison.OrdinalIgnoreCase)
        || FromEmail.Contains("no-reply", StringComparison.OrdinalIgnoreCase)
        || FromEmail.Contains("donotreply", StringComparison.OrdinalIgnoreCase);

    public bool IsNewsletterLike => !string.IsNullOrWhiteSpace(ListUnsubscribe)
        || string.Equals(Precedence, "bulk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Precedence, "list", StringComparison.OrdinalIgnoreCase);

    public bool IsNotificationLike => FromEmail.Contains("notification", StringComparison.OrdinalIgnoreCase)
        || FromEmail.Contains("notify", StringComparison.OrdinalIgnoreCase)
        || Subject.Contains("notification", StringComparison.OrdinalIgnoreCase)
        || Subject.Contains("alert", StringComparison.OrdinalIgnoreCase);
}
