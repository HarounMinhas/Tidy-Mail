using MailboxCleaner.Web.Domain;

namespace MailboxCleaner.Web.Application.Services;

public interface ISenderAggregationService
{
    Task<IReadOnlyList<SenderStat>> BuildSenderStatsAsync(CancellationToken cancellationToken);
}
