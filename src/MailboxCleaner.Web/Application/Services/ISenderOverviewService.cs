using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Queries;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.Services;

public interface ISenderOverviewService
{
    Task<IReadOnlyList<SenderStatDto>> GetOverviewAsync(GetSenderOverviewQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailItemDto>> GetMailItemsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GmailLabel>> GetLabelsAsync(CancellationToken cancellationToken);
    Task ApplyActionAsync(MailBulkAction action, IReadOnlyCollection<string> messageIds, string? labelId, string? newLabelName, CancellationToken cancellationToken);
}
