using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.Services;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.Cleanup;

/// <summary>Applies one user-confirmed Gmail operation and preserves outcomes even if the local cache fails.</summary>
public sealed class GmailBulkActionService
{
    private readonly IGmailClient _gmailClient;
    private readonly IMailboxMetadataStore _store;
    private readonly MailboxOperationCoordinator _coordinator;

    public GmailBulkActionService(IGmailClient gmailClient, IMailboxMetadataStore store,
        MailboxOperationCoordinator? coordinator = null)
    {
        _gmailClient = gmailClient;
        _store = store;
        _coordinator = coordinator ?? new MailboxOperationCoordinator();
    }

    public async Task<GmailBulkActionResult> ApplyAsync(string userId, CleanupBulkAction action,
        IReadOnlyCollection<string> messageIds, string? labelId, string? newLabelName, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported Gmail bulk action.");

        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return GmailBulkActionResult.Empty;
        using var operation = await _coordinator.EnterAsync(userId, cancellationToken);

        var targetLabelId = labelId;
        var actionRequiresLabel = action is CleanupBulkAction.MoveToLabel or CleanupBulkAction.AddLabel;
        GmailActionResult gmailResult;
        try
        {
            if (actionRequiresLabel)
            {
                if (!string.IsNullOrWhiteSpace(newLabelName))
                    targetLabelId = (await _gmailClient.CreateLabelAsync(newLabelName.Trim(), cancellationToken)).Id;
                if (string.IsNullOrWhiteSpace(targetLabelId))
                    throw new ArgumentException("Choose an existing Gmail label or provide a new label name.");
            }

            gmailResult = action switch
            {
                CleanupBulkAction.Trash => await _gmailClient.TrashMessagesAsync(ids, cancellationToken),
                CleanupBulkAction.Archive => await _gmailClient.ArchiveMessagesAsync(ids, cancellationToken),
                CleanupBulkAction.MarkRead => await _gmailClient.MarkMessagesReadAsync(ids, cancellationToken),
                CleanupBulkAction.MarkUnread => await _gmailClient.MarkMessagesUnreadAsync(ids, cancellationToken),
                CleanupBulkAction.MoveToLabel => await _gmailClient.MoveMessagesToLabelAsync(ids, targetLabelId!, cancellationToken),
                CleanupBulkAction.AddLabel => await _gmailClient.AddLabelToMessagesAsync(ids, targetLabelId!, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An interrupted request might have reached Gmail. Do NOT classify it as
            // a definitive failure or offer an automatic retry.
            return new GmailBulkActionResult(ids.Count, 0, 0, Array.Empty<string>(),
                [ex.Message], ids, targetLabelId);
        }

        var cacheUpdateFailed = false;
        var errors = gmailResult.ErrorMessages.ToList();
        if (gmailResult.SucceededMessageIds.Count > 0)
        {
            try
            {
                var localAction = action switch
                {
                    CleanupBulkAction.Trash => MailboxLocalAction.Trash,
                    CleanupBulkAction.Archive => MailboxLocalAction.Archive,
                    CleanupBulkAction.MarkRead => MailboxLocalAction.MarkRead,
                    CleanupBulkAction.MarkUnread => MailboxLocalAction.MarkUnread,
                    CleanupBulkAction.MoveToLabel => MailboxLocalAction.MoveToLabel,
                    CleanupBulkAction.AddLabel => MailboxLocalAction.AddLabel,
                    _ => throw new ArgumentOutOfRangeException(nameof(action))
                };
                await _store.UpdateAfterActionAsync(userId, localAction,
                    gmailResult.SucceededMessageIds, targetLabelId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Gmail already acknowledged these writes; failure to update SQLite
                // cannot turn their outcome into "0 succeeded".
                cacheUpdateFailed = true;
                errors.Add($"Gmail accepted the operation but local metadata refresh failed: {ex.Message}");
            }
        }

        return new GmailBulkActionResult(ids.Count, gmailResult.TotalSucceeded, gmailResult.TotalFailed,
            gmailResult.FailedMessageIds, errors, gmailResult.UnknownMessageIds,
            targetLabelId, cacheUpdateFailed);
    }

    public static CleanupBulkAction FromLegacyAction(MailBulkAction action) => action switch
    {
        MailBulkAction.Delete => CleanupBulkAction.Trash,
        MailBulkAction.Archive => CleanupBulkAction.Archive,
        MailBulkAction.MarkRead => CleanupBulkAction.MarkRead,
        MailBulkAction.MarkUnread => CleanupBulkAction.MarkUnread,
        MailBulkAction.Move => CleanupBulkAction.MoveToLabel,
        MailBulkAction.AddLabel => CleanupBulkAction.AddLabel,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
