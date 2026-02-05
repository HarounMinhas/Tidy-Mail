namespace MailboxCleaner.Web.Infrastructure.Security;

public sealed record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset? ExpiresAt);
