using Google;
using System.Net;

namespace MailboxCleaner.Web.Infrastructure.Google;

// Only fixed text, HTTP codes and recognized Google error reasons leave the server.
// Exception messages, request URLs, IDs, tokens and mail headers are never diagnostic data.
internal static class GmailScanDiagnostics
{
    private static readonly HashSet<string> KnownReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "badRequest", "invalidArgument", "invalid", "invalidFieldSelection", "authError",
        "insufficientPermissions", "forbidden", "notFound", "rateLimitExceeded",
        "userRateLimitExceeded", "quotaExceeded", "dailyLimitExceeded", "backendError",
        "accessNotConfigured", "serviceDisabled", "limitExceeded"
    };

    internal static bool IsRetryable(GoogleApiException api) =>
        api.HttpStatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        || api.HttpStatusCode == HttpStatusCode.Forbidden
            && api.Error?.Errors?.Any(error => error.Reason is "rateLimitExceeded" or "userRateLimitExceeded") == true;

    internal static bool IsPermanentRequestFailure(GoogleApiException api) =>
        (api.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        && !IsRetryable(api);

    internal static string Describe(Exception exception)
    {
        if (exception is GoogleApiException api)
        {
            var reason = api.Error?.Errors?.Select(error => error.Reason)
                .FirstOrDefault(value => value is not null && KnownReasons.Contains(value));
            var code = $"Gmail API HTTP {(int)api.HttpStatusCode}" + (reason is null ? "." : $" ({reason}).");
            return code + (api.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized => " Please sign in again.",
                HttpStatusCode.Forbidden when !IsRetryable(api) => " Check Gmail permissions or quota; if access was revoked, sign in again.",
                HttpStatusCode.BadRequest => " Gmail rejected the request parameters.",
                _ when IsRetryable(api) => " Temporary Gmail API failure or rate limit.",
                _ => string.Empty
            });
        }
        if (exception is HttpRequestException) return "Gmail network request failed.";
        if (exception is OperationCanceledException) return "Gmail request timed out.";
        if (exception is Google.Apis.Auth.OAuth2.Responses.TokenResponseException) return "Google authorization failed. Please sign in again.";
        return "Unexpected scan error. Check the application server diagnostics.";
    }
}
