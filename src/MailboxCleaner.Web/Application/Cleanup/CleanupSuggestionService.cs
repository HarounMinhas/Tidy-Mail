using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Application.Cleanup;

public sealed class CleanupSuggestionService
{
    public IReadOnlyList<CleanupSuggestion> Generate(IReadOnlyCollection<MailboxMetadata> messages, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var oneYearAgo = clock.AddYears(-1);
        var suggestions = new List<CleanupSuggestion>();
        var list = messages as IList<MailboxMetadata> ?? messages.ToList();
        if (list.Count == 0) return suggestions;

        AddSuggestion(suggestions, list.Where(m => m.IsRead && m.IsNewsletterLike && m.ReceivedAt < oneYearAgo), "Read newsletters older than 1 year", "These look like bulk/newsletter messages you have already read.", CleanupRiskLevel.Low, "Archive selected");
        AddSuggestion(suggestions, list.Where(m => m.IsNoreply).GroupBy(m => m.FromEmail).Where(g => g.Count() >= 10).SelectMany(g => g), "High-volume noreply senders", "Noreply senders often contain automated messages that are safe to review in bulk.", CleanupRiskLevel.Medium, "Archive selected");
        AddSuggestion(suggestions, list.Where(m => m.IsNotificationLike && m.IsRead).GroupBy(m => m.FromEmail).Where(g => g.Count() >= 10).SelectMany(g => g), "Notification senders with many read messages", "These read notifications may be good candidates for archiving.", CleanupRiskLevel.Low, "Archive selected");
        AddSuggestion(suggestions, list.Where(m => !m.IsRead && m.ReceivedAt < oneYearAgo), "Old unread messages", "Unread messages older than a year can hide important mail, so review them carefully.", CleanupRiskLevel.High, "Preview selected");
        AddSuggestion(suggestions, list.Where(m => m.IsRead && m.ReceivedAt < oneYearAgo), "Old read messages", "Read messages older than a year are common cleanup candidates.", CleanupRiskLevel.Medium, "Archive selected");

        var bulkSender = list.GroupBy(m => m.FromEmail).Where(g => g.Count() >= Math.Max(10, list.Count / 10)).SelectMany(g => g);
        AddSuggestion(suggestions, bulkSender, "Bulk senders causing large mailbox volume", "A few senders account for a large share of the mailbox.", CleanupRiskLevel.Medium, "Preview selected");

        var bulkDomain = list.GroupBy(m => m.Domain).Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() >= Math.Max(10, list.Count / 10)).SelectMany(g => g);
        AddSuggestion(suggestions, bulkDomain, "Domains with high message volume", "These domains appear frequently and may be worth filtering or archiving.", CleanupRiskLevel.Medium, "Preview selected");

        return suggestions.OrderByDescending(s => s.MessageCount).ThenBy(s => s.RiskLevel).ToList();
    }

    private static void AddSuggestion(List<CleanupSuggestion> suggestions, IEnumerable<MailboxMetadata> query, string title, string explanation, CleanupRiskLevel risk, string action)
    {
        var messages = query.DistinctBy(m => m.MessageId).ToList();
        if (messages.Count == 0) return;
        suggestions.Add(new CleanupSuggestion(title, explanation, risk, messages.Count, CleanupFilterCriteria.FromMessages(messages), action));
    }
}

public enum CleanupRiskLevel { Low, Medium, High }
public sealed record CleanupSuggestion(string Title, string Explanation, CleanupRiskLevel RiskLevel, int MessageCount, CleanupFilterCriteria FilterCriteria, string SuggestedAction);
public sealed record CleanupFilterCriteria(IReadOnlyCollection<string> MessageIds, string? Sender = null, string? Domain = null, bool? IsRead = null, bool? IsNewsletterLike = null)
{
    public static CleanupFilterCriteria FromMessages(IReadOnlyCollection<MailboxMetadata> messages)
    {
        var sender = messages.Select(m => m.FromEmail).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
        var domains = messages.Select(m => m.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
        return new CleanupFilterCriteria(messages.Select(m => m.MessageId).ToList(), sender.Count == 1 ? sender[0] : null, domains.Count == 1 ? domains[0] : null);
    }
}
