using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Infrastructure.Demo;

/// <summary>
/// IGmailClient double that runs the real application workflow against synthetic
/// account-scoped data, including label mutations and incremental history sync.
/// </summary>
public sealed class DemoGmailClient : IGmailClient
{
    private readonly DemoMailboxStore _store;
    private readonly MailboxUserContext _userContext;
    private readonly IUserMailboxKeyProvider _userKeyProvider;

    public DemoGmailClient(DemoMailboxStore store, MailboxUserContext userContext,
        IUserMailboxKeyProvider userKeyProvider)
    {
        _store = store;
        _userContext = userContext;
        _userKeyProvider = userKeyProvider;
    }

    private DemoMailboxStore.DemoMailbox Mailbox
    {
        get
        {
            var key = _userContext.CurrentUserKey ?? _userKeyProvider.GetCurrentUserKey();
            return _store.GetMailbox(key);
        }
    }

    public async Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken)
        => (await FetchMessageMetadataAsync(cancellationToken)).Select(message => message.FromHeader).ToList();

    public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken)
        => FetchMessageMetadataAsync(false, null, cancellationToken);

    public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(
        IProgress<GmailScanProgress>? progress, CancellationToken cancellationToken)
        => FetchMessageMetadataAsync(false, progress, cancellationToken);

    public async Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(
        bool forceRefresh, IProgress<GmailScanProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mailbox = Mailbox;
        List<GmailMessageMetadata> messages;
        lock (mailbox.SyncRoot)
            messages = mailbox.Messages.Values.Where(IsVisible)
                .OrderByDescending(message => message.ReceivedAt).ToList();

        progress?.Report(new GmailScanProgress(1, messages.Count, 0, false));
        progress?.Report(new GmailScanProgress(1, messages.Count, 0, true));
        for (var completed = 0; completed < messages.Count; completed += 12)
        {
            await Task.Delay(10, cancellationToken);
            progress?.Report(new GmailScanProgress(1, messages.Count,
                Math.Min(messages.Count, completed + 12), true));
        }

        return messages;
    }

    public Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mailbox = Mailbox;
        lock (mailbox.SyncRoot)
            return Task.FromResult<IReadOnlyList<GmailLabel>>(mailbox.Labels.Values
                .OrderBy(label => label.Name).ToList());
    }

    public Task<string?> GetMailboxHistoryIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mailbox = Mailbox;
        lock (mailbox.SyncRoot)
            return Task.FromResult<string?>(mailbox.Revision.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }

    public Task<GmailHistoryDelta?> FetchHistoryDeltaAsync(
        string startHistoryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!long.TryParse(startHistoryId, out var fromRevision))
            return Task.FromResult<GmailHistoryDelta?>(null);

        var mailbox = Mailbox;
        lock (mailbox.SyncRoot)
        {
            if (fromRevision < 1_000 || fromRevision > mailbox.Revision)
                return Task.FromResult<GmailHistoryDelta?>(null);

            var changedIds = mailbox.Changes.Where(change => change.Revision > fromRevision)
                .Select(change => change.MessageId)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var updated = new List<GmailMessageMetadata>();
            var deleted = new List<string>();
            foreach (var id in changedIds)
            {
                if (!mailbox.Messages.TryGetValue(id, out var message) || !IsVisible(message))
                    deleted.Add(id);
                else
                    updated.Add(message);
            }

            return Task.FromResult<GmailHistoryDelta?>(new GmailHistoryDelta(
                mailbox.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                updated, deleted));
        }
    }

    public Task<GmailActionResult> TrashMessagesAsync(
        IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
        => ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            labels.Remove("INBOX");
            labels.Add("TRASH");
            return WithLabels(message, labels);
        }, cancellationToken);

    public Task<GmailActionResult> ArchiveMessagesAsync(
        IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
        => ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            labels.Remove("INBOX");
            return WithLabels(message, labels);
        }, cancellationToken);

    public Task<GmailActionResult> MarkMessagesReadAsync(
        IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
        => ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            labels.Remove("UNREAD");
            return WithLabels(message, labels);
        }, cancellationToken);

    public Task<GmailActionResult> MarkMessagesUnreadAsync(
        IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken)
        => ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            labels.Add("UNREAD");
            return WithLabels(message, labels);
        }, cancellationToken);

    public Task<GmailActionResult> AddLabelToMessagesAsync(
        IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken)
    {
        ValidateUserLabel(labelId);
        return ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            labels.Add(labelId);
            return WithLabels(message, labels);
        }, cancellationToken);
    }

    public Task<GmailActionResult> MoveMessagesToLabelAsync(
        IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken)
    {
        ValidateUserLabel(labelId);
        return ApplyAsync(messageIds, message =>
        {
            var labels = Labels(message);
            foreach (var existing in labels.ToList())
            {
                if (existing.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                    || existing.StartsWith("Label_", StringComparison.OrdinalIgnoreCase))
                    labels.Remove(existing);
            }
            labels.Add(labelId);
            return WithLabels(message, labels);
        }, cancellationToken);
    }

    public Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = labelName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A new label needs a name.", nameof(labelName));

        var mailbox = Mailbox;
        lock (mailbox.SyncRoot)
        {
            if (mailbox.Labels.Values.Any(label =>
                label.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Label '{name}' already exists.");

            var label = new GmailLabel($"Label_DemoCustom_{mailbox.NextLabelId++:000}", name, false);
            mailbox.Labels[label.Id] = label;
            return Task.FromResult(label);
        }
    }

    private void ValidateUserLabel(string labelId)
    {
        var mailbox = Mailbox;
        lock (mailbox.SyncRoot)
        {
            if (!mailbox.Labels.TryGetValue(labelId, out var label) || label.IsSystemLabel)
                throw new ArgumentException("Select a valid user label.", nameof(labelId));
        }
    }

    private Task<GmailActionResult> ApplyAsync(IReadOnlyCollection<string> messageIds,
        Func<GmailMessageMetadata, GmailMessageMetadata> mutation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = messageIds.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested.Count == 0) return Task.FromResult(GmailActionResult.Empty);

        var succeeded = new List<string>();
        var failed = new List<string>();
        var unknown = new List<string>();
        var errors = new List<string>();
        var mailbox = Mailbox;

        lock (mailbox.SyncRoot)
        {
            foreach (var id in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!mailbox.Messages.TryGetValue(id, out var current) || !IsVisible(current))
                {
                    failed.Add(id);
                    errors.Add($"{id}: message not found in this mailbox.");
                    continue;
                }

                mailbox.Failures.TryGetValue(id, out var failure);
                if (failure == DemoFailure.Rejected)
                {
                    failed.Add(id);
                    errors.Add($"{id}: simulated Gmail rejection.");
                    continue;
                }

                var updated = mutation(current);
                mailbox.Messages[id] = updated;
                mailbox.RecordChange(id);
                if (failure == DemoFailure.UncertainAfterApplying)
                {
                    unknown.Add(id);
                    errors.Add($"{id}: simulated network timeout after Gmail applied the change.");
                }
                else
                {
                    succeeded.Add(id);
                }
            }
        }

        return Task.FromResult(new GmailActionResult(requested.Count,
            succeeded, failed, errors, unknown));
    }

    private static bool IsVisible(GmailMessageMetadata message)
        => !message.Labels.Contains("TRASH", StringComparer.OrdinalIgnoreCase)
           && !message.Labels.Contains("SPAM", StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Labels(GmailMessageMetadata message)
        => message.Labels.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static GmailMessageMetadata WithLabels(
        GmailMessageMetadata message, HashSet<string> labels)
        => message with
        {
            Labels = labels.ToList(),
            IsRead = !labels.Contains("UNREAD"),
            ScannedAt = DateTimeOffset.UtcNow
        };
}
