using MailboxCleaner.Web.Application.DTOs;

namespace MailboxCleaner.Web.Application.Services;

public sealed class MailManagementEngine
{
    private readonly List<MailItemDto> _messages;
    private readonly HashSet<string> _selectedMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedSenders = new(StringComparer.OrdinalIgnoreCase);

    public MailManagementEngine(IEnumerable<MailItemDto> messages)
    {
        _messages = messages.ToList();
    }

    public IReadOnlyCollection<string> SelectedMessageIds => _selectedMessageIds;
    public IReadOnlyCollection<string> ExpandedSenders => _expandedSenders;

    public IEnumerable<IGrouping<string, MailItemDto>> GroupedBySender =>
        _messages.GroupBy(message => message.SenderEmail)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    public void ToggleSenderExpansion(string senderEmail)
    {
        if (!_expandedSenders.Add(senderEmail))
        {
            _expandedSenders.Remove(senderEmail);
        }
    }

    public bool IsSenderFullySelected(string senderEmail)
    {
        var ids = _messages.Where(message => message.SenderEmail.Equals(senderEmail, StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Id)
            .ToList();

        return ids.Count > 0 && ids.All(id => _selectedMessageIds.Contains(id));
    }

    public void ToggleSenderSelection(string senderEmail, bool selected)
    {
        var ids = _messages.Where(message => message.SenderEmail.Equals(senderEmail, StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Id);

        foreach (var id in ids)
        {
            if (selected)
            {
                _selectedMessageIds.Add(id);
            }
            else
            {
                _selectedMessageIds.Remove(id);
            }
        }
    }

    public void ToggleMessageSelection(string id, bool selected)
    {
        if (selected)
        {
            _selectedMessageIds.Add(id);
            return;
        }

        _selectedMessageIds.Remove(id);
    }

    public IReadOnlyList<MailItemDto> FilterMessages(string senderOrKeyword, string status, string folder, bool? hasAttachment)
    {
        IEnumerable<MailItemDto> results = _messages;

        if (!string.IsNullOrWhiteSpace(senderOrKeyword))
        {
            results = results.Where(message =>
                message.SenderEmail.Contains(senderOrKeyword, StringComparison.OrdinalIgnoreCase)
                || message.Subject.Contains(senderOrKeyword, StringComparison.OrdinalIgnoreCase)
                || message.SenderName.Contains(senderOrKeyword, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            results = status.Trim().ToLowerInvariant() switch
            {
                "read" => results.Where(message => message.IsRead),
                "unread" => results.Where(message => !message.IsRead),
                _ => results
            };
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            results = results.Where(message => message.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
        }

        if (hasAttachment.HasValue)
        {
            results = results.Where(message => message.HasAttachment == hasAttachment.Value);
        }

        return results.OrderByDescending(message => message.ReceivedAt).ToList();
    }

    public IEnumerable<string> AutocompleteSuggestions(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return _messages
                .SelectMany(message => new[] { message.SenderEmail, message.Subject })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .Take(10);
        }

        return _messages
            .SelectMany(message => new[] { message.SenderEmail, message.Subject })
            .Where(value => value.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Take(10);
    }

    public ActionPreview PreviewAction(MailBulkAction action, string? destinationFolder = null)
    {
        var impacted = _messages.Where(message => _selectedMessageIds.Contains(message.Id)).ToList();
        return new ActionPreview(action, impacted.Count, destinationFolder ?? string.Empty, impacted.Select(m => m.Id).ToList());
    }

    public void ApplyAction(MailBulkAction action, string? destinationFolder = null)
    {
        var selected = _messages.Where(message => _selectedMessageIds.Contains(message.Id)).ToList();

        foreach (var message in selected)
        {
            var index = _messages.FindIndex(item => item.Id.Equals(message.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            _messages[index] = action switch
            {
                MailBulkAction.MarkRead => message with { IsRead = true },
                MailBulkAction.MarkUnread => message with { IsRead = false },
                MailBulkAction.Archive => message with { IsArchived = true, Folder = "Archive" },
                MailBulkAction.Move => message with { Folder = string.IsNullOrWhiteSpace(destinationFolder) ? message.Folder : destinationFolder },
                MailBulkAction.Delete => message with { Folder = "Trash" },
                _ => message
            };
        }
    }
}

public enum MailBulkAction
{
    Delete,
    Archive,
    Move,
    MarkRead,
    MarkUnread
}

public sealed record ActionPreview(MailBulkAction Action, int SelectedCount, string DestinationFolder, IReadOnlyList<string> MessageIds);
