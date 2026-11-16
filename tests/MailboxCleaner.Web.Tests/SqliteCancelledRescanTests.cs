using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Demo;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MailboxCleaner.Web.Tests;

public sealed class SqliteCancelledRescanTests
{
    [Fact]
    public async Task CancelledRecentRescan_PersistsRetryMarkerAcrossDatabaseReopen()
    {
        const string userId = "user:demo-alice";
        var file = Path.Combine(Path.GetTempPath(), $"tidymail-cancel-{Guid.NewGuid():N}.sqlite");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mailbox:SqlitePath"] = file })
            .Build();

        try
        {
            var dummy = new DemoMailboxStore();
            var gmail = new DemoGmailClient(dummy,
                new MailboxUserContext { CurrentUserKey = userId },
                new DenyFallbackKeyProvider());
            var store = new SqliteMailboxMetadataStore(configuration);
            var scanService = new MailboxScanService(gmail, store);

            var completed = await scanService.ScanAsync(userId, null, CancellationToken.None);
            Assert.Equal(MailboxScanStatus.Completed, completed.Status);
            Assert.False(store.IsScanStale(completed, TimeSpan.FromHours(12)));

            dummy.CancelNextSync(userId);
            var cancelled = await scanService.ScanAsync(userId, null, CancellationToken.None);
            Assert.Equal(MailboxScanStatus.Cancelled, cancelled.Status);

            var reopened = new SqliteMailboxMetadataStore(configuration);
            var persisted = await reopened.GetCurrentScanAsync(userId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(MailboxScanStatus.Completed, persisted.Status);
            Assert.Equal(completed.CompletedAt, persisted.CompletedAt);
            Assert.Equal(completed.LastHistoryId, persisted.LastHistoryId);
            Assert.Equal(completed.ScannedMessages, persisted.ScannedMessages);
            Assert.False(reopened.IsScanStale(persisted, TimeSpan.FromHours(12)));
            // Overview.razor shows its Retry scan button for any nonempty ErrorMessage.
            Assert.Contains("cancelled", persisted.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(48, (await reopened.GetMetadataAsync(userId, CancellationToken.None)).Count);

            var retried = await scanService.ScanAsync(userId, null, CancellationToken.None);
            Assert.Equal(MailboxScanStatus.Completed, retried.Status);
            Assert.Null((await reopened.GetCurrentScanAsync(userId, CancellationToken.None))?.ErrorMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(file + suffix); }
                catch (IOException) { /* Disposable test database only. */ }
            }
        }
    }

    private sealed class DenyFallbackKeyProvider : IUserMailboxKeyProvider
    {
        public string GetCurrentUserKey()
            => throw new UnauthorizedAccessException("Only the explicit demo test account is allowed.");
    }
}
