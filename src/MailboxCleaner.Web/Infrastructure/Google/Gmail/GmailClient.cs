using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using System.Collections.Concurrent;
using Google.Apis.Services;

namespace MailboxCleaner.Web.Infrastructure.Google.Gmail;

public sealed class GmailClient : IGmailClient
{
    private const int MaxConcurrency = 10;
    private const int PageSize = 500;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly IGmailCredentialFactory _credentialFactory;
    private IReadOnlyList<GmailMessageMetadata>? _metadataCache;
    private IReadOnlyList<GmailLabel>? _labelCache;
    private DateTimeOffset _metadataCacheExpiresAt;

    public GmailClient(IGmailCredentialFactory credentialFactory) => _credentialFactory = credentialFactory;

    public async Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken)
    {
        var metadata = await FetchMessageMetadataAsync(cancellationToken);
        return metadata.Select(item => item.FromHeader).Where(header => !string.IsNullOrWhiteSpace(header)).ToList();
    }

    public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken)
        => FetchMessageMetadataAsync(null, cancellationToken);

    public async Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(IProgress<GmailScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (_metadataCache is not null && _metadataCacheExpiresAt > DateTimeOffset.UtcNow)
        {
            progress?.Report(new GmailScanProgress(0, _metadataCache.Count, _metadataCache.Count, true));
            return _metadataCache;
        }

        var service = await CreateServiceAsync(cancellationToken);
        if (service is null)
        {
            return Array.Empty<GmailMessageMetadata>();
        }

        try
        {
            var allMessageIds = new List<string>();
            var pageNumber = 0;
            var listRequest = service.Users.Messages.List("me");
            listRequest.IncludeSpamTrash = false;
            listRequest.MaxResults = PageSize;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await ExecuteWithRetryAsync(() => listRequest.ExecuteAsync(cancellationToken), cancellationToken);
                pageNumber++;
                if (response.Messages is not null)
                {
                    allMessageIds.AddRange(response.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Id)).Select(m => m.Id));
                }

                progress?.Report(new GmailScanProgress(pageNumber, allMessageIds.Count, 0, false));
                listRequest.PageToken = response.NextPageToken;
            } while (!string.IsNullOrWhiteSpace(listRequest.PageToken));

            var uniqueIds = allMessageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Gmail METADATA format does not include MIME parts. Search message IDs
            // separately to classify attachments without downloading message bodies.
            var attachmentIds = await FetchAttachmentIdsAsync(service, cancellationToken);
            progress?.Report(new GmailScanProgress(pageNumber, uniqueIds.Count, 0, true));
            var scannedMessages = 0;
            var metadataItems = new ConcurrentBag<GmailMessageMetadata>();
            var failures = new ConcurrentBag<string>();
            await Parallel.ForEachAsync(
                uniqueIds,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = MaxConcurrency },
                async (messageId, token) =>
                {
                    try
                    {
                        var message = await FetchMetadataAsync(service, messageId, token);
                        var fromHeader = GetHeader(message, "From");
                        if (string.IsNullOrWhiteSpace(fromHeader))
                        {
                            fromHeader = "Unknown sender";
                        }

                        var subject = GetHeader(message, "Subject") ?? "(No subject)";
                        var dateValue = GetHeader(message, "Date");
                        var receivedAt = DateTimeOffset.TryParse(dateValue, out var parsedDate) ? parsedDate : (DateTimeOffset?)null;
                        var labels = message.LabelIds?.ToList() ?? new List<string>();
                        var listUnsubscribe = GetHeader(message, "List-Unsubscribe");
                        var precedence = GetHeader(message, "Precedence");
                        metadataItems.Add(new GmailMessageMetadata(message.Id ?? messageId, fromHeader, subject, receivedAt, labels.Contains("UNREAD", StringComparer.OrdinalIgnoreCase) is false, attachmentIds.Contains(messageId), labels, message.ThreadId, message.SizeEstimate, listUnsubscribe, precedence, DateTimeOffset.UtcNow));
                        var scanned = Interlocked.Increment(ref scannedMessages);
                        if (scanned % 25 == 0 || scanned == uniqueIds.Count)
                            progress?.Report(new GmailScanProgress(pageNumber, uniqueIds.Count, scanned, true));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add($"{messageId}: {ex.Message}");
                    }
                });

            if (!failures.IsEmpty)
            {
                throw new GmailOperationException(
                    $"Unable to load complete Gmail metadata. {failures.Count} message metadata request(s) failed; retry to avoid using an incomplete mailbox scan.",
                    new InvalidOperationException(string.Join("; ", failures.Take(5))));
            }

            _metadataCache = metadataItems.OrderByDescending(item => item.ReceivedAt).ToList();
            _metadataCacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _metadataCache;
        }
        catch (GmailOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GmailOperationException("Unable to load Gmail metadata. Please retry or sign in again.", ex);
        }
    }

    public async Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken)
    {
        if (_labelCache is not null)
        {
            return _labelCache;
        }

        var service = await CreateServiceAsync(cancellationToken);
        if (service is null)
        {
            return Array.Empty<GmailLabel>();
        }

        var response = await ExecuteWithRetryAsync(() => service.Users.Labels.List("me").ExecuteAsync(cancellationToken), cancellationToken);
        _labelCache = response.Labels?.Where(label => !string.IsNullOrWhiteSpace(label.Id) && !string.IsNullOrWhiteSpace(label.Name))
            .Select(label => new GmailLabel(label.Id, label.Name, label.Type?.Equals("system", StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(label => label.Name)
            .ToList() ?? new List<GmailLabel>();
        return _labelCache;
    }

    public Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ExecuteBatchAsync(messageIds, (service, id) => service.Users.Messages.Trash("me", id).ExecuteAsync(cancellationToken), cancellationToken);
    public Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: Array.Empty<string>(), removeLabels: ["INBOX"], cancellationToken);
    public Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: Array.Empty<string>(), removeLabels: ["UNREAD"], cancellationToken);
    public Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: ["UNREAD"], removeLabels: Array.Empty<string>(), cancellationToken);

    public async Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return GmailActionResult.Empty;
        var labelsById = (await FetchLabelsAsync(cancellationToken))
            .GroupBy(label => label.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Only fetch labels for the messages selected by the user, not the whole mailbox.
        return await ExecuteBatchAsync(messageIds, async (service, id) =>
        {
            var current = await FetchMetadataAsync(service, id, cancellationToken);
            var removeLabels = ResolveLabelsToRemoveForMove(current.LabelIds?.ToList() ?? new List<string>(), labelId, labelsById);
            var request = new ModifyMessageRequest { AddLabelIds = [labelId], RemoveLabelIds = removeLabels.ToList() };
            return await service.Users.Messages.Modify(request, "me", id).ExecuteAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken)
    {
        var service = await CreateRequiredServiceAsync(cancellationToken);
        var label = new Label { Name = labelName.Trim(), LabelListVisibility = "labelShow", MessageListVisibility = "show" };
        var created = await ExecuteWithRetryAsync(() => service.Users.Labels.Create(label, "me").ExecuteAsync(cancellationToken), cancellationToken);
        InvalidateCache();
        return new GmailLabel(created.Id, created.Name, false);
    }

    private async Task<GmailActionResult> ModifyMessagesAsync(IReadOnlyCollection<string> messageIds, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels, CancellationToken cancellationToken)
    {
        return await ExecuteBatchModifyAsync(messageIds, addLabels, removeLabels, cancellationToken);
    }


    private async Task<GmailActionResult> ExecuteBatchModifyAsync(IReadOnlyCollection<string> messageIds, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return GmailActionResult.Empty;
        var service = await CreateRequiredServiceAsync(cancellationToken);
        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var succeeded = new List<string>();
        var failed = new List<string>();
        var errors = new List<string>();
        try
        {
            foreach (var chunk in ids.Chunk(1000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkIds = chunk.ToList();
                var request = new BatchModifyMessagesRequest
                {
                    Ids = chunkIds,
                    AddLabelIds = addLabels.ToList(),
                    RemoveLabelIds = removeLabels.ToList()
                };

                try
                {
                    await ExecuteWithRetryAsync(() => service.Users.Messages.BatchModify(request, "me").ExecuteAsync(cancellationToken), cancellationToken);
                    succeeded.AddRange(chunkIds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.AddRange(chunkIds);
                    errors.Add(ex.Message);
                }
            }

            return new GmailActionResult(ids.Count, succeeded, failed, errors);
        }
        finally
        {
            InvalidateCache();
        }
    }

    private async Task<GmailActionResult> ExecuteBatchAsync<T>(IReadOnlyCollection<string> messageIds, Func<GmailService, string, Task<T>> action, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return GmailActionResult.Empty;
        var service = await CreateRequiredServiceAsync(cancellationToken);
        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var succeeded = new List<string>();
        var failed = new List<string>();
        var errors = new List<string>();
        try
        {
            foreach (var messageId in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ExecuteWithRetryAsync(() => action(service, messageId), cancellationToken);
                    succeeded.Add(messageId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add(messageId);
                    errors.Add($"{messageId}: {ex.Message}");
                }
            }

            return new GmailActionResult(ids.Count, succeeded, failed, errors);
        }
        finally
        {
            InvalidateCache();
        }
    }

    private async Task<GmailService?> CreateServiceAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentialFactory.CreateCredentialAsync(cancellationToken);
        if (credential is null) throw new GmailOperationException("Gmail token missing or expired. Please sign in again.", new InvalidOperationException("Missing Gmail token."));
        return new GmailService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "MailboxCleaner" });
    }

    private async Task<GmailService> CreateRequiredServiceAsync(CancellationToken cancellationToken) => await CreateServiceAsync(cancellationToken) ?? throw new GmailOperationException("Gmail token missing or expired. Please sign in again.", new InvalidOperationException("Missing Gmail token."));

    private static IReadOnlyCollection<string> ResolveLabelsToRemoveForMove(IReadOnlyCollection<string> currentLabels, string destinationLabelId, IReadOnlyDictionary<string, GmailLabel> labelsById) => currentLabels
        .Where(labelId => ShouldRemoveLabelForMove(labelId, destinationLabelId, labelsById))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static bool ShouldRemoveLabelForMove(string currentLabelId, string destinationLabelId, IReadOnlyDictionary<string, GmailLabel> labelsById)
    {
        if (currentLabelId.Equals(destinationLabelId, StringComparison.OrdinalIgnoreCase)) return false;
        if (currentLabelId.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return ShouldRemoveInboxForMove(destinationLabelId);
        if (labelsById.TryGetValue(currentLabelId, out var label)) return !label.IsSystemLabel;
        return !IsKnownSystemLabel(currentLabelId);
    }

    private static bool ShouldRemoveInboxForMove(string labelId) => !labelId.Equals("INBOX", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownSystemLabel(string labelId) => labelId.Equals("UNREAD", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("SENT", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("DRAFT", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("TRASH", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("SPAM", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("STARRED", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("IMPORTANT", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("CATEGORY_PERSONAL", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("CATEGORY_SOCIAL", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("CATEGORY_PROMOTIONS", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("CATEGORY_UPDATES", StringComparison.OrdinalIgnoreCase)
        || labelId.Equals("CATEGORY_FORUMS", StringComparison.OrdinalIgnoreCase);

    private static Task<Message> FetchMetadataAsync(GmailService service, string messageId, CancellationToken cancellationToken)
    {
        var getRequest = service.Users.Messages.Get("me", messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        getRequest.MetadataHeaders = new[] { "From", "Subject", "Date", "List-Unsubscribe", "Precedence" };
        getRequest.Fields = "id,threadId,labelIds,sizeEstimate,payload(headers(name,value),filename,body/attachmentId,parts(filename,body/attachmentId,parts)))";
        return ExecuteWithRetryAsync(() => getRequest.ExecuteAsync(cancellationToken), cancellationToken);
    }

    private static string? GetHeader(Message message, string name) => message.Payload?.Headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await operation(); }
            catch when (attempt < 3 && !cancellationToken.IsCancellationRequested) { await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken); }
        }
    }

    private void InvalidateCache()
    {
        _metadataCache = null; _labelCache = null; _metadataCacheExpiresAt = default;
    }

    private static async Task<HashSet<string>> FetchAttachmentIdsAsync(GmailService service, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var request = service.Users.Messages.List("me");
        request.Q = "has:attachment";
        request.IncludeSpamTrash = false;
        request.MaxResults = PageSize;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await ExecuteWithRetryAsync(() => request.ExecuteAsync(cancellationToken), cancellationToken);
            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                    if (!string.IsNullOrWhiteSpace(message.Id)) result.Add(message.Id);
            }
            request.PageToken = response.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(request.PageToken));
        return result;
    }
}
