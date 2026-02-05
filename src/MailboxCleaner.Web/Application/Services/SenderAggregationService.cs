using System.Net.Mail;
using MailboxCleaner.Web.Domain;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.Services;

public sealed class SenderAggregationService : ISenderAggregationService
{
    private readonly IGmailClient _gmailClient;

    public SenderAggregationService(IGmailClient gmailClient)
    {
        _gmailClient = gmailClient;
    }

    public async Task<IReadOnlyList<SenderStat>> BuildSenderStatsAsync(CancellationToken cancellationToken)
    {
        var fromHeaders = await _gmailClient.FetchFromHeadersAsync(cancellationToken);
        var grouped = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in fromHeaders)
        {
            var (email, name) = ParseSender(header);
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            if (grouped.TryGetValue(email, out var current))
            {
                grouped[email] = (current.Name, current.Count + 1);
            }
            else
            {
                grouped[email] = (name, 1);
            }
        }

        return grouped.Select(kvp => new SenderStat(new SenderId(kvp.Key.ToLowerInvariant()), kvp.Value.Name, kvp.Value.Count))
            .OrderByDescending(stat => stat.Count)
            .ToList();
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
