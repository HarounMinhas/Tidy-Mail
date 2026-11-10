using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Tests;

public sealed class GmailBulkActionServiceTests
{
    [Fact]
    public async Task PartialResult_OnlyAcknowledgedMessagesChangeLocalCache()
    {
        var store = await CreateStoreAsync();
        var gmail = new ActionGmailClient
        {
            Result = new GmailActionResult(3, ["m1"], ["m2"], ["Gmail rejected m2"], ["m3"])
        };

        var result = await new GmailBulkActionService(gmail, store)
            .ApplyAsync("user:alice", CleanupBulkAction.MarkRead, ["m1", "m2", "m3"],
                null, null, CancellationToken.None);

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(1, result.TotalFailed);
        Assert.Equal(1, result.TotalUnknown);
        Assert.True(result.RequiresReconciliation);
        Assert.Equal(["m2"], result.FailedMessageIds);
        Assert.Equal(["m3"], result.UnknownMessageIds);
        var state = await store.GetMetadataAsync("user:alice", CancellationToken.None);
        Assert.True(state.Single(m => m.MessageId == "m1").IsRead);
        Assert.False(state.Single(m => m.MessageId == "m2").IsRead);
        Assert.False(state.Single(m => m.MessageId == "m3").IsRead);
    }

    [Fact]
    public async Task NetworkError_DoesNotFalselyClassifyMessagesAsRejected()
    {
        var store = await CreateStoreAsync();
        var gmail = new ActionGmailClient { Error = new HttpRequestException("Connection lost after send") };
        var result = await new GmailBulkActionService(gmail, store)
            .ApplyAsync("user:alice", CleanupBulkAction.Archive, ["m1", "m2"],
                null, null, CancellationToken.None);

        Assert.Equal(0, result.TotalSucceeded);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(2, result.TotalUnknown);
        Assert.Empty(result.FailedMessageIds);
        Assert.True(result.RequiresReconciliation);
        Assert.Contains("INBOX", (await store.GetMetadataAsync("user:alice", CancellationToken.None))
            .Single(m => m.MessageId == "m1").Labels);
    }

    [Fact]
    public async Task AddLabel_PreservesInboxAndCustomLabels_AndReturnsCreatedLabelForRetry()
    {
        var store = await CreateStoreAsync();
        var gmail = new ActionGmailClient
        {
            Result = new GmailActionResult(2, ["m1"], ["m2"], ["m2 was rejected"])
        };

        var result = await new GmailBulkActionService(gmail, store)
            .ApplyAsync("user:alice", CleanupBulkAction.AddLabel, ["m1", "m2"],
                null, "Receipts", CancellationToken.None);

        Assert.Equal("Label_Receipts", result.TargetLabelId);
        Assert.Equal(["Receipts"], gmail.CreatedLabels);
        Assert.Equal(["Label_Receipts"], gmail.AddedLabels);
        var m1 = (await store.GetMetadataAsync("user:alice", CancellationToken.None))
            .Single(m => m.MessageId == "m1");
        Assert.Contains("INBOX", m1.Labels);
        Assert.Contains("Label_Old", m1.Labels);
        Assert.Contains("Label_Receipts", m1.Labels);
        Assert.DoesNotContain("Label_Receipts",
            (await store.GetMetadataAsync("user:alice", CancellationToken.None))
                .Single(m => m.MessageId == "m2").Labels);
    }

    [Fact]
    public async Task CacheFailure_PreservesAcknowledgedGmailResult()
    {
        var inner = await CreateStoreAsync();
        var gmail = new ActionGmailClient();
        var service = new GmailBulkActionService(gmail, new FailingUpdateStore(inner));

        var result = await service.ApplyAsync("user:alice", CleanupBulkAction.Archive,
            ["m1"], null, null, CancellationToken.None);

        Assert.Equal(1, result.TotalSucceeded);
        Assert.Equal(0, result.TotalFailed);
        Assert.Equal(0, result.TotalUnknown);
        Assert.True(result.CacheUpdateFailed);
        Assert.True(result.RequiresReconciliation);
    }

