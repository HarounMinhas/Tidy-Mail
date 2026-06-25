using Google.Apis.Auth.OAuth2;

namespace MailboxCleaner.Web.Infrastructure.Google.Gmail;

public interface IGmailCredentialFactory
{
    Task<UserCredential?> CreateCredentialAsync(CancellationToken cancellationToken);
}
