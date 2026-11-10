using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace MailboxCleaner.Web.Application.MailboxScanning;

public interface IUserMailboxKeyProvider
{
    string GetCurrentUserKey();
}

/// <summary>Only a validated, authenticated Google subject may identify a mailbox.</summary>
public sealed class UserMailboxKeyProvider : IUserMailboxKeyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserMailboxKeyProvider(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string GetCurrentUserKey()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var subject = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject) ||
            string.Equals(subject, "google-user", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A validated Google account is required to access mailbox data.");
        }

        return $"user:{subject.Trim().ToLowerInvariant()}";
    }
}
