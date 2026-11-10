using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MailboxCleaner.Web.Application.MailboxScanning;

/// <summary>
/// Durable, single-instance metadata cache. Stores message metadata only, never bodies.
/// Use a shared server database rather than a local SQLite file for multi-instance hosting.
/// </summary>
public sealed class SqliteMailboxMetadataStore : IMailboxMetadataStore
{
    private readonly string _connectionString;

    public SqliteMailboxMetadataStore(IConfiguration configuration)
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

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS mailbox_scans (
                user_id TEXT PRIMARY KEY COLLATE NOCASE,
                scan_id TEXT NOT NULL,
                scan_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS mailbox_messages (
                user_id TEXT NOT NULL COLLATE NOCASE,
                message_id TEXT NOT NULL COLLATE NOCASE,
                metadata_json TEXT NOT NULL,
                PRIMARY KEY (user_id, message_id)
            );
            CREATE INDEX IF NOT EXISTS idx_mailbox_messages_user
                ON mailbox_messages(user_id);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<MailboxScanState?> GetCurrentScanAsync(string userId, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var command = MakeCommand(connection,
            "SELECT scan_json FROM mailbox_scans WHERE user_id = $user", userId);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : JsonSerializer.Deserialize<MailboxScanState>(json);
    }

    public async Task SaveScanStateAsync(MailboxScanState state, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        var isBeginning = state.Status == MailboxScanStatus.Discovering;
        var sql = isBeginning
            ? """
              INSERT INTO mailbox_scans(user_id, scan_id, scan_json)
              VALUES($user, $scanId, $json)
              ON CONFLICT(user_id) DO UPDATE SET
                  scan_id = excluded.scan_id, scan_json = excluded.scan_json
              """
            : """
              UPDATE mailbox_scans SET scan_json = $json
              WHERE user_id = $user AND scan_id = $scanId
              """;
        using var command = MakeCommand(connection, sql, state.UserId);
        command.Parameters.AddWithValue("$scanId", state.ScanId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(WithoutDuplicateMetadata(state)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MailboxMetadata>> GetMetadataAsync(string userId, CancellationToken cancellationToken)
    {
        var messages = new List<MailboxMetadata>();
        using var connection = await OpenAsync(cancellationToken);
        using var command = MakeCommand(connection,
            "SELECT metadata_json FROM mailbox_messages WHERE user_id = $user", userId);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var message = JsonSerializer.Deserialize<MailboxMetadata>(reader.GetString(0));
            if (message is not null) messages.Add(message);
        }
        return messages.OrderByDescending(message => message.ReceivedAt).ToList();
    }

    public async Task UpsertMetadataAsync(string userId, IEnumerable<MailboxMetadata> metadata, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var item in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpsertMessageAsync(connection, transaction, userId, item, cancellationToken);
        }
        transaction.Commit();
    }

    public async Task ReplaceMetadataAsync(string userId, IEnumerable<MailboxMetadata> metadata, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using (var command = MakeCommand(connection,
            "DELETE FROM mailbox_messages WHERE user_id = $user", userId, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var item in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpsertMessageAsync(connection, transaction, userId, item, cancellationToken);
        }
        transaction.Commit();
    }

    public async Task<bool> TryCompleteScanAsync(MailboxScanState state, IEnumerable<MailboxMetadata> metadata,
        CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        using (var check = MakeCommand(connection,
            "SELECT scan_id FROM mailbox_scans WHERE user_id = $user", state.UserId, transaction))
        {
            var currentId = await check.ExecuteScalarAsync(cancellationToken) as string;
            if (!string.Equals(currentId, state.ScanId, StringComparison.Ordinal))
                return false;
        }

        using (var clear = MakeCommand(connection,
            "DELETE FROM mailbox_messages WHERE user_id = $user", state.UserId, transaction))
        {
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UpsertMessageAsync(connection, transaction, state.UserId, item, cancellationToken);
        }

        using (var save = MakeCommand(connection,
            "UPDATE mailbox_scans SET scan_json = $json WHERE user_id = $user AND scan_id = $scanId",
            state.UserId, transaction))
        {
            save.Parameters.AddWithValue("$scanId", state.ScanId);
            save.Parameters.AddWithValue("$json", JsonSerializer.Serialize(WithoutDuplicateMetadata(state)));
            await save.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
        return true;
    }

    public async Task<bool> HasMessageAsync(string userId, string messageId, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var command = MakeCommand(connection,
            "SELECT 1 FROM mailbox_messages WHERE user_id = $user AND message_id = $id LIMIT 1", userId);
        command.Parameters.AddWithValue("$id", messageId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "mailbox_messages", "mailbox_scans" })
        {
            using var command = MakeCommand(connection,
                $"DELETE FROM {table} WHERE user_id = $user", userId, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }

    public async Task UpdateAfterActionAsync(string userId, MailboxLocalAction action,
        IReadOnlyCollection<string> successfulMessageIds, string? labelId, CancellationToken cancellationToken)
    {
        if (successfulMessageIds.Count == 0) return;
        using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var selected = new List<MailboxMetadata>();
        foreach (var id in successfulMessageIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var command = MakeCommand(connection,
                "SELECT metadata_json FROM mailbox_messages WHERE user_id = $user AND message_id = $id",
                userId, transaction);
            command.Parameters.AddWithValue("$id", id);
            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (json is not null)
            {
                var message = JsonSerializer.Deserialize<MailboxMetadata>(json);
                if (message is not null) selected.Add(message);
            }
        }

        // Reuse the already unit-tested label/read-state mutation semantics.
        var temporary = new MailboxMetadataStore();
        await temporary.UpsertMetadataAsync(userId, selected, cancellationToken);
        await temporary.UpdateAfterActionAsync(userId, action, successfulMessageIds, labelId, cancellationToken);
        var updated = await temporary.GetMetadataAsync(userId, cancellationToken);
        foreach (var item in updated)
        {
            await UpsertMessageAsync(connection, transaction, userId, item, cancellationToken);
        }
        transaction.Commit();
    }

    public bool IsScanStale(MailboxScanState? state, TimeSpan maxAge)
        => state?.CompletedAt is null || DateTimeOffset.UtcNow - state.CompletedAt.Value > maxAge;

    private static MailboxScanState WithoutDuplicateMetadata(MailboxScanState state)
        => state with { CachedMetadata = Array.Empty<MailboxMetadata>() };

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

    private static SqliteCommand MakeCommand(SqliteConnection connection, string sql,
        string userId, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$user", userId);
        return command;
    }

    private static async Task UpsertMessageAsync(SqliteConnection connection, SqliteTransaction transaction,
        string userId, MailboxMetadata metadata, CancellationToken cancellationToken)
    {
        using var command = MakeCommand(connection,
            """
            INSERT INTO mailbox_messages(user_id, message_id, metadata_json)
            VALUES($user, $messageId, $json)
            ON CONFLICT(user_id, message_id) DO UPDATE
            SET metadata_json = excluded.metadata_json
            """, userId, transaction);
        command.Parameters.AddWithValue("$messageId", metadata.MessageId);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
