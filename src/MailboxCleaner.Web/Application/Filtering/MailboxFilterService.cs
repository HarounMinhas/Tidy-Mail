using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Application.Filtering;

public sealed class MailboxFilterService
{
    public IReadOnlyList<MailboxMetadata> Apply(IEnumerable<MailboxMetadata> messages, MailboxFilter filter, MailboxSortOption sort = MailboxSortOption.NewestMessage)
    {
        var results = messages;
        if (!string.IsNullOrWhiteSpace(filter.Sender)) results = results.Where(m => m.FromEmail.Contains(filter.Sender, StringComparison.OrdinalIgnoreCase) || m.FromName.Contains(filter.Sender, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Domain)) results = results.Where(m => m.Domain.Contains(filter.Domain, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.SubjectKeyword)) results = results.Where(m => m.Subject.Contains(filter.SubjectKeyword, StringComparison.OrdinalIgnoreCase));
        if (filter.IsRead.HasValue) results = results.Where(m => m.IsRead == filter.IsRead.Value);
        if (filter.HasAttachment.HasValue) results = results.Where(m => m.HasAttachment == filter.HasAttachment.Value);
        if (filter.OlderThanSixMonthsFrom.HasValue) results = results.Where(m => m.ReceivedAt < filter.OlderThanSixMonthsFrom.Value.AddMonths(-6));
        if (filter.OlderThanOneYearFrom.HasValue) results = results.Where(m => m.ReceivedAt < filter.OlderThanOneYearFrom.Value.AddYears(-1));
        if (!string.IsNullOrWhiteSpace(filter.Label)) results = results.Where(m => m.Labels.Contains(filter.Label, StringComparer.OrdinalIgnoreCase));
        if (filter.NoreplyOnly) results = results.Where(m => m.IsNoreply);
        if (filter.NewsletterLikeOnly) results = results.Where(m => m.IsNewsletterLike);
        if (filter.NotificationLikeOnly) results = results.Where(m => m.IsNotificationLike);

        return Sort(results, sort).ToList();
    }

    public IEnumerable<MailboxMetadata> Sort(IEnumerable<MailboxMetadata> messages, MailboxSortOption sort) => sort switch
    {
        MailboxSortOption.SenderNameAscending => messages.OrderBy(m => m.FromName).ThenBy(m => m.FromEmail),
        MailboxSortOption.SenderNameDescending => messages.OrderByDescending(m => m.FromName).ThenBy(m => m.FromEmail),
        MailboxSortOption.EmailAscending => messages.OrderBy(m => m.FromEmail),
        MailboxSortOption.EmailDescending => messages.OrderByDescending(m => m.FromEmail),
        MailboxSortOption.OldestMessage => messages.OrderBy(m => m.ReceivedAt),
        MailboxSortOption.UnreadCountDescending => messages.OrderBy(m => m.IsRead).ThenByDescending(m => m.ReceivedAt),
        MailboxSortOption.AttachmentCountDescending => messages.OrderByDescending(m => m.HasAttachment).ThenByDescending(m => m.ReceivedAt),
        _ => messages.OrderByDescending(m => m.ReceivedAt)
    };
}

public sealed record MailboxFilter
{
    public string? Sender { get; init; }
    public string? Domain { get; init; }
    public string? SubjectKeyword { get; init; }
    public bool? IsRead { get; init; }
    public bool? HasAttachment { get; init; }
    public DateTimeOffset? OlderThanSixMonthsFrom { get; init; }
    public DateTimeOffset? OlderThanOneYearFrom { get; init; }
    public string? Label { get; init; }
    public bool NoreplyOnly { get; init; }
    public bool NewsletterLikeOnly { get; init; }
    public bool NotificationLikeOnly { get; init; }
}

public enum MailboxSortOption
{
    SenderNameAscending,
    SenderNameDescending,
    EmailAscending,
    EmailDescending,
    MessageCountAscending,
    MessageCountDescending,
    NewestMessage,
    OldestMessage,
    UnreadCountDescending,
    AttachmentCountDescending
}
