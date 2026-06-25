using System.Security.Claims;
using Microsoft.AspNetCore.Http;

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

        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            return $"user:{claimValue.Trim().ToLowerInvariant()}";
        }

        var sessionId = context?.Session?.Id;
        return string.IsNullOrWhiteSpace(sessionId) ? "anonymous" : $"session:{sessionId}";
    }
}
