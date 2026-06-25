using System.Net.Mail;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.MailboxScanning;

public sealed class MailboxScanService
{
    private readonly IGmailClient _gmailClient;
    private readonly IMailboxMetadataStore _store;

    public MailboxScanService(IGmailClient gmailClient, IMailboxMetadataStore store)
    {
        _gmailClient = gmailClient;
        _store = store;
    }

    public async Task<MailboxScanState> GetOrCreateScanAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await _store.GetCurrentScanAsync(userId, cancellationToken);
        return existing ?? new MailboxScanState { UserId = userId };
    }

    public async Task<MailboxScanState> ScanAsync(string userId, IProgress<MailboxScanState>? progress, CancellationToken cancellationToken)
    {
        var state = new MailboxScanState { UserId = userId, Status = MailboxScanStatus.Discovering, StatusMessage = "Discovering Gmail messages" };
        await _store.SaveScanStateAsync(state, cancellationToken);
        progress?.Report(state);

        try
        {
            state = state with { Status = MailboxScanStatus.FetchingMetadata, StatusMessage = "Fetching metadata only; message bodies are never requested" };
            await _store.SaveScanStateAsync(state, cancellationToken);
            progress?.Report(state);

            var metadata = await _gmailClient.FetchMessageMetadataAsync(cancellationToken);
            var mapped = metadata.Select(Map).ToList();
            await _store.ReplaceMetadataAsync(userId, mapped, cancellationToken);

            state = state with
            {
                Status = MailboxScanStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
                TotalDiscoveredMessages = mapped.Count,
                ScannedMessages = mapped.Count,
                CurrentPage = mapped.Count == 0 ? 0 : (int)Math.Ceiling(mapped.Count / 500m),
                StatusMessage = "Mailbox scan complete",
                CachedMetadata = mapped
            };
            await _store.SaveScanStateAsync(state, cancellationToken);
            progress?.Report(state);
            return state;
        }
        catch (OperationCanceledException)
        {
            state = state with { Status = MailboxScanStatus.Cancelled, StatusMessage = "Mailbox scan cancelled", CachedMetadata = await _store.GetMetadataAsync(userId, CancellationToken.None) };
            await _store.SaveScanStateAsync(state, CancellationToken.None);
            progress?.Report(state);
            return state;
        }
        catch (Exception ex)
        {
            state = state with { Status = MailboxScanStatus.Failed, StatusMessage = "Mailbox scan failed", ErrorMessage = ex.Message, CachedMetadata = await _store.GetMetadataAsync(userId, CancellationToken.None) };
            await _store.SaveScanStateAsync(state, CancellationToken.None);
            progress?.Report(state);
            return state;
        }
    }

    public Task ClearScanAsync(string userId, CancellationToken cancellationToken) => _store.ClearAsync(userId, cancellationToken);

    public static MailboxMetadata Map(GmailMessageMetadata item)
    {
        var (email, name) = ParseSender(item.FromHeader);
        var domain = email.Contains('@') ? email.Split('@')[^1] : string.Empty;
        return new MailboxMetadata(
            item.Id,
            item.ThreadId ?? string.Empty,
            name,
            email,
            domain,
            item.Subject,
            item.ReceivedAt,
            item.IsRead,
            item.Labels,
            item.HasAttachment,
            item.SizeEstimate,
            item.ScannedAt ?? DateTimeOffset.UtcNow,
            item.ListUnsubscribe,
            item.Precedence);
    }

    private static (string Email, string Name) ParseSender(string header)
    {
        try
        {
            var mailAddress = new MailAddress(header);
            return (mailAddress.Address, string.IsNullOrWhiteSpace(mailAddress.DisplayName) ? mailAddress.Address : mailAddress.DisplayName);
        }
        catch
        {
            var fallback = header.Trim();
            return (fallback, fallback);
        }
    }
}
