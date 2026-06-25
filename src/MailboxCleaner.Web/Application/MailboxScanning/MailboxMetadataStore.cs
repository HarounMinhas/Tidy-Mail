using System.Collections.Concurrent;

namespace MailboxCleaner.Web.Application.MailboxScanning;

public sealed class MailboxMetadataStore : IMailboxMetadataStore
{
    private sealed class UserMailboxCache
    {
        public MailboxScanState? State { get; set; }
        public Dictionary<string, MailboxMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<string, UserMailboxCache> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<MailboxScanState?> GetCurrentScanAsync(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_cache.TryGetValue(userId, out var cache) ? cache.State : null);
    }

    public Task SaveScanStateAsync(MailboxScanState state, CancellationToken cancellationToken)
    {
        var cache = _cache.GetOrAdd(state.UserId, _ => new UserMailboxCache());
        lock (cache)
        {
            cache.State = state with { CachedMetadata = cache.Metadata.Values.OrderByDescending(m => m.ReceivedAt).ToList() };
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MailboxMetadata>> GetMetadataAsync(string userId, CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(userId, out var cache)) return Task.FromResult<IReadOnlyList<MailboxMetadata>>(Array.Empty<MailboxMetadata>());
        lock (cache)
        {
            return Task.FromResult<IReadOnlyList<MailboxMetadata>>(cache.Metadata.Values.OrderByDescending(m => m.ReceivedAt).ToList());
        }
    }

    public Task UpsertMetadataAsync(string userId, IEnumerable<MailboxMetadata> metadata, CancellationToken cancellationToken)
    {
        var cache = _cache.GetOrAdd(userId, _ => new UserMailboxCache());
        lock (cache)
        {
            foreach (var item in metadata)
            {
                cache.Metadata[item.MessageId] = item;
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> HasMessageAsync(string userId, string messageId, CancellationToken cancellationToken)
    {
        var exists = _cache.TryGetValue(userId, out var cache) && cache.Metadata.ContainsKey(messageId);
        return Task.FromResult(exists);
    }

    public Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        _cache.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task UpdateAfterActionAsync(string userId, MailboxLocalAction action, IReadOnlyCollection<string> successfulMessageIds, string? labelId, CancellationToken cancellationToken)
    {
        if (!_cache.TryGetValue(userId, out var cache) || successfulMessageIds.Count == 0) return Task.CompletedTask;
        var ids = successfulMessageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (cache)
        {
            foreach (var id in ids)
            {
                if (!cache.Metadata.TryGetValue(id, out var message)) continue;
                var labels = message.Labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                switch (action)
                {
                    case MailboxLocalAction.Trash:
                        labels.Remove("INBOX"); labels.Add("TRASH");
                        break;
                    case MailboxLocalAction.Archive:
                        labels.Remove("INBOX");
                        break;
                    case MailboxLocalAction.MarkRead:
                        labels.Remove("UNREAD");
                        break;
                    case MailboxLocalAction.MarkUnread:
                        labels.Add("UNREAD");
                        break;
                    case MailboxLocalAction.MoveToLabel:
                        if (!string.IsNullOrWhiteSpace(labelId)) labels.Add(labelId);
                        labels.Remove("INBOX");
                        break;
                }
                cache.Metadata[id] = message with { Labels = labels.ToList(), IsRead = !labels.Contains("UNREAD") };
            }
        }
        return Task.CompletedTask;
    }

    public bool IsScanStale(MailboxScanState? state, TimeSpan maxAge)
        => state?.CompletedAt is null || DateTimeOffset.UtcNow - state.CompletedAt.Value > maxAge;
}
