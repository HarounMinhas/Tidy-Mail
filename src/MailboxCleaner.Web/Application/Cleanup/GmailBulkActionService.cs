using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.Services;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.Cleanup;

public sealed class GmailBulkActionService
{
    private readonly IGmailClient _gmailClient;
    private readonly IMailboxMetadataStore _store;

    public GmailBulkActionService(IGmailClient gmailClient, IMailboxMetadataStore store)
    {
        _gmailClient = gmailClient;
        _store = store;
    }

    public async Task<GmailBulkActionResult> ApplyAsync(string userId, CleanupBulkAction action, IReadOnlyCollection<string> messageIds, string? labelId, string? newLabelName, CancellationToken cancellationToken)
    {
        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return GmailBulkActionResult.Empty;

        var targetLabelId = labelId;
        try
        {
            switch (action)
            {
                case CleanupBulkAction.Trash:
                    await _gmailClient.TrashMessagesAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.Trash, ids, null, cancellationToken);
                    break;
                case CleanupBulkAction.Archive:
                    await _gmailClient.ArchiveMessagesAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.Archive, ids, null, cancellationToken);
                    break;
                case CleanupBulkAction.MarkRead:
                    await _gmailClient.MarkMessagesReadAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MarkRead, ids, null, cancellationToken);
                    break;
                case CleanupBulkAction.MarkUnread:
                    await _gmailClient.MarkMessagesUnreadAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MarkUnread, ids, null, cancellationToken);
                    break;
                case CleanupBulkAction.MoveToLabel:
                    if (!string.IsNullOrWhiteSpace(newLabelName)) targetLabelId = (await _gmailClient.CreateLabelAsync(newLabelName, cancellationToken)).Id;
                    if (string.IsNullOrWhiteSpace(targetLabelId)) throw new InvalidOperationException("A Gmail label is required.");
                    await _gmailClient.MoveMessagesToLabelAsync(ids, targetLabelId, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MoveToLabel, ids, targetLabelId, cancellationToken);
                    break;
            }
            return GmailBulkActionResult.Success(ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GmailBulkActionResult(ids.Count, 0, ids.Count, ids, [ex.Message]);
        }
    }

    public static CleanupBulkAction FromLegacyAction(MailBulkAction action) => action switch
    {
        MailBulkAction.Delete => CleanupBulkAction.Trash,
        MailBulkAction.Archive => CleanupBulkAction.Archive,
        MailBulkAction.MarkRead => CleanupBulkAction.MarkRead,
        MailBulkAction.MarkUnread => CleanupBulkAction.MarkUnread,
        MailBulkAction.Move => CleanupBulkAction.MoveToLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
