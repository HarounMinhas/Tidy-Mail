using MailboxCleaner.Web.Application.Sorting;

namespace MailboxCleaner.Web.Application.Queries;

public sealed class GetSenderOverviewQuery
{
    public string? SearchTerm { get; set; }
    public string? DomainFilter { get; set; }
    public bool NoreplyOnly { get; set; }
    public SenderSortOption SortOption { get; set; } = SenderSortOption.CountDesc;
}
