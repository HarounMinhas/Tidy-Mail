namespace MailboxCleaner.Web.Infrastructure.Security;

public interface ITokenStore
{
    Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken);
    Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
