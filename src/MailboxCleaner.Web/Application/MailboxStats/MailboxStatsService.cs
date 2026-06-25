using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Application.MailboxStats;

public sealed class MailboxStatsService
{
    public MailboxStats Build(IReadOnlyCollection<MailboxMetadata> messages, DateTimeOffset? recentScanDate = null, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var sixMonthsAgo = clock.AddMonths(-6);
        var oneYearAgo = clock.AddYears(-1);
        var list = messages as IList<MailboxMetadata> ?? messages.ToList();

        var topSenders = list.GroupBy(m => m.FromEmail, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new SenderStatsRow(
                    first.FromName,
                    first.FromEmail,
                    first.Domain,
                    g.Count(),
                    g.Count(m => m.IsRead),
                    g.Count(m => !m.IsRead),
                    g.Count(m => m.HasAttachment),
                    g.Max(m => m.ReceivedAt),
                    g.Min(m => m.ReceivedAt),
                    SuggestSenderAction(g));
            })
            .OrderByDescending(s => s.TotalCount)
            .ThenBy(s => s.SenderEmail)
            .Take(25)
            .ToList();

        var topDomains = list.GroupBy(m => m.Domain, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new DomainStatsRow(g.Key, g.Count(), g.Count(m => !m.IsRead)))
            .OrderByDescending(d => d.TotalCount)
            .ThenBy(d => d.Domain)
            .Take(25)
            .ToList();

        return new MailboxStats(
            list.Count,
            list.Count(m => m.IsRead),
            list.Count(m => !m.IsRead),
            list.Count(m => m.HasAttachment),
            list.Count(m => m.ReceivedAt < sixMonthsAgo),
            list.Count(m => m.ReceivedAt < oneYearAgo),
            topSenders,
            topDomains,
            topSenders.Where(s => s.SenderEmail.Contains("noreply", StringComparison.OrdinalIgnoreCase) || s.SenderEmail.Contains("no-reply", StringComparison.OrdinalIgnoreCase)).Take(10).ToList(),
            list.Count(m => m.IsNewsletterLike),
            topDomains.Take(10).Select(d => new CleanupGroupRow($"{d.Domain} domain", d.TotalCount, CleanupRiskLevel.Medium)).ToList(),
            recentScanDate);
    }

    private static string? SuggestSenderAction(IEnumerable<MailboxMetadata> messages)
    {
        var list = messages.ToList();
        if (list.Count >= 25 && list.All(m => m.IsRead)) return "Archive read sender messages";
        if (list.Count >= 10 && list.Any(m => m.IsNewsletterLike)) return "Review newsletter cleanup";
        if (list.Count >= 10 && list.Any(m => m.IsNoreply)) return "Review noreply sender";
        return null;
    }
}

public sealed record MailboxStats(
    int TotalScannedMessages,
    int ReadCount,
    int UnreadCount,
    int MessagesWithAttachments,
    int MessagesOlderThanSixMonths,
    int MessagesOlderThanOneYear,
    IReadOnlyList<SenderStatsRow> TopSenders,
    IReadOnlyList<DomainStatsRow> TopDomains,
    IReadOnlyList<SenderStatsRow> TopNoreplySenders,
    int LikelyNewsletters,
    IReadOnlyList<CleanupGroupRow> LargestCleanupGroups,
    DateTimeOffset? RecentScanDate);

public sealed record SenderStatsRow(string SenderName, string SenderEmail, string Domain, int TotalCount, int ReadCount, int UnreadCount, int AttachmentCount, DateTimeOffset? NewestMessageDate, DateTimeOffset? OldestMessageDate, string? SuggestedCleanupAction);
public sealed record DomainStatsRow(string Domain, int TotalCount, int UnreadCount);
public sealed record CleanupGroupRow(string Name, int MessageCount, CleanupRiskLevel RiskLevel);
