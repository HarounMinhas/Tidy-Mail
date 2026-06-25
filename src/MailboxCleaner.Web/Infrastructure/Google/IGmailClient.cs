namespace MailboxCleaner.Web.Infrastructure.Google;

public interface IGmailClient
{
    Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken);
    Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken);
    Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken);
    Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken);
}

public sealed record GmailLabel(string Id, string Name, bool IsSystemLabel);

public sealed record GmailActionResult(int TotalRequested, IReadOnlyList<string> SucceededMessageIds, IReadOnlyList<string> FailedMessageIds, IReadOnlyList<string> ErrorMessages)
{
    public int TotalSucceeded => SucceededMessageIds.Count;
    public int TotalFailed => FailedMessageIds.Count;
    public bool HasFailures => TotalFailed > 0;
    public static GmailActionResult Empty { get; } = new(0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
    public static GmailActionResult Success(IReadOnlyCollection<string> ids) => new(ids.Count, ids.ToList(), Array.Empty<string>(), Array.Empty<string>());
}
