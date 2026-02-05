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
        var tokens = await _tokenStore.GetTokensAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return Array.Empty<string>();
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

        var fromHeaders = new List<string>();
        var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = allMessageIds.Select(async messageId =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var message = await FetchMetadataAsync(service, messageId, cancellationToken);
                var fromHeader = message?.Payload?.Headers?.FirstOrDefault(h => h.Name == "From")?.Value;
                if (!string.IsNullOrWhiteSpace(fromHeader))
                {
                    lock (fromHeaders)
                    {
                        fromHeaders.Add(fromHeader);
                    }
                }
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);

        return fromHeaders;
    }

    private static Task<Message> FetchMetadataAsync(GmailService service, string messageId, CancellationToken cancellationToken)
    {
        var getRequest = service.Users.Messages.Get("me", messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        getRequest.MetadataHeaders = new[] { "From" };
        return getRequest.ExecuteAsync(cancellationToken);
    }
}
