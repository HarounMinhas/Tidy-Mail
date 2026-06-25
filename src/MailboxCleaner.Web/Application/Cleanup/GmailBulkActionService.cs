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
                    var trashResult = await _gmailClient.TrashMessagesAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.Trash, trashResult.SucceededMessageIds, null, cancellationToken);
                    return ToBulkResult(ids.Count, trashResult);
                case CleanupBulkAction.Archive:
                    var archiveResult = await _gmailClient.ArchiveMessagesAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.Archive, archiveResult.SucceededMessageIds, null, cancellationToken);
                    return ToBulkResult(ids.Count, archiveResult);
                case CleanupBulkAction.MarkRead:
                    var readResult = await _gmailClient.MarkMessagesReadAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MarkRead, readResult.SucceededMessageIds, null, cancellationToken);
                    return ToBulkResult(ids.Count, readResult);
                case CleanupBulkAction.MarkUnread:
                    var unreadResult = await _gmailClient.MarkMessagesUnreadAsync(ids, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MarkUnread, unreadResult.SucceededMessageIds, null, cancellationToken);
                    return ToBulkResult(ids.Count, unreadResult);
                case CleanupBulkAction.MoveToLabel:
                    if (!string.IsNullOrWhiteSpace(newLabelName)) targetLabelId = (await _gmailClient.CreateLabelAsync(newLabelName, cancellationToken)).Id;
                    if (string.IsNullOrWhiteSpace(targetLabelId)) throw new InvalidOperationException("A Gmail label is required.");
                    var moveResult = await _gmailClient.MoveMessagesToLabelAsync(ids, targetLabelId, cancellationToken);
                    await _store.UpdateAfterActionAsync(userId, MailboxLocalAction.MoveToLabel, moveResult.SucceededMessageIds, targetLabelId, cancellationToken);
                    return ToBulkResult(ids.Count, moveResult);
            }
            return GmailBulkActionResult.Success(ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GmailBulkActionResult(ids.Count, 0, ids.Count, ids, [ex.Message]);
        }
    }

    private static GmailBulkActionResult ToBulkResult(int requested, GmailActionResult result)
        => new(requested, result.TotalSucceeded, result.TotalFailed, result.FailedMessageIds, result.ErrorMessages);

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
