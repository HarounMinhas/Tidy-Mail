using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Queries;
using MailboxCleaner.Web.Application.Sorting;
using MailboxCleaner.Web.Infrastructure.Google;
using System.Net.Mail;

namespace MailboxCleaner.Web.Application.Services;

public sealed class SenderOverviewService : ISenderOverviewService
{
    private readonly ISenderAggregationService _aggregationService;
    private readonly IGmailClient _gmailClient;

    public SenderOverviewService(ISenderAggregationService aggregationService, IGmailClient gmailClient)
    {
        _aggregationService = aggregationService;
        _gmailClient = gmailClient;
    }

    public async Task<IReadOnlyList<SenderStatDto>> GetOverviewAsync(GetSenderOverviewQuery query, CancellationToken cancellationToken)
    {
        var stats = await _aggregationService.BuildSenderStatsAsync(cancellationToken);

        IEnumerable<SenderStatDto> results = stats.Select(stat => new SenderStatDto(
            stat.SenderId.Email,
            stat.DisplayName,
            stat.Count,
            stat.SenderId.Domain));

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            results = results.Where(dto =>
                dto.Email.Contains(term, StringComparison.OrdinalIgnoreCase)
                || dto.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.DomainFilter))
        {
            results = results.Where(dto => dto.Domain.Equals(query.DomainFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (query.NoreplyOnly)
        {
            results = results.Where(dto => dto.Email.Contains("noreply", StringComparison.OrdinalIgnoreCase));
        }

        results = query.SortOption switch
        {
            SenderSortOption.CountAsc => results.OrderBy(dto => dto.Count),
            SenderSortOption.SenderAsc => results.OrderBy(dto => dto.Email),
            SenderSortOption.SenderDesc => results.OrderByDescending(dto => dto.Email),
            _ => results.OrderByDescending(dto => dto.Count)
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
            return new MailItemDto(
                item.Id,
                email,
                name,
                domain,
                item.Subject,
                item.ReceivedAt,
                item.IsRead);
        }).ToList();
    }

    private static (string Email, string Name) ParseSender(string header)
    {
        try
        {
            var mailAddress = new MailAddress(header);
            var name = string.IsNullOrWhiteSpace(mailAddress.DisplayName)
                ? mailAddress.Address
                : mailAddress.DisplayName;
            return (mailAddress.Address, name);
        }
        catch
        {
            var fallback = header.Trim();
            return (fallback, fallback);
        }
    }
}