    private static async Task<MailboxMetadataStore> CreateStoreAsync()
    {
        var store = new MailboxMetadataStore();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertMetadataAsync("user:alice",
            Enumerable.Range(1, 3).Select(i => new MailboxMetadata($"m{i}", $"t{i}",
                "Sender", "sender@example.com", "example.com", "Subject",
                now, false, ["INBOX", "UNREAD", "Label_Old"], false, 123, now)),
            CancellationToken.None);
        return store;
    }

    private sealed class ActionGmailClient : IGmailClient
    {
        public GmailActionResult? Result { get; init; }
        public Exception? Error { get; init; }
        public List<string> CreatedLabels { get; } = new();
        public List<string> AddedLabels { get; } = new();

        private Task<GmailActionResult> Execute(IReadOnlyCollection<string> ids)
        {
            if (Error is not null) throw Error;
            return Task.FromResult(Result ?? GmailActionResult.Success(ids));
        }

        public Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GmailMessageMetadata>>(Array.Empty<GmailMessageMetadata>());
        public Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GmailLabel>>(Array.Empty<GmailLabel>());
        public Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
            => Execute(ids);
        public Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
            => Execute(ids);
        public Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
            => Execute(ids);
        public Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
            => Execute(ids);
        public Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> ids, string label, CancellationToken ct)
            => Execute(ids);
        public Task<GmailActionResult> AddLabelToMessagesAsync(IReadOnlyCollection<string> ids, string label, CancellationToken ct)
        {
            AddedLabels.Add(label);
            return Execute(ids);
        }
        public Task<GmailLabel> CreateLabelAsync(string name, CancellationToken ct)
        {
            CreatedLabels.Add(name);
            return Task.FromResult(new GmailLabel($"Label_{name}", name, false));
        }
    }

    private sealed class FailingUpdateStore : IMailboxMetadataStore
    {
        private readonly IMailboxMetadataStore _inner;
        public FailingUpdateStore(IMailboxMetadataStore inner) => _inner = inner;

        public Task<MailboxScanState?> GetCurrentScanAsync(string user, CancellationToken ct)
            => _inner.GetCurrentScanAsync(user, ct);
        public Task SaveScanStateAsync(MailboxScanState state, CancellationToken ct)
            => _inner.SaveScanStateAsync(state, ct);
        public Task<IReadOnlyList<MailboxMetadata>> GetMetadataAsync(string user, CancellationToken ct)
            => _inner.GetMetadataAsync(user, ct);
        public Task UpsertMetadataAsync(string user, IEnumerable<MailboxMetadata> metadata, CancellationToken ct)
            => _inner.UpsertMetadataAsync(user, metadata, ct);
        public Task ReplaceMetadataAsync(string user, IEnumerable<MailboxMetadata> metadata, CancellationToken ct)
            => _inner.ReplaceMetadataAsync(user, metadata, ct);
        public Task<bool> TryCompleteScanAsync(MailboxScanState state, IEnumerable<MailboxMetadata> metadata, CancellationToken ct)
            => _inner.TryCompleteScanAsync(state, metadata, ct);
        public Task<bool> HasMessageAsync(string user, string id, CancellationToken ct)
            => _inner.HasMessageAsync(user, id, ct);
        public Task ClearAsync(string user, CancellationToken ct) => _inner.ClearAsync(user, ct);
        public bool IsScanStale(MailboxScanState? state, TimeSpan age) => _inner.IsScanStale(state, age);
        public Task UpdateAfterActionAsync(string user, MailboxLocalAction action,
            IReadOnlyCollection<string> ids, string? label, CancellationToken ct)
            => throw new InvalidOperationException("SQLite write failed");
    }
}
