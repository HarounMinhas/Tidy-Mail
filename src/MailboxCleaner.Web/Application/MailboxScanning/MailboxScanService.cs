using System.Net.Mail;
using MailboxCleaner.Web.Infrastructure.Google;

namespace MailboxCleaner.Web.Application.MailboxScanning;

public sealed class MailboxScanService
{
    private readonly IGmailClient _gmailClient;
    private readonly IMailboxMetadataStore _store;
    private readonly MailboxOperationCoordinator _coordinator;

    public MailboxScanService(IGmailClient gmailClient, IMailboxMetadataStore store,
        MailboxOperationCoordinator? coordinator = null)
    {
        _gmailClient = gmailClient;
        _store = store;
        _coordinator = coordinator ?? new MailboxOperationCoordinator();
    }

    public async Task<MailboxScanState> GetOrCreateScanAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await _store.GetCurrentScanAsync(userId, cancellationToken);
        return existing ?? new MailboxScanState { UserId = userId };
    }

    public async Task<MailboxScanState> ScanAsync(string userId, IProgress<MailboxScanState>? progress, CancellationToken cancellationToken)
    {
        using var operation = await _coordinator.EnterAsync(userId, cancellationToken);
        var state = new MailboxScanState { UserId = userId, Status = MailboxScanStatus.Discovering, StatusMessage = "Discovering Gmail messages" };
        await _store.SaveScanStateAsync(state, cancellationToken);
        progress?.Report(state);

        try
        {
            state = state with { Status = MailboxScanStatus.FetchingMetadata, StatusMessage = "Fetching metadata only; message bodies are never requested" };
            await _store.SaveScanStateAsync(state, cancellationToken);
            progress?.Report(state);

            var gmailProgress = new Progress<GmailScanProgress>(update =>
            {
                progress?.Report(state with
                {
                    Status = update.DiscoveryComplete ? MailboxScanStatus.FetchingMetadata : MailboxScanStatus.Discovering,
                    StatusMessage = update.DiscoveryComplete ? "Fetching Gmail metadata" : "Discovering Gmail message IDs",
                    CurrentPage = update.CurrentPage,
                    TotalDiscoveredMessages = update.DiscoveryComplete ? update.DiscoveredMessages : null,
                    ScannedMessages = update.ScannedMessages
                });
            });
            var metadata = await _gmailClient.FetchMessageMetadataAsync(true, gmailProgress, cancellationToken);
            var mapped = metadata.Select(Map).ToList();
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
            if (!await _store.TryCompleteScanAsync(state, mapped, cancellationToken))
                return state with { Status = MailboxScanStatus.Cancelled, StatusMessage = "Scan discarded because the account changed or signed out" };
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
