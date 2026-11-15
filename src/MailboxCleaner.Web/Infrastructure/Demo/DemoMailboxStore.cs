using System.Collections.Concurrent;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Infrastructure.Demo;

/// <summary>
/// In-memory, synthetic Gmail state. No Google credentials, HTTP calls or real email bodies.
/// State belongs to exactly one of the two predefined demo subjects and resets on restart.
/// </summary>
public sealed class DemoMailboxStore
{
    private readonly ConcurrentDictionary<string, DemoMailbox> _mailboxes = new(StringComparer.Ordinal);

    public DemoMailbox GetMailbox(string userKey)
    {
        if (userKey is not ("user:demo-alice" or "user:demo-bob"))
            throw new UnauthorizedAccessException("Only the dedicated demo accounts can use the dummy mailbox.");

        return _mailboxes.GetOrAdd(userKey, SeedMailbox);
    }

    public void SetFailure(string userKey, string messageId, DemoFailure failure)
    {
        var mailbox = GetMailbox(userKey);
        lock (mailbox.SyncRoot)
        {
            if (!mailbox.Messages.ContainsKey(messageId))
                throw new ArgumentException("Message does not belong to the demo mailbox.", nameof(messageId));
            if (failure == DemoFailure.None) mailbox.Failures.Remove(messageId);
            else mailbox.Failures[messageId] = failure;
        }
    }

    public void Reset(string userKey) => _mailboxes.TryRemove(ValidateUserKey(userKey), out _);

    private static string ValidateUserKey(string key) =>
        key is "user:demo-alice" or "user:demo-bob"
            ? key : throw new UnauthorizedAccessException("Not a demo account.");

    private static DemoMailbox SeedMailbox(string key)
    {
        var mailbox = new DemoMailbox();
        var who = key.EndsWith("alice", StringComparison.Ordinal) ? "Alice" : "Bob";
        var now = DateTimeOffset.UtcNow;
        var senders = new (string Email, string Name, string Subject)[]
        {
            ("digest@news.example.test", "News Digest", "Weekly newsletter"),
            ("noreply@shop.example.test", "Example Shop", "Order confirmation"),
            ("alerts@bank.example.test", "Bank Alerts", "Account notification"),
            ("team@work.example.test", "Project Team", "Project update"),
            ("photos@friends.example.test", "Family Photos", "Photos from the weekend"),
            ("offers@travel.example.test", "Travel Offers", "Holiday promotion")
        };

        mailbox.Labels["INBOX"] = new GmailLabel("INBOX", "Inbox", true);
        mailbox.Labels["UNREAD"] = new GmailLabel("UNREAD", "Unread", true);
        mailbox.Labels["TRASH"] = new GmailLabel("TRASH", "Trash", true);
        mailbox.Labels["SPAM"] = new GmailLabel("SPAM", "Spam", true);
        mailbox.Labels["SENT"] = new GmailLabel("SENT", "Sent", true);
        mailbox.Labels["STARRED"] = new GmailLabel("STARRED", "Starred", true);
        mailbox.Labels["IMPORTANT"] = new GmailLabel("IMPORTANT", "Important", true);
        mailbox.Labels["Label_DemoProjects"] = new GmailLabel("Label_DemoProjects", "Projects", false);
        mailbox.Labels["Label_DemoReceipts"] = new GmailLabel("Label_DemoReceipts", "Receipts", false);
        mailbox.Labels["Label_DemoNews"] = new GmailLabel("Label_DemoNews", "Newsletters", false);

        // Stable identifiers, diverse metadata: read/unread, recent/old, attachments,
        // newsletters, notifications, starred, sent, archived and user labels.
        for (var i = 1; i <= 48; i++)
        {
            var sender = senders[(i - 1) % senders.Length];
            var labels = new List<string>();
            if (i % 4 != 0) labels.Add("INBOX");
            if (i % 3 == 0) labels.Add("UNREAD");
            if (i % 11 == 0) labels.Add("SENT");
            if (i % 7 == 0) labels.Add("STARRED");
            if (i % 13 == 0) labels.Add("IMPORTANT");
            if (i % 5 == 0) labels.Add("Label_DemoProjects");
            if (i % 10 == 0) labels.Add("Label_DemoReceipts");
            if (i % 6 == 1) labels.Add("Label_DemoNews");

            var isNewsletter = sender.Email.StartsWith("digest@", StringComparison.Ordinal);
            var id = $"{who.ToLowerInvariant()}-demo-{i:000}";
            mailbox.Messages[id] = new GmailMessageMetadata(
                id,
                $"{sender.Name} <{sender.Email}>",
                $"{who}: {sender.Subject} #{i}",
                now.AddDays(-i * 19),
                !labels.Contains("UNREAD"),
                i % 5 == 0,
                labels,
                $"demo-thread-{who.ToLowerInvariant()}-{(i + 1) / 2:000}",
                1_024L * (i + 2),
                isNewsletter ? "<mailto:unsubscribe@news.example.test>" : null,
                isNewsletter ? "bulk" : null,
                now);
        }

        return mailbox;
    }

    public sealed class DemoMailbox
    {
        internal object SyncRoot { get; } = new();
        internal Dictionary<string, GmailMessageMetadata> Messages { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, GmailLabel> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, DemoFailure> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal List<(long Revision, string MessageId)> Changes { get; } = new();
        internal long Revision { get; set; } = 1_000;
        internal long NextLabelId { get; set; } = 1;

        internal void RecordChange(string messageId)
        {
            Revision++;
            Changes.Add((Revision, messageId));
        }
    }
}

public enum DemoFailure
{
    None,
    Rejected,
    UncertainAfterApplying
}
