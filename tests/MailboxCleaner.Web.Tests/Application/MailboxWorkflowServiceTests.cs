using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.Filtering;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;
using MailboxCleaner.Web.Infrastructure.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

namespace MailboxCleaner.Web.Tests.Application;

public sealed class MailboxWorkflowServiceTests
{
    [Fact]
    public void StatsSuggestionsFilteringAndSelection_RunFromMetadataOnly()
    {
        var now = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
        var metadata = CreateMetadata(now);

        var stats = new MailboxStatsService().Build(metadata, now, now);
        Assert.Equal(4, stats.TotalScannedMessages);
        Assert.Equal(2, stats.UnreadCount);
        Assert.NotEmpty(stats.TopSenders);
        Assert.NotEmpty(stats.TopDomains);

        var suggestions = new CleanupSuggestionService().Generate(metadata, now);
        Assert.Contains(suggestions, suggestion => suggestion.Title.Contains("Old unread", StringComparison.OrdinalIgnoreCase));

        var filter = new MailboxFilterService();
        var newsletters = filter.Apply(metadata, new MailboxFilter { NewsletterLikeOnly = true });
        Assert.All(newsletters, message => Assert.True(message.IsNewsletterLike));

        var selection = new MailboxSelectionService();
        selection.SelectAllMatching(newsletters);
        Assert.Equal(newsletters.Count, selection.SelectedMessageIds.Count);
        selection.Clear();
        Assert.Empty(selection.SelectedMessageIds);
    }

    [Fact]
    public async Task ReplaceMetadataAsync_RemovesMessagesMissingFromFullRescan()
    {
        var store = new MailboxMetadataStore();
        var metadata = CreateMetadata(DateTimeOffset.UtcNow);
        await store.ReplaceMetadataAsync("user", metadata, CancellationToken.None);

        await store.ReplaceMetadataAsync("user", metadata.Where(m => m.MessageId != "m4"), CancellationToken.None);

        var cached = await store.GetMetadataAsync("user", CancellationToken.None);
        Assert.DoesNotContain(cached, message => message.MessageId == "m4");
    }

