namespace MailboxCleaner.Web.Infrastructure.Google;

public interface IGmailClient
{
    Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken);
    Task TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken);
    Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken);
}

public sealed record GmailLabel(string Id, string Name, bool IsSystemLabel);
