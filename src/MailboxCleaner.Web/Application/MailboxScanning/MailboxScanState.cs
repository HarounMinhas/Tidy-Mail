namespace MailboxCleaner.Web.Application.MailboxScanning;

public enum MailboxScanStatus
{
    NotStarted,
    Discovering,
    FetchingMetadata,
    Completed,
    Cancelled,
    Failed
}

public sealed record MailboxScanState
{
    public string ScanId { get; init; } = Guid.NewGuid().ToString("n");
    public string UserId { get; init; } = "session";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public string? LastPageToken { get; init; }
    public long? TotalDiscoveredMessages { get; init; }
    public int ScannedMessages { get; init; }
    public int CurrentPage { get; init; }
    public MailboxScanStatus Status { get; init; } = MailboxScanStatus.NotStarted;
    public string StatusMessage { get; init; } = "Ready to scan";
    public string? ErrorMessage { get; init; }
    public string? LastHistoryId { get; init; }
    public IReadOnlyList<MailboxMetadata> CachedMetadata { get; init; } = Array.Empty<MailboxMetadata>();

    public decimal ProgressPercent => TotalDiscoveredMessages is > 0
        ? Math.Min(100, Math.Round(ScannedMessages / (decimal)TotalDiscoveredMessages.Value * 100, 1))
        : Status == MailboxScanStatus.Completed ? 100 : 0;
}
