using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MailboxCleaner.Web.Tests;

public sealed class SqliteTokenStoreTests
{
    [Fact]
    public async Task Tokens_AreEncryptedPersistedAndIsolatedByGoogleSubject()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tidy-tokens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "mailbox.db");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Mailbox:SqlitePath"] = path }).Build();
        using var services = new ServiceCollection().AddDataProtection().Services.BuildServiceProvider();
        var protection = services.GetRequiredService<IDataProtectionProvider>();

        try
        {
            var alice = new SqliteTokenStore(configuration, protection, new RejectingFallback(),
                new MailboxUserContext { CurrentUserKey = "user:alice" });
            var bob = new SqliteTokenStore(configuration, protection, new RejectingFallback(),
                new MailboxUserContext { CurrentUserKey = "user:bob" });

            await alice.SaveTokensAsync(new TokenSet("alice-access", "alice-secret-refresh", null), CancellationToken.None);
            Assert.Null(await bob.GetTokensAsync(CancellationToken.None));

            // A new instance (e.g. a new Blazor circuit) reads the same encrypted record.
            var reopened = new SqliteTokenStore(configuration, protection, new RejectingFallback(),
                new MailboxUserContext { CurrentUserKey = "user:alice" });
            Assert.Equal("alice-access", (await reopened.GetTokensAsync(CancellationToken.None))?.AccessToken);

            using var sql = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            sql.Open();
            using var command = sql.CreateCommand();
            command.CommandText = "SELECT protected_token FROM account_tokens WHERE user_id = 'user:alice'";
            var stored = (string)command.ExecuteScalar()!;
            Assert.DoesNotContain("alice-secret-refresh", stored, StringComparison.Ordinal);

            await alice.ClearAsync(CancellationToken.None);
            Assert.Null(await reopened.GetTokensAsync(CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RejectingFallback : IUserMailboxKeyProvider
    {
        public string GetCurrentUserKey() => throw new UnauthorizedAccessException("No HTTP context.");
    }
}
