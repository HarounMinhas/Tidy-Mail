using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.Filtering;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;

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
}
