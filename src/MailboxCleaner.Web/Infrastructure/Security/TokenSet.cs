namespace MailboxCleaner.Web.Infrastructure.Security;

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset? ExpiresAt, string? IdToken = null)
{
    // Google may return no refresh token when signing in to an already authorized account.
    // Retain only the token stored for that same, validated Google subject.
    public TokenSet PreserveExistingRefreshToken(TokenSet? sameAccountTokens) =>
        string.IsNullOrWhiteSpace(RefreshToken) && !string.IsNullOrWhiteSpace(sameAccountTokens?.RefreshToken)
            ? this with { RefreshToken = sameAccountTokens.RefreshToken }
            : this;
}

