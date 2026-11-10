using System.Security.Cryptography;
using System.Text.Json;
using MailboxCleaner.Web.Application.MailboxScanning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace MailboxCleaner.Web.Infrastructure.Security;

/// <summary>
/// Server-side, account-scoped token cache. Unlike ASP.NET Session, it works during
/// Blazor Server circuit events. For multi-instance deployment replace the memory
/// cache with encrypted durable/distributed storage.
/// </summary>
public sealed class SessionTokenStore : ITokenStore
{
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;
    private readonly IUserMailboxKeyProvider _keyProvider;
    private readonly MailboxUserContext _mailboxUserContext;

    public SessionTokenStore(IMemoryCache cache, IDataProtectionProvider provider,
        IUserMailboxKeyProvider keyProvider, MailboxUserContext mailboxUserContext)
    {
        _cache = cache;
        _protector = provider.CreateProtector("MailboxCleaner.TokenStore.v2");
        _keyProvider = keyProvider;
        _mailboxUserContext = mailboxUserContext;
    }

    private string CacheKey
    {
        get
        {
            var account = _mailboxUserContext.CurrentUserKey ?? _keyProvider.GetCurrentUserKey();
            if (!account.StartsWith("user:", StringComparison.Ordinal) || account.Length <= 5)
            {
                throw new UnauthorizedAccessException("Cannot access Gmail tokens without a validated Google account.");
            }
            return $"gmail-token:{account}";
        }
    }

    public Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Set(CacheKey, _protector.Protect(JsonSerializer.Serialize(tokenSet)), TimeSpan.FromDays(30));
        return Task.CompletedTask;
    }

    public Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = CacheKey;
        if (!_cache.TryGetValue<string>(key, out var protectedPayload) || string.IsNullOrWhiteSpace(protectedPayload))
        {
            return Task.FromResult<TokenSet?>(null);
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<TokenSet>(_protector.Unprotect(protectedPayload)));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            _cache.Remove(key);
            return Task.FromResult<TokenSet?>(null);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Remove(CacheKey);
        return Task.CompletedTask;
    }
}
