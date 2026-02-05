using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MailboxCleaner.Web.Infrastructure.Security;

public sealed class SessionTokenStore : ITokenStore
{
    private const string TokenKey = "gmail_tokens";
    private readonly ISession _session;
    private readonly IDataProtector _protector;

    public SessionTokenStore(IHttpContextAccessor httpContextAccessor, IDataProtectionProvider provider)
    {
        _session = httpContextAccessor.HttpContext?.Session
            ?? throw new InvalidOperationException("Session is not available.");
        _protector = provider.CreateProtector("MailboxCleaner.TokenStore");
    }

    public Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(tokenSet);
        var protectedPayload = _protector.Protect(payload);
        _session.SetString(TokenKey, protectedPayload);
        return Task.CompletedTask;
    }

    public Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var protectedPayload = _session.GetString(TokenKey);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return Task.FromResult<TokenSet?>(null);
        }

        var payload = _protector.Unprotect(protectedPayload);
        var tokenSet = JsonSerializer.Deserialize<TokenSet>(payload);
        return Task.FromResult<TokenSet?>(tokenSet);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _session.Remove(TokenKey);
        return Task.CompletedTask;
    }
}
