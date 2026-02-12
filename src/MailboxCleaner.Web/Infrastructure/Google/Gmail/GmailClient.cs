using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Auth.OAuth2;
using MailboxCleaner.Web.Infrastructure.Security;

namespace MailboxCleaner.Web.Infrastructure.Google.Gmail;

public sealed class GmailClient : IGmailClient
{
    private const int MaxConcurrency = 10;
    private readonly ITokenStore _tokenStore;

    public GmailClient(ITokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public async Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken)
    {
        var metadata = await FetchMessageMetadataAsync(cancellationToken);
        return metadata.Select(item => item.FromHeader)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToList();
    }

    public async Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken)
    {
        var tokens = await _tokenStore.GetTokensAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return Array.Empty<GmailMessageMetadata>();
        }

        var credential = GoogleCredential.FromAccessToken(tokens.AccessToken);
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "MailboxCleaner"
        });

        var allMessageIds = new List<string>();
        var listRequest = service.Users.Messages.List("me");
        listRequest.IncludeSpamTrash = false;

        do
        {
            var response = await listRequest.ExecuteAsync(cancellationToken);
            if (response.Messages != null)
            {
                allMessageIds.AddRange(response.Messages.Select(m => m.Id));
            }

            listRequest.PageToken = response.NextPageToken;
        } while (!string.IsNullOrWhiteSpace(listRequest.PageToken));

        var metadataItems = new List<GmailMessageMetadata>();
        var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = allMessageIds.Select(async messageId =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var message = await FetchMetadataAsync(service, messageId, cancellationToken);
                var fromHeader = message?.Payload?.Headers?.FirstOrDefault(h => h.Name == "From")?.Value;
                if (!string.IsNullOrWhiteSpace(fromHeader) && message is not null)
                {
                    var subject = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(No subject)";
                    var dateValue = message.Payload?.Headers?.FirstOrDefault(h => h.Name == "Date")?.Value;
                    var receivedAt = DateTimeOffset.TryParse(dateValue, out var parsedDate) ? parsedDate : null;
                    var isRead = message.LabelIds?.Contains("UNREAD") != true;

                    lock (metadataItems)
                    {
                        metadataItems.Add(new GmailMessageMetadata(message.Id ?? messageId, fromHeader, subject, receivedAt, isRead));
                    }
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);

        return metadataItems;
    }

    private static Task<Message> FetchMetadataAsync(GmailService service, string messageId, CancellationToken cancellationToken)
    {
        var getRequest = service.Users.Messages.Get("me", messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        getRequest.MetadataHeaders = new[] { "From", "Subject", "Date" };
        return getRequest.ExecuteAsync(cancellationToken);
    }
}
