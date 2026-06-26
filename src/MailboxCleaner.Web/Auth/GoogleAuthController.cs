using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Google;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace MailboxCleaner.Web.Auth;

[ApiController]
[Route("auth")]
public sealed class GoogleAuthController : ControllerBase
{
    private const string StateKey = "oauth_state";
    private readonly IGoogleOAuthService _oauthService;
    private readonly ITokenStore _tokenStore;
    private readonly IMailboxMetadataStore _metadataStore;
    private readonly IUserMailboxKeyProvider _userMailboxKeyProvider;

    public GoogleAuthController(IGoogleOAuthService oauthService, ITokenStore tokenStore, IMailboxMetadataStore metadataStore, IUserMailboxKeyProvider userMailboxKeyProvider)
    {
        _oauthService = oauthService;
        _tokenStore = tokenStore;
        _metadataStore = metadataStore;
        _userMailboxKeyProvider = userMailboxKeyProvider;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(StateKey, state);
        var url = _oauthService.BuildAuthorizationUrl(state);
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var expectedState = HttpContext.Session.GetString(StateKey);
        if (string.IsNullOrWhiteSpace(expectedState) || expectedState != state)
        {
            return BadRequest("Invalid state.");
        }

        HttpContext.Session.Remove(StateKey);
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest("Google sign-in was cancelled or denied.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Invalid state.");
        }

        var tokenSet = await _oauthService.ExchangeCodeAsync(code, cancellationToken);
        await _tokenStore.SaveTokensAsync(tokenSet, cancellationToken);

        var googleIdentity = GoogleIdentityClaims.FromIdToken(tokenSet.IdToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, googleIdentity.Name ?? googleIdentity.Email ?? "Google User")
        };

        if (!string.IsNullOrWhiteSpace(googleIdentity.Subject))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, googleIdentity.Subject));
        }

        if (!string.IsNullOrWhiteSpace(googleIdentity.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, googleIdentity.Email));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Redirect("/overview");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userKey = _userMailboxKeyProvider.GetCurrentUserKey();
        await _metadataStore.ClearAsync(userKey, cancellationToken);
        await _tokenStore.ClearAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    private sealed record GoogleIdentityClaims(string? Subject, string? Email, string? Name)
    {
        public static GoogleIdentityClaims FromIdToken(string? idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                return new GoogleIdentityClaims(null, null, null);
            }

            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return new GoogleIdentityClaims(null, null, null);
            }

            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                return new GoogleIdentityClaims(
                    TryGetString(root, "sub"),
                    TryGetString(root, "email"),
                    TryGetString(root, "name"));
            }
            catch (JsonException)
            {
                return new GoogleIdentityClaims(null, null, null);
            }
            catch (FormatException)
            {
                return new GoogleIdentityClaims(null, null, null);
            }
        }

        private static string? TryGetString(JsonElement element, string propertyName)
            => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static byte[] Base64UrlDecode(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return Convert.FromBase64String(padded);
        }
    }
}
