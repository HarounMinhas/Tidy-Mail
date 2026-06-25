using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MailboxCleaner.Web.Application.MailboxScanning;

public interface IUserMailboxKeyProvider
{
    string GetCurrentUserKey();
}

public sealed class UserMailboxKeyProvider : IUserMailboxKeyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserMailboxKeyProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetCurrentUserKey()
    {
        var context = _httpContextAccessor.HttpContext;
        var user = context?.User;
        var claimValue = user?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user?.FindFirstValue(ClaimTypes.Email)
            ?? user?.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(claimValue) && !claimValue.Equals("google-user", StringComparison.OrdinalIgnoreCase))
        {
            return $"user:{claimValue.Trim().ToLowerInvariant()}";
        }

        var sessionId = GetSessionId(context);
        return string.IsNullOrWhiteSpace(sessionId) ? "anonymous" : $"session:{sessionId}";
    }

    private static string? GetSessionId(HttpContext? context)
    {
        var sessionFeature = context?.Features.Get<ISessionFeature>();
        return sessionFeature?.Session?.Id;
    }
}
