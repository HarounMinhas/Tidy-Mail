using System.Net.Mail;
using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Queries;
using MailboxCleaner.Web.Application.Sorting;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.Services;

public sealed class SenderOverviewService : ISenderOverviewService
{
    private readonly IGmailClient _gmailClient;

    public SenderOverviewService(ISenderAggregationService aggregationService, IGmailClient gmailClient) => _gmailClient = gmailClient;

    public async Task<IReadOnlyList<SenderStatDto>> GetOverviewAsync(GetSenderOverviewQuery query, CancellationToken cancellationToken)
    {
        var messages = await GetMailItemsAsync(cancellationToken);
        IEnumerable<SenderStatDto> results = messages.GroupBy(message => message.SenderEmail, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var first = group.First();
            var dates = group.Select(message => message.ReceivedAt).Where(date => date.HasValue).Select(date => date!.Value).ToList();
            return new SenderStatDto(first.SenderEmail, first.SenderName, group.Count(), first.Domain, dates.Count > 0 ? dates.Max() : null, dates.Count > 0 ? dates.Min() : null);
        });

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            results = results.Where(dto => dto.Email.Contains(term, StringComparison.OrdinalIgnoreCase) || dto.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(query.DomainFilter)) results = results.Where(dto => dto.Domain.Equals(query.DomainFilter, StringComparison.OrdinalIgnoreCase));
        if (query.NoreplyOnly) results = results.Where(dto => dto.Email.Contains("noreply", StringComparison.OrdinalIgnoreCase));

        results = query.SortOption switch
        {
            SenderSortOption.CountAsc => results.OrderBy(dto => dto.Count).ThenBy(dto => dto.Email),
            SenderSortOption.SenderNameAsc => results.OrderBy(dto => dto.Name).ThenBy(dto => dto.Email),
            SenderSortOption.SenderNameDesc => results.OrderByDescending(dto => dto.Name).ThenBy(dto => dto.Email),
            SenderSortOption.EmailAsc => results.OrderBy(dto => dto.Email),
            SenderSortOption.EmailDesc => results.OrderByDescending(dto => dto.Email),
            SenderSortOption.NewestMessageAsc => results.OrderBy(dto => dto.NewestMessage),
            SenderSortOption.NewestMessageDesc => results.OrderByDescending(dto => dto.NewestMessage),
            SenderSortOption.OldestMessageAsc => results.OrderBy(dto => dto.OldestMessage),
            SenderSortOption.OldestMessageDesc => results.OrderByDescending(dto => dto.OldestMessage),
            _ => results.OrderByDescending(dto => dto.Count).ThenBy(dto => dto.Email)
        };
        return results.ToList();
    }

    public async Task<IReadOnlyList<MailItemDto>> GetMailItemsAsync(CancellationToken cancellationToken)
    {
        var metadataItems = await _gmailClient.FetchMessageMetadataAsync(cancellationToken);
        return metadataItems.Select(item =>
        {
            var (email, name) = ParseSender(item.FromHeader);
            var domain = email.Contains('@') ? email.Split('@')[1] : string.Empty;
            return new MailItemDto(item.Id, email, name, domain, item.Subject, item.ReceivedAt, item.IsRead, item.HasAttachment, item.Labels.Contains("INBOX", StringComparer.OrdinalIgnoreCase) is false, ResolvePrimaryFolder(item.Labels));
        }).ToList();
    }

    public Task<IReadOnlyList<GmailLabel>> GetLabelsAsync(CancellationToken cancellationToken) => _gmailClient.FetchLabelsAsync(cancellationToken);

    public async Task ApplyActionAsync(MailBulkAction action, IReadOnlyCollection<string> messageIds, string? labelId, string? newLabelName, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case MailBulkAction.Delete: await _gmailClient.TrashMessagesAsync(messageIds, cancellationToken); break;
            case MailBulkAction.Archive: await _gmailClient.ArchiveMessagesAsync(messageIds, cancellationToken); break;
            case MailBulkAction.MarkRead: await _gmailClient.MarkMessagesReadAsync(messageIds, cancellationToken); break;
            case MailBulkAction.MarkUnread: await _gmailClient.MarkMessagesUnreadAsync(messageIds, cancellationToken); break;
            case MailBulkAction.Move:
                var target = !string.IsNullOrWhiteSpace(newLabelName) ? await _gmailClient.CreateLabelAsync(newLabelName, cancellationToken) : null;
                await _gmailClient.MoveMessagesToLabelAsync(messageIds, target?.Id ?? labelId ?? throw new InvalidOperationException("A Gmail label is required."), cancellationToken);
                break;
        }
    }

    private static string ResolvePrimaryFolder(IReadOnlyCollection<string> labels)
    {
        if (labels.Contains("INBOX", StringComparer.OrdinalIgnoreCase)) return "Inbox";
        var userLabel = labels.FirstOrDefault(label => !label.Contains('_') && !new[] { "UNREAD", "SENT", "DRAFT", "TRASH", "SPAM", "STARRED", "IMPORTANT" }.Contains(label, StringComparer.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(userLabel)) return userLabel;
        var systemLabel = labels.FirstOrDefault(label => label.Equals("TRASH", StringComparison.OrdinalIgnoreCase) || label.Equals("SPAM", StringComparison.OrdinalIgnoreCase) || label.Equals("SENT", StringComparison.OrdinalIgnoreCase) || label.Equals("DRAFT", StringComparison.OrdinalIgnoreCase));
        return systemLabel?.ToLowerInvariant() switch { "trash" => "Trash", "spam" => "Spam", "sent" => "Sent", "draft" => "Draft", _ => "Archive" };
    }

    private static (string Email, string Name) ParseSender(string header)
    {
        try { var mailAddress = new MailAddress(header); return (mailAddress.Address, string.IsNullOrWhiteSpace(mailAddress.DisplayName) ? mailAddress.Address : mailAddress.DisplayName); }
        catch { var fallback = header.Trim(); return (fallback, fallback); }
    }
}
