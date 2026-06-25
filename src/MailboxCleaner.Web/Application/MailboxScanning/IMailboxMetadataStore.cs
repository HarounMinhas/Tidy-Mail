namespace MailboxCleaner.Web.Application.MailboxScanning;

public interface IMailboxMetadataStore
{
    Task<MailboxScanState?> GetCurrentScanAsync(string userId, CancellationToken cancellationToken);
    Task SaveScanStateAsync(MailboxScanState state, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailboxMetadata>> GetMetadataAsync(string userId, CancellationToken cancellationToken);
    Task UpsertMetadataAsync(string userId, IEnumerable<MailboxMetadata> metadata, CancellationToken cancellationToken);
    Task ReplaceMetadataAsync(string userId, IEnumerable<MailboxMetadata> metadata, CancellationToken cancellationToken);
    Task<bool> HasMessageAsync(string userId, string messageId, CancellationToken cancellationToken);
    Task ClearAsync(string userId, CancellationToken cancellationToken);
    Task UpdateAfterActionAsync(string userId, MailboxLocalAction action, IReadOnlyCollection<string> successfulMessageIds, string? labelId, CancellationToken cancellationToken);
    bool IsScanStale(MailboxScanState? state, TimeSpan maxAge);
}

public enum MailboxLocalAction
{
    Trash,
    Archive,
    MarkRead,
    MarkUnread,
    MoveToLabel
}
