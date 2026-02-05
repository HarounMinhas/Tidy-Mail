using System.Security.Claims;
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

    public GoogleAuthController(IGoogleOAuthService oauthService, ITokenStore tokenStore)
    {
        _oauthService = oauthService;
        _tokenStore = tokenStore;
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
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, CancellationToken cancellationToken)
    {
        var expectedState = HttpContext.Session.GetString(StateKey);
        if (string.IsNullOrWhiteSpace(expectedState) || expectedState != state)
        {
            return BadRequest("Invalid state.");
        }

        var tokenSet = await _oauthService.ExchangeCodeAsync(code, cancellationToken);
        await _tokenStore.SaveTokensAsync(tokenSet, cancellationToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "Google User"),
            new(ClaimTypes.NameIdentifier, "google-user")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Redirect("/overview");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _tokenStore.ClearAsync(cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }
}
