using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Auth;
using MailboxCleaner.Web.Infrastructure.Google;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace MailboxCleaner.Web.Tests;

public sealed class GoogleAuthControllerTests
{
    [Fact]
    public async Task Callback_WithErrorAndInvalidState_DoesNotClearStoredOAuthState()
    {
        var session = new TestSession();
        session.SetString("oauth_state", "expected-state");
        var controller = CreateController(session);

        var result = await controller.Callback(null, "wrong-state", "access_denied", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("expected-state", session.GetString("oauth_state"));
    }

    [Fact]
    public async Task Callback_WithErrorAndValidState_ClearsStoredOAuthState()
    {
        var session = new TestSession();
        session.SetString("oauth_state", "expected-state");
        var controller = CreateController(session);

        var result = await controller.Callback(null, "expected-state", "access_denied", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(session.GetString("oauth_state"));
    }

    private static GoogleAuthController CreateController(ISession session)
    {
        var context = new DefaultHttpContext();
        context.Features.Set<ISessionFeature>(new TestSessionFeature(session));
        return new GoogleAuthController(new TestOAuthService(), new TestTokenStore(), new MailboxMetadataStore(), new TestUserMailboxKeyProvider())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public TestSessionFeature(ISession session) => Session = session;
        public ISession Session { get; set; }
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }

    private sealed class TestOAuthService : IGoogleOAuthService
    {
        public string BuildAuthorizationUrl(string state) => $"https://accounts.google.com/o/oauth2/v2/auth?state={state}";
        public Task<TokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken) => throw new InvalidOperationException("Exchange should not be called for callback errors.");
    }

    private sealed class TestTokenStore : ITokenStore
    {
        public Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken) => Task.FromResult<TokenSet?>(null);
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestUserMailboxKeyProvider : IUserMailboxKeyProvider
    {
        public string GetCurrentUserKey() => "test-user";
    }
}
