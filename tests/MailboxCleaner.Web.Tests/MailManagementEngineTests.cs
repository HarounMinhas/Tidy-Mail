using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Services;

namespace MailboxCleaner.Web.Tests;

public sealed class MailManagementEngineTests
{
    [Fact]
    public void ParentSelection_SelectsAndUnselectsAllSenderMessages_ButKeepsIndividualControl()
    {
        var engine = new MailManagementEngine(CreateDummyMessages());

        engine.ToggleSenderSelection("alpha@example.com", true);
        Assert.True(engine.IsSenderFullySelected("alpha@example.com"));

        engine.ToggleMessageSelection("m2", false);
        Assert.False(engine.IsSenderFullySelected("alpha@example.com"));
        Assert.Contains("m1", engine.SelectedMessageIds);
        Assert.DoesNotContain("m2", engine.SelectedMessageIds);

        engine.ToggleSenderSelection("alpha@example.com", false);
        Assert.DoesNotContain("m1", engine.SelectedMessageIds);
    }

    [Fact]
    public void SenderSelection_OnlySelectsVisibleMessagesWhenFiltered()
    {
        var engine = new MailManagementEngine(CreateDummyMessages());
        var visible = engine.FilterMessages("", "Unread", "", null)
            .Where(m => m.SenderEmail == "alpha@example.com")
            .Select(m => m.Id).ToList();

        engine.ToggleSenderSelection("alpha@example.com", true, visible);

        Assert.Contains("m1", engine.SelectedMessageIds);
        Assert.DoesNotContain("m2", engine.SelectedMessageIds);
        Assert.True(engine.IsSenderFullySelected("alpha@example.com", visible));
        Assert.False(engine.IsSenderFullySelected("alpha@example.com"));
        Assert.Single(engine.PreviewAction(MailBulkAction.Delete).MessageIds);
    }

    [Fact]
    public void Filters_WorkForUnreadReadFolderAttachmentAndKeyword()
    {
        var engine = new MailManagementEngine(CreateDummyMessages());

        var unread = engine.FilterMessages("", "Unread", "", null);
        Assert.All(unread, message => Assert.False(message.IsRead));

        var sentFolder = engine.FilterMessages("", "", "Sent", null);
        Assert.All(sentFolder, message => Assert.Equal("Sent", message.Folder));

        var withAttachments = engine.FilterMessages("", "", "", true);
        Assert.All(withAttachments, message => Assert.True(message.HasAttachment));

        var keyword = engine.FilterMessages("invoice", "", "", null);
        Assert.All(keyword, message => Assert.Contains("invoice", message.Subject, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtendedFilters_UseAgeClassificationAndGmailLabelIds()
    {
        var messages = CreateDummyMessages().ToList();
        messages.Add(new MailItemDto("m6", "news@example.com", "News", "example.com",
            "Weekly digest", new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            true, false, true, "News", true, false, ["Label_News"]));
        var engine = new MailManagementEngine(messages);

        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var result = engine.FilterMessages("", "", "", null,
            "1y", newsletterOnly: true, labelId: "Label_News", clock: now);

        Assert.Single(result);
        Assert.Equal("m6", result.Single().Id);
        Assert.Empty(engine.FilterMessages("", "", "", null,
            notificationOnly: true, labelId: "Label_News", clock: now));
    }

    [Fact]
    public void Autocomplete_ReturnsSenderAndSubjectSuggestions_AndHandlesEmptyInput()
    {
        var engine = new MailManagementEngine(CreateDummyMessages());

        var specific = engine.AutocompleteSuggestions("alpha").ToList();
        Assert.Contains("alpha@example.com", specific);

        var empty = engine.AutocompleteSuggestions("").ToList();
        Assert.NotEmpty(empty);
        Assert.True(empty.Count <= 10);
    }

    [Fact]
    public void PreviewAndApplyActions_UpdateSelectedMessages_ForEdgeCases()
    {
        var engine = new MailManagementEngine(CreateDummyMessages());
        engine.ToggleMessageSelection("m3", true);
        engine.ToggleMessageSelection("m4", true);

        var preview = engine.PreviewAction(MailBulkAction.Move, "Projects");
        Assert.Equal(2, preview.SelectedCount);
        Assert.Equal("Projects", preview.DestinationFolder);

        engine.ApplyAction(MailBulkAction.MarkUnread);
        var unreadAfterMark = engine.FilterMessages("", "Unread", "", null);
        Assert.Contains(unreadAfterMark, message => message.Id == "m3");

        engine.ApplyAction(MailBulkAction.Move, "Projects");
        var moved = engine.FilterMessages("", "", "Projects", null);
        Assert.Contains(moved, message => message.Id == "m3");
        Assert.Contains(moved, message => message.Id == "m4");

        engine.ApplyAction(MailBulkAction.Delete);
        var trash = engine.FilterMessages("", "", "Trash", null);
        Assert.Contains(trash, message => message.Id == "m3");

        engine.ApplyAction(MailBulkAction.Archive);
        var archive = engine.FilterMessages("", "", "Archive", null);
        Assert.Contains(archive, message => message.Id == "m3");
    }

    private static IReadOnlyList<MailItemDto> CreateDummyMessages() =>
    [
        new("m1", "alpha@example.com", "Alpha", "example.com", "Invoice April", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), false, true, false, "Inbox"),
        new("m2", "alpha@example.com", "Alpha", "example.com", "Meeting Notes", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), true, false, false, "Inbox"),
        new("m3", "beta@example.com", "Beta", "example.com", "Invoice May", new DateTimeOffset(2024, 12, 21, 0, 0, 0, TimeSpan.Zero), true, true, false, "Sent"),
        new("m4", "gamma@corp.com", "Gamma", "corp.com", "Release Plan", null, false, false, false, "Inbox"),
        new("m5", "noreply@service.com", "Service", "service.com", "System Alert", new DateTimeOffset(2025, 2, 8, 0, 0, 0, TimeSpan.Zero), false, true, true, "Archive")
    ];
}
