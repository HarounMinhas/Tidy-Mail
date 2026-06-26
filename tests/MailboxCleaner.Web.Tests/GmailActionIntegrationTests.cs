using MailboxCleaner.Web.Application.Services;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Tests;

public sealed class GmailActionIntegrationTests
{
    [Fact]
    public async Task DeleteArchiveReadUnreadMoveAndCreateLabel_CallGmailClientAndInvalidateLocalOnlyFlow()
    {
        var gmail = new RecordingGmailClient();
        var service = new SenderOverviewService(new SenderAggregationService(gmail), gmail);
        var ids = new[] { "m1", "m2" };

        await service.ApplyActionAsync(MailBulkAction.Delete, ids, null, null, CancellationToken.None);
        await service.ApplyActionAsync(MailBulkAction.Archive, ids, null, null, CancellationToken.None);
        await service.ApplyActionAsync(MailBulkAction.MarkRead, ids, null, null, CancellationToken.None);
        await service.ApplyActionAsync(MailBulkAction.MarkUnread, ids, null, null, CancellationToken.None);
        await service.ApplyActionAsync(MailBulkAction.Move, ids, "Label_Existing", null, CancellationToken.None);
        await service.ApplyActionAsync(MailBulkAction.Move, ids, null, "Projects", CancellationToken.None);

        Assert.Equal(2, gmail.Trashed.Count);
        Assert.Equal(2, gmail.Archived.Count);
        Assert.Equal(2, gmail.Read.Count);
        Assert.Equal(2, gmail.Unread.Count);
        Assert.Contains("Label_Existing", gmail.MovedToLabels);
        Assert.Contains("Projects", gmail.CreatedLabels);
        Assert.Contains("Label_Projects", gmail.MovedToLabels);
    }


    [Fact]
    public async Task GetMailItemsAsync_MapsGmailLabelIdsToDisplayNames()
    {
        var gmail = new RecordingGmailClient
        {
            Metadata =
            [
                new GmailMessageMetadata("m1", "Projects <projects@example.com>", "Project update", DateTimeOffset.Parse("2026-06-25T00:00:00Z"), true, false, ["Label_Projects"])
            ],
            Labels = [new GmailLabel("Label_Projects", "Projects", false)]
        };
        var service = new SenderOverviewService(new SenderAggregationService(gmail), gmail);

        var items = await service.GetMailItemsAsync(CancellationToken.None);

        Assert.Collection(items, item => Assert.Equal("Projects", item.Folder));
    }

    private sealed class RecordingGmailClient : IGmailClient
    {
        public List<string> Trashed { get; } = new();
        public List<string> Archived { get; } = new();
        public List<string> Read { get; } = new();
        public List<string> Unread { get; } = new();
        public List<string> MovedToLabels { get; } = new();
        public List<string> CreatedLabels { get; } = new();
        public IReadOnlyList<GmailMessageMetadata> Metadata { get; init; } = Array.Empty<GmailMessageMetadata>();
        public IReadOnlyList<GmailLabel> Labels { get; init; } = Array.Empty<GmailLabel>();

        public Task<IReadOnlyList<string>> FetchFromHeadersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<IReadOnlyList<GmailMessageMetadata>> FetchMessageMetadataAsync(CancellationToken cancellationToken) => Task.FromResult(Metadata);
        public Task<IReadOnlyList<GmailLabel>> FetchLabelsAsync(CancellationToken cancellationToken) => Task.FromResult(Labels);
        public Task<GmailActionResult> TrashMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) { Trashed.AddRange(messageIds); return Task.FromResult(GmailActionResult.Success(messageIds)); }
        public Task<GmailActionResult> ArchiveMessagesAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) { Archived.AddRange(messageIds); return Task.FromResult(GmailActionResult.Success(messageIds)); }
        public Task<GmailActionResult> MarkMessagesReadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) { Read.AddRange(messageIds); return Task.FromResult(GmailActionResult.Success(messageIds)); }
        public Task<GmailActionResult> MarkMessagesUnreadAsync(IReadOnlyCollection<string> messageIds, CancellationToken cancellationToken) { Unread.AddRange(messageIds); return Task.FromResult(GmailActionResult.Success(messageIds)); }
        public Task<GmailActionResult> MoveMessagesToLabelAsync(IReadOnlyCollection<string> messageIds, string labelId, CancellationToken cancellationToken) { MovedToLabels.Add(labelId); return Task.FromResult(GmailActionResult.Success(messageIds)); }
        public Task<GmailLabel> CreateLabelAsync(string labelName, CancellationToken cancellationToken) { CreatedLabels.Add(labelName); return Task.FromResult(new GmailLabel($"Label_{labelName}", labelName, false)); }
    }
}
