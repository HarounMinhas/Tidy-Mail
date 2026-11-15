using System.Security.Claims;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MailboxCleaner.Web.Infrastructure.Demo;

/// <summary>
/// Development-only fake logins. Uses real ASP.NET authentication cookies while
/// never touching Google. Endpoints are not registered outside explicit demo mode.
/// </summary>
public static class DemoAuthEndpoints
{
    public static void MapDemoAuthEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Demo authentication must never run outside Development.");

        app.MapGet("/demo/login", async (HttpContext context, string? account) =>
        {
            if (!IsLocalRequest(context)) return Results.NotFound();
            if (account is not ("alice" or "bob"))
                return Results.BadRequest("Choose the demo account alice or bob.");

            var subject = $"demo-{account}";
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, $"Demo {char.ToUpperInvariant(account[0])}{account[1..]}"),
                new Claim(ClaimTypes.Email, $"{account}@demo.example.test"),
                new Claim("tidymail:demo", "true")
            ], CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });

            return Results.Redirect("/overview");
        });

        app.MapGet("/demo/logout", async (HttpContext context) =>
        {
            if (!IsLocalRequest(context)) return Results.NotFound();
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        });
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote is null || System.Net.IPAddress.IsLoopback(remote);
    }
}

/// <summary>Never returns real credentials; only used to prevent OAuth access in demo mode.</summary>
public sealed class DemoTokenStore : ITokenStore
{
    public Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken)
        => Task.FromResult<TokenSet?>(null);

    public Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken)
        => throw new NotSupportedException("Google OAuth token storage is disabled in demo mode.");

    public Task ClearAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
