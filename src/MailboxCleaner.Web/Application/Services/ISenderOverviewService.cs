using MailboxCleaner.Web.Application.DTOs;
using MailboxCleaner.Web.Application.Queries;

namespace MailboxCleaner.Web.Application.Services;

public interface ISenderOverviewService
{
    Task<IReadOnlyList<SenderStatDto>> GetOverviewAsync(GetSenderOverviewQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<MailItemDto>> GetMailItemsAsync(CancellationToken cancellationToken);
}
