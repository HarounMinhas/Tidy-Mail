using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Application.Cleanup;

public enum CleanupBulkAction
{
    Trash,
    Archive,
    MarkRead,
    MarkUnread,
    MoveToLabel
}

public sealed record BulkActionPreview(
    CleanupBulkAction Action,
    int AffectedMessageCount,
    IReadOnlyList<AffectedSenderPreview> TopAffectedSenders,
    IReadOnlyList<MailboxMetadata> SampleMessages,
    string? RiskWarning,
    string? TargetLabelId,
    IReadOnlyList<string> MessageIds)
{
    public bool CanConfirm => AffectedMessageCount > 0;
}

public sealed record AffectedSenderPreview(string SenderName, string SenderEmail, int Count);

public sealed record GmailBulkActionResult(int TotalRequested, int TotalSucceeded, int TotalFailed, IReadOnlyList<string> FailedMessageIds, IReadOnlyList<string> ErrorMessages)
{
    public bool IsPartialSuccess => TotalSucceeded > 0 && TotalFailed > 0;
    public static GmailBulkActionResult Empty { get; } = new(0, 0, 0, Array.Empty<string>(), Array.Empty<string>());
    public static GmailBulkActionResult Success(IReadOnlyCollection<string> ids) => new(ids.Count, ids.Count, 0, Array.Empty<string>(), Array.Empty<string>());
}

public sealed class BulkActionPreviewService
{
    public BulkActionPreview Build(CleanupBulkAction action, IEnumerable<MailboxMetadata> metadata, IReadOnlyCollection<string> selectedIds, string? labelId = null)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var impacted = metadata.Where(m => selected.Contains(m.MessageId)).ToList();
        var warning = action == CleanupBulkAction.Trash ? "Trash moves messages out of the active mailbox. Review the sample before confirming." : null;
        var topSenders = impacted.GroupBy(m => m.FromEmail, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AffectedSenderPreview(g.First().FromName, g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .Take(5)
            .ToList();
        return new BulkActionPreview(action, impacted.Count, topSenders, impacted.Take(10).ToList(), warning, labelId, impacted.Select(m => m.MessageId).ToList());
    }
}
