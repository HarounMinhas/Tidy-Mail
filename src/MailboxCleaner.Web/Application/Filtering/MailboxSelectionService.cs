using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Application.Filtering;

public sealed class MailboxSelectionService
{
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> SelectedMessageIds => _selected;
    public void SelectVisible(IEnumerable<MailboxMetadata> visible) => SelectMany(visible.Select(m => m.MessageId));
    public void SelectAllMatching(IEnumerable<MailboxMetadata> matching) => SelectMany(matching.Select(m => m.MessageId));
    public void SelectSender(IEnumerable<MailboxMetadata> messages, string senderEmail) => SelectMany(messages.Where(m => m.FromEmail.Equals(senderEmail, StringComparison.OrdinalIgnoreCase)).Select(m => m.MessageId));
    public void Deselect(string messageId) => _selected.Remove(messageId);
    public void Toggle(string messageId, bool selected) { if (selected) _selected.Add(messageId); else _selected.Remove(messageId); }
    public void Clear() => _selected.Clear();
    private void SelectMany(IEnumerable<string> ids) { foreach (var id in ids) _selected.Add(id); }
}