    [Fact]
    public async Task SqliteStore_PersistsMetadataAndRejectsClearedScan()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tidy-mail-{Guid.NewGuid():N}.db");
        var options = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mailbox:SqlitePath"] = path
        }).Build();
        try
        {
            var store = new SqliteMailboxMetadataStore(options);
            var state = new MailboxScanState { UserId = "user:alice", Status = MailboxScanStatus.Discovering };
            await store.SaveScanStateAsync(state, CancellationToken.None);
            Assert.True(await store.TryCompleteScanAsync(state with { Status = MailboxScanStatus.Completed },
                CreateMetadata(DateTimeOffset.UtcNow), CancellationToken.None));

            var reopened = new SqliteMailboxMetadataStore(options);
            Assert.Equal(MailboxScanStatus.Completed,
                (await reopened.GetCurrentScanAsync("user:alice", CancellationToken.None))?.Status);
            Assert.Equal(4, (await reopened.GetMetadataAsync("user:alice", CancellationToken.None)).Count);
            Assert.Empty(await reopened.GetMetadataAsync("user:bob", CancellationToken.None));

            await reopened.ClearAsync("user:alice", CancellationToken.None);
            Assert.False(await store.TryCompleteScanAsync(state with { Status = MailboxScanStatus.Completed },
                CreateMetadata(DateTimeOffset.UtcNow), CancellationToken.None));
            Assert.Empty(await store.GetMetadataAsync("user:alice", CancellationToken.None));
        }
        finally
        {
            SqliteTestCleanup(path);
        }
    }

    private static void SqliteTestCleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(path + suffix); }
            catch (IOException) { /* SQLite connection pooling may keep the file briefly. */ }
        }
    }

    [Fact]
    public async Task ClearedMailbox_DoesNotResurrectAnInFlightScan()
    {
        var store = new MailboxMetadataStore();
        var state = new MailboxScanState { UserId = "user", Status = MailboxScanStatus.Discovering };
        await store.SaveScanStateAsync(state, CancellationToken.None);
        await store.ClearAsync("user", CancellationToken.None);

        var accepted = await store.TryCompleteScanAsync(state with { Status = MailboxScanStatus.Completed },
            CreateMetadata(DateTimeOffset.UtcNow), CancellationToken.None);
        Assert.False(accepted);
        Assert.Null(await store.GetCurrentScanAsync("user", CancellationToken.None));
        Assert.Empty(await store.GetMetadataAsync("user", CancellationToken.None));
    }

    [Fact]
    public async Task OnlyTheLatestScanCanReplaceMailboxMetadata()
    {
        var store = new MailboxMetadataStore();
        var first = new MailboxScanState { UserId = "user", Status = MailboxScanStatus.Discovering };
        var second = new MailboxScanState { UserId = "user", Status = MailboxScanStatus.Discovering };
        await store.SaveScanStateAsync(first, CancellationToken.None);
        await store.SaveScanStateAsync(second, CancellationToken.None);

        Assert.False(await store.TryCompleteScanAsync(first with { Status = MailboxScanStatus.Completed },
            CreateMetadata(DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.True(await store.TryCompleteScanAsync(second with { Status = MailboxScanStatus.Completed },
            CreateMetadata(DateTimeOffset.UtcNow), CancellationToken.None));
    }

    [Fact]
    public async Task StoreUpdatesOnlySuccessfulMessageIdsAfterActions()
    {
        var store = new MailboxMetadataStore();
        var metadata = CreateMetadata(DateTimeOffset.UtcNow);
        await store.UpsertMetadataAsync("user", metadata, CancellationToken.None);

        await store.UpdateAfterActionAsync("user", MailboxLocalAction.MarkRead, ["m1"], null, CancellationToken.None);
        await store.UpdateAfterActionAsync("user", MailboxLocalAction.Archive, ["m2"], null, CancellationToken.None);

        var cached = await store.GetMetadataAsync("user", CancellationToken.None);
        Assert.True(cached.Single(m => m.MessageId == "m1").IsRead);
        Assert.DoesNotContain("INBOX", cached.Single(m => m.MessageId == "m2").Labels);
        Assert.Contains("UNREAD", cached.Single(m => m.MessageId == "m3").Labels);
    }

    [Fact]
    public async Task MoveToLabel_RemovesPreviousUserLabelsButKeepsSystemLabels()
    {
        var store = new MailboxMetadataStore();
        var now = DateTimeOffset.UtcNow;
        await store.ReplaceMetadataAsync("user",
        [
            new("m1", "t1", "Sender", "sender@example.com", "example.com", "Subject", now, false, ["INBOX", "UNREAD", "Label_Old"], false, 100, now)
        ], CancellationToken.None);

        await store.UpdateAfterActionAsync("user", MailboxLocalAction.MoveToLabel, ["m1"], "Label_New", CancellationToken.None);

        var moved = (await store.GetMetadataAsync("user", CancellationToken.None)).Single();
        Assert.Contains("Label_New", moved.Labels);
        Assert.Contains("UNREAD", moved.Labels);
        Assert.DoesNotContain("Label_Old", moved.Labels);
        Assert.DoesNotContain("INBOX", moved.Labels);
    }


    [Fact]
    public async Task MoveToInbox_KeepsInboxLabelInLocalCache()
    {
        var store = new MailboxMetadataStore();
        var now = DateTimeOffset.UtcNow;
        await store.ReplaceMetadataAsync("user",
        [
            new("m1", "t1", "Sender", "sender@example.com", "example.com", "Subject", now, true, ["Label_Old"], false, 100, now)
        ], CancellationToken.None);

        await store.UpdateAfterActionAsync("user", MailboxLocalAction.MoveToLabel, ["m1"], "INBOX", CancellationToken.None);

        var moved = (await store.GetMetadataAsync("user", CancellationToken.None)).Single();
        Assert.Contains("INBOX", moved.Labels);
    }

    [Fact]
    public async Task BulkAction_UpdatesCacheOnlyForSucceededMessageIds()
    {
        var store = new MailboxMetadataStore();
        var metadata = CreateMetadata(DateTimeOffset.UtcNow);
        await store.ReplaceMetadataAsync("user", metadata, CancellationToken.None);
        var gmail = new PartiallyFailingGmailClient(["m1"], ["m3"]);
        var service = new GmailBulkActionService(gmail, store);

        var result = await service.ApplyAsync("user", CleanupBulkAction.MarkRead, ["m1", "m3"], null, null, CancellationToken.None);

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);
        var cached = await store.GetMetadataAsync("user", CancellationToken.None);
        Assert.True(cached.Single(m => m.MessageId == "m1").IsRead);
        Assert.False(cached.Single(m => m.MessageId == "m3").IsRead);
    }

    [Fact]
    public async Task ScanAsync_WhenMetadataFetchFails_PreservesExistingCacheAndMarksFailed()
    {
        var store = new MailboxMetadataStore();
        var existing = CreateMetadata(DateTimeOffset.UtcNow);
        await store.ReplaceMetadataAsync("user", existing, CancellationToken.None);
        var service = new MailboxScanService(new FailingMetadataGmailClient(), store);

        var state = await service.ScanAsync("user", null, CancellationToken.None);

        Assert.Equal(MailboxScanStatus.Failed, state.Status);
        Assert.Contains("Unable to load complete Gmail metadata", state.ErrorMessage);
        var cached = await store.GetMetadataAsync("user", CancellationToken.None);
        Assert.Equal(existing.Count, cached.Count);
        Assert.Contains(cached, message => message.MessageId == "m4");
    }

    [Fact]
    public void UserMailboxKeyProvider_FailsClosedWithoutAuthenticatedGoogleSubject()
    {
        var contextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        contextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "google-user")
        ], "test"));
        Assert.Throws<UnauthorizedAccessException>(() => new UserMailboxKeyProvider(contextAccessor).GetCurrentUserKey());

        contextAccessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "real-subject")
        ], "test"));
        Assert.Equal("user:real-subject", new UserMailboxKeyProvider(contextAccessor).GetCurrentUserKey());

        contextAccessor.HttpContext = null;
        Assert.Throws<UnauthorizedAccessException>(() => new UserMailboxKeyProvider(contextAccessor).GetCurrentUserKey());
    }

    [Fact]
    public void PreviewIncludesWarningTopSendersAndSamples()
    {
        var metadata = CreateMetadata(DateTimeOffset.UtcNow);
        var preview = new BulkActionPreviewService().Build(CleanupBulkAction.Trash, metadata, ["m1", "m2"]);

        Assert.True(preview.CanConfirm);
        Assert.NotNull(preview.RiskWarning);
        Assert.NotEmpty(preview.TopAffectedSenders);
        Assert.NotEmpty(preview.SampleMessages);
    }

    private static IReadOnlyList<MailboxMetadata> CreateMetadata(DateTimeOffset now) =>
    [
        new("m1", "t1", "News", "news@example.com", "example.com", "Newsletter", now.AddYears(-2), true, ["INBOX"], false, 100, now, "<mailto:u@example.com>", "bulk"),
        new("m2", "t1", "News", "news@example.com", "example.com", "Newsletter 2", now.AddYears(-2), true, ["INBOX"], true, 100, now, "<mailto:u@example.com>", "bulk"),
        new("m3", "t2", "Alerts", "noreply@service.com", "service.com", "Alert", now.AddYears(-2), false, ["INBOX", "UNREAD"], false, 100, now),
        new("m4", "t3", "Human", "person@corp.com", "corp.com", "Meeting", now, false, ["INBOX", "UNREAD"], false, 100, now)
    ];

    private sealed class FailingMetadataGmailClient : IGmailClient
    {
        public Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken) => throw new GmailOperationException("Unable to load complete Gmail metadata.", new InvalidOperationException("partial scan"));
        public Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GmailLabel>>(Array.Empty<GmailLabel>());
        public Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Task.FromResult(GmailActionResult.Success(messageIds));
        public Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Task.FromResult(GmailActionResult.Success(messageIds));
        public Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Task.FromResult(GmailActionResult.Success(messageIds));
        public Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Task.FromResult(GmailActionResult.Success(messageIds));
        public Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken) => Task.FromResult(GmailActionResult.Success(messageIds));
        public Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken) => Task.FromResult(new GmailLabel(labelName, labelName, false));
    }

    private sealed class PartiallyFailingGmailClient : IGmailClient
    {
        private readonly IReadOnlyList<string> _succeeded;
        private readonly IReadOnlyList<string> _failed;

        public PartiallyFailingGmailClient(IReadOnlyList<string> succeeded, IReadOnlyList<string> failed)
        {
            _succeeded = succeeded;
            _failed = failed;
        }

        public Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GmailMessageMetadata>>(Array.Empty<GmailMessageMetadata>());
        public Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GmailLabel>>(Array.Empty<GmailLabel>());
        public Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Result(messageIds);
        public Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Result(messageIds);
        public Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Result(messageIds);
        public Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) => Result(messageIds);
        public Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken) => Result(messageIds);
        public Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken) => Task.FromResult(new GmailLabel(labelName, labelName, false));

        private Task<GmailActionResult> Result(IReadOnlyCollection<string> messageIds)
            => Task.FromResult(new GmailActionResult(messageIds.Count, _succeeded, _failed, ["failed"]));
    }

}
