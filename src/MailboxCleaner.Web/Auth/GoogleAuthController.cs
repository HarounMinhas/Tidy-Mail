using System.Security.Claims;
using Google.Apis.Auth;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Google;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly MailboxUserContext _mailboxUserContext;
    private readonly GoogleOAuthOptions _options;

    public GoogleAuthController(IGoogleOAuthService oauthService, ITokenStore tokenStore,
        IMailboxMetadataStore metadataStore, IUserMailboxKeyProvider userMailboxKeyProvider,
        MailboxUserContext mailboxUserContext, IOptions<GoogleOAuthOptions> options)
    {
        _oauthService = oauthService;
        _tokenStore = tokenStore;
        _metadataStore = metadataStore;
        _userMailboxKeyProvider = userMailboxKeyProvider;
        _mailboxUserContext = mailboxUserContext;
        _options = options.Value;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(StateKey, state);
        return Redirect(_oauthService.BuildAuthorizationUrl(state));
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken cancellationToken)
    {
        var expectedState = HttpContext.Session.GetString(StateKey);
        if (string.IsNullOrWhiteSpace(expectedState) || expectedState != state)
        {
            return BadRequest("Invalid state.");
        }
        HttpContext.Session.Remove(StateKey);

        if (!string.IsNullOrWhiteSpace(error)) return BadRequest("Google sign-in was cancelled or denied.");
        if (string.IsNullOrWhiteSpace(code)) return BadRequest("Missing Google authorization code.");

        var tokenSet = await _oauthService.ExchangeCodeAsync(code, cancellationToken);
        if (string.IsNullOrWhiteSpace(tokenSet.IdToken))
        {
            return BadRequest("Google did not return an identity token.");
        }

        GoogleJsonWebSignature.Payload identity;
        try
        {
            identity = await GoogleJsonWebSignature.ValidateAsync(tokenSet.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { _options.ClientId } });
        }
        catch (InvalidJwtException)
        {
            return BadRequest("Invalid Google identity token.");
        }

        if (string.IsNullOrWhiteSpace(identity.Subject))
        {
            return BadRequest("Google identity is missing a subject.");
        }

        _mailboxUserContext.CurrentUserKey = $"user:{identity.Subject.Trim().ToLowerInvariant()}";
        var existingTokens = await _tokenStore.GetTokensAsync(cancellationToken);
        await _tokenStore.SaveTokensAsync(tokenSet.PreserveExistingRefreshToken(existingTokens), cancellationToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.Subject),
            new(ClaimTypes.Name, identity.Email ?? "Google User")
        };
        if (!string.IsNullOrWhiteSpace(identity.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, identity.Email));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Redirect("/overview");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var userKey = _userMailboxKeyProvider.GetCurrentUserKey();
            _mailboxUserContext.CurrentUserKey = userKey;
            await _metadataStore.ClearAsync(userKey, cancellationToken);
            await _tokenStore.ClearAsync(cancellationToken);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }
}
