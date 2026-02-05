using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Queries;
using MailboxCleaner.Web.Application.Sorting;

namespace MailboxCleaner.Web.Application.Services;

public sealed class SenderOverviewService : ISenderOverviewService
{
    private readonly ISenderAggregationService _aggregationService;

    public SenderOverviewService(ISenderAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
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
}
