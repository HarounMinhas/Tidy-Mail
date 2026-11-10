using System.Security.Cryptography;
using System.Text.Json;
using MailboxCleaner.Web.Application.MailboxScanning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace MailboxCleaner.Web.Infrastructure.Security;

/// <summary>
/// Persists only Data Protection-encrypted OAuth tokens, keyed by verified Google subject.
/// The Data Protection key ring must also be persisted for tokens to survive a restart.
/// </summary>
public sealed class SqliteTokenStore : ITokenStore
{
    private readonly string _connectionString;
    private readonly IDataProtector _protector;
    private readonly IUserMailboxKeyProvider _keyProvider;
    private readonly MailboxUserContext _mailboxUserContext;

    public SqliteTokenStore(IConfiguration configuration, IDataProtectionProvider protection,
        IUserMailboxKeyProvider keyProvider, MailboxUserContext mailboxUserContext)
    {
        var configured = configuration["Mailbox:SqlitePath"];
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(root, "TidyMail", "mailbox.db")
            : configured);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _protector = protection.CreateProtector("MailboxCleaner.TokenStore.v3");
        _keyProvider = keyProvider;
        _mailboxUserContext = mailboxUserContext;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS account_tokens (
                user_id TEXT PRIMARY KEY COLLATE NOCASE,
                protected_token TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private string AccountKey
    {
        get
        {
            var key = _mailboxUserContext.CurrentUserKey ?? _keyProvider.GetCurrentUserKey();
            if (!key.StartsWith("user:", StringComparison.Ordinal) || key.Length <= 5)
                throw new UnauthorizedAccessException("A validated Google account is required to access Gmail tokens.");
            return key;
        }
    }

    public async Task SaveTokensAsync(TokenSet tokenSet, CancellationToken cancellationToken)
    {
        var encrypted = _protector.Protect(JsonSerializer.Serialize(tokenSet));
        using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO account_tokens(user_id, protected_token, updated_utc)
            VALUES($user, $token, $updated)
            ON CONFLICT(user_id) DO UPDATE
            SET protected_token = excluded.protected_token, updated_utc = excluded.updated_utc
            """;
        command.Parameters.AddWithValue("$user", AccountKey);
        command.Parameters.AddWithValue("$token", encrypted);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TokenSet?> GetTokensAsync(CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT protected_token FROM account_tokens WHERE user_id = $user";
        command.Parameters.AddWithValue("$user", AccountKey);
        var encrypted = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        try
        {
            return JsonSerializer.Deserialize<TokenSet>(_protector.Unprotect(encrypted));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            await ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM account_tokens WHERE user_id = $user";
        command.Parameters.AddWithValue("$user", AccountKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
