using MailboxCleaner.Web.Infrastructure.Security;

namespace MailboxCleaner.Web.Infrastructure.Google;

public interface IGoogleOAuthService
{
    string BuildAuthorizationUrl(string state);
    Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken);
}
