using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace MailboxCleaner.Web.Infrastructure.Google;

public sealed class GoogleOAuthService : IGoogleOAuthService
{
    private readonly GoogleOAuthOptions _options;

    public GoogleOAuthService(IOptions<GoogleOAuthOptions> options)
    {
        _options = options.Value;
    }

    public string BuildAuthorizationUrl(string state)
    {
        var request = new GoogleAuthorizationCodeRequestUrl(new Uri(GoogleAuthConsts.AuthorizationUrl))
        {
            ClientId = _options.ClientId,
            RedirectUri = _options.RedirectUri,
            Scope = _options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            State = state,
            AccessType = "offline",
            IncludeGrantedScopes = true,
            Prompt = "consent"
        };

        return request.Build().AbsoluteUri;
    }

    public async Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = _options.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        });

        var token = await flow.ExchangeCodeForTokenAsync(
            userId: "me",
            code: code,
            redirectUri: _options.RedirectUri,
            cancellationToken: cancellationToken);

        return new TokenSet(token.AccessToken, token.RefreshToken ?? string.Empty, token.IssuedUtc?.AddSeconds(token.ExpiresInSeconds ?? 0));
    }
}
