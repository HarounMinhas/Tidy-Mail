using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MailboxCleaner.Web.Tests;

public sealed class SqliteTrashRegressionTests
{
    [Fact]
    public async Task AcknowledgedTrash_ImmediatelyRemovesMessageFromDurableDashboardSnapshot()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tidymail-trash-{Guid.NewGuid():N}.sqlite");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mailbox:SqlitePath"] = file })
            .Build();

        try
        {
            var store = new SqliteMailboxMetadataStore(config);
            var now = DateTimeOffset.UtcNow;
            await store.UpsertMetadataAsync("user:alice",
            [
                new MailboxMetadata("message-1", "thread-1", "Example", "sender@example.test",
                    "example.test", "First", now, true, ["INBOX"], false, 100, now),
                new MailboxMetadata("message-2", "thread-2", "Example", "sender@example.test",
                    "example.test", "Second", now, false, ["INBOX", "UNREAD"], false, 150, now)
            ], CancellationToken.None);

            await store.UpdateAfterActionAsync("user:alice", MailboxLocalAction.Trash,
                ["message-1"], null, CancellationToken.None);

            // This is the actual SQLite store used outside demo mode, not an in-memory double.
            var messages = await store.GetMetadataAsync("user:alice", CancellationToken.None);
            Assert.Single(messages);
            Assert.Equal("message-2", messages.Single().MessageId);
            Assert.Equal(1, new MailboxStatsService().Build(messages).TotalScannedMessages);

            // A second account is never modified by Alice's actions.
            Assert.Empty(await store.GetMetadataAsync("user:bob", CancellationToken.None));
            var reopened = new SqliteMailboxMetadataStore(config);
            Assert.False(await reopened.HasMessageAsync("user:alice", "message-1", CancellationToken.None));
            Assert.True(await reopened.HasMessageAsync("user:alice", "message-2", CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(file + suffix); }
                catch (IOException) { /* The test database is an isolated disposable file. */ }
            }
        }
    }
}
