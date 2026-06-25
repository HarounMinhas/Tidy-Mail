using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using MailboxCleaner.Web.Infrastructure.Security;

namespace MailboxCleaner.Web.Infrastructure.Google.Gmail;

public sealed class GmailClient : IGmailClient
{
    private const int MaxConcurrency = 8;
    private const int PageSize = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private readonly ITokenStore _tokenStore;
    private IReadOnlyList<GmailMessageMetadata>? _metadataCache;
    private IReadOnlyList<GmailLabel>? _labelCache;
    private DateTimeOffset _metadataCacheExpiresAt;

    public GmailClient(ITokenStore tokenStore) => _tokenStore = tokenStore;

    public async Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken)
    {
        var metadata = await FetchMessageMetadataAsync(cancellationToken);
        return metadata.Select(item => item.FromHeader).Where(header => !string.IsNullOrWhiteSpace(header)).ToList();
    }

    public async Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken)
    {
        if (_metadataCache is not null && _metadataCacheExpiresAt > DateTimeOffset.UtcNow)
        {
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
            var listRequest = service.Users.Messages.List("me");
            listRequest.IncludeSpamTrash = false;
            listRequest.MaxResults = PageSize;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await ExecuteWithRetryAsync(() => listRequest.ExecuteAsync(cancellationToken), cancellationToken);
                if (response.Messages is not null)
                {
                    allMessageIds.AddRange(response.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Id)).Select(m => m.Id));
                }

                listRequest.PageToken = response.NextPageToken;
            } while (!string.IsNullOrWhiteSpace(listRequest.PageToken));

            var metadataItems = new List<GmailMessageMetadata>(allMessageIds.Count);
            using var throttler = new SemaphoreSlim(MaxConcurrency);
            var tasks = allMessageIds.Select(async messageId =>
            {
                await throttler.WaitAsync(cancellationToken);
                try
                {
                    var message = await FetchMetadataAsync(service, messageId, cancellationToken);
                    var fromHeader = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "From")?.Value;
                    if (string.IsNullOrWhiteSpace(fromHeader))
                    {
                        return;
                    }

                    var subject = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(No subject)";
                    var dateValue = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Date")?.Value;
                    var receivedAt = DateTimeOffset.TryParse(dateValue, out var parsedDate) ? parsedDate : (DateTimeOffset?)null;
                    var labels = message.LabelIds?.ToList() ?? new List<string>();
                    var item = new GmailMessageMetadata(message.Id ?? messageId, fromHeader, subject, receivedAt, labels.Contains("UNREAD", StringComparer.OrdinalIgnoreCase) is false, ContainsAttachment(message.Payload), labels);
                    lock (metadataItems)
                    {
                        metadataItems.Add(item);
                    }
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            _metadataCache = metadataItems.OrderByDescending(item => item.ReceivedAt).ToList();
            _metadataCacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
            return _metadataCache;
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
            .ToList() ?? Array.Empty<GmailLabel>();
        return _labelCache;
    }

    public Task TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ExecuteBatchAsync(messageIds, (service, id) => service.Users.Messages.Trash("me", id).ExecuteAsync(cancellationToken), cancellationToken);
    public Task ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: Array.Empty<string>(), removeLabels: ["INBOX"], cancellationToken);
    public Task MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: Array.Empty<string>(), removeLabels: ["UNREAD"], cancellationToken);
    public Task MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => ModifyMessagesAsync(messageIds, addLabels: ["UNREAD"], removeLabels: Array.Empty<string>(), cancellationToken);

    public async Task MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken)
    {
        var metadataById = (await FetchMessageMetadataAsync(cancellationToken))
            .GroupBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var labelsById = (await FetchLabelsAsync(cancellationToken))
            .GroupBy(label => label.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        await ExecuteBatchAsync(messageIds, (service, id) =>
        {
            var removeLabels = ResolveLabelsToRemoveForMove(metadataById.TryGetValue(id, out var metadata) ? metadata.Labels : Array.Empty<string>(), labelId, labelsById);
            var request = new ModifyMessageRequest { AddLabelIds = [labelId], RemoveLabelIds = removeLabels.ToList() };
            return service.Users.Messages.Modify(request, "me", id).ExecuteAsync(cancellationToken);
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

    private async Task ModifyMessagesAsync(IReadOnlyCollection<string> messageIds, IReadOnlyCollection<string> addLabels, IReadOnlyCollection<string> removeLabels, CancellationToken cancellationToken)
    {
        var request = new ModifyMessageRequest { AddLabelIds = addLabels.ToList(), RemoveLabelIds = removeLabels.ToList() };
        await ExecuteBatchAsync(messageIds, (service, id) => service.Users.Messages.Modify(request, "me", id).ExecuteAsync(cancellationToken), cancellationToken);
    }

    private async Task ExecuteBatchAsync<T>(IReadOnlyCollection<string> messageIds, Func<GmailService, string, Task<T>> action, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return;
        var service = await CreateRequiredServiceAsync(cancellationToken);
        try
        {
            foreach (var messageId in messageIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteWithRetryAsync(() => action(service, messageId), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new GmailOperationException("Gmail action failed. Gmail metadata cache was invalidated before reporting this error.", ex);
        }
        finally
        {
            InvalidateCache();
        }
    }

    private async Task<GmailService?> CreateServiceAsync(CancellationToken cancellationToken)
    {
        var tokens = await _tokenStore.GetTokensAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken)) return null;
        return new GmailService(new BaseClientService.Initializer { HttpClientInitializer = GoogleCredential.FromAccessToken(tokens.AccessToken), ApplicationName = "MailboxCleaner" });
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
        getRequest.MetadataHeaders = new[] { "From", "Subject", "Date" };
        return ExecuteWithRetryAsync(() => getRequest.ExecuteAsync(cancellationToken), cancellationToken);
    }

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

    private static bool ContainsAttachment(MessagePart? payload) => payload is not null && (!string.IsNullOrWhiteSpace(payload.Filename) || !string.IsNullOrWhiteSpace(payload.Body?.AttachmentId) || payload.Parts?.Any(ContainsAttachment) == true);
}
