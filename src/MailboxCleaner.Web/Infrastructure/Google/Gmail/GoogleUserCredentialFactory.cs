using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace MailboxCleaner.Web.Infrastructure.Google.Gmail;

public sealed class GoogleUserCredentialFactory : IGmailCredentialFactory
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    private readonly ITokenStore _tokenStore;
    private readonly GoogleOAuthOptions _options;

    public GoogleUserCredentialFactory(ITokenStore tokenStore, IOptions<GoogleOAuthOptions> options)
    {
        _tokenStore = tokenStore;
        _options = options.Value;
    }

    public async Task<UserCredential?> CreateCredentialAsync(CancellationToken cancellationToken)
    {
        var tokens = await _tokenStore.GetTokensAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken)) return null;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = _options.ClientId, ClientSecret = _options.ClientSecret },
            Scopes = _options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        });

        var token = new TokenResponse
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            IssuedUtc = DateTime.UtcNow,
            ExpiresInSeconds = tokens.ExpiresAt.HasValue ? Math.Max(0, (long)(tokens.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds) : null
        };
        var credential = new UserCredential(flow, "me", token);

        if (ShouldRefresh(tokens))
        {
            if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            {
                await _tokenStore.ClearAsync(cancellationToken);
                return null;
            }

            try
            {
                var refreshed = await credential.RefreshTokenAsync(cancellationToken);
                if (!refreshed || string.IsNullOrWhiteSpace(credential.Token.AccessToken))
                {
                    await _tokenStore.ClearAsync(cancellationToken);
                    return null;
                }

                var expiresAt = credential.Token.ExpiresInSeconds.HasValue
                    ? credential.Token.IssuedUtc.AddSeconds(credential.Token.ExpiresInSeconds.Value)
                    : (DateTimeOffset?)null;
                await _tokenStore.SaveTokensAsync(new TokenSet(
                    credential.Token.AccessToken,
                    string.IsNullOrWhiteSpace(credential.Token.RefreshToken) ? tokens.RefreshToken : credential.Token.RefreshToken,
                    expiresAt), cancellationToken);
            }
            catch (TokenResponseException ex) when (IsInvalidGrant(ex))
            {
                await _tokenStore.ClearAsync(cancellationToken);
                return null;
            }
        }

        return credential;
    }

    private static bool ShouldRefresh(TokenSet tokenSet)
        => tokenSet.ExpiresAt.HasValue && tokenSet.ExpiresAt.Value <= DateTimeOffset.UtcNow.Add(RefreshSkew);

    private static bool IsInvalidGrant(TokenResponseException ex)
        => string.Equals(ex.Error?.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);
}
