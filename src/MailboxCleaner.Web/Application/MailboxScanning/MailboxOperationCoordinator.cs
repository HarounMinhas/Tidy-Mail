using System.Collections.Concurrent;

namespace MailboxCleaner.Web.Application.MailboxScanning;

/// <summary>Serializes scans and write actions for the same Google mailbox.</summary>
public sealed class MailboxOperationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId) || !userId.StartsWith("user:", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Mailbox operations require a validated account.");
        }
        var semaphore = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Lease(semaphore);
    }

    private sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;
        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Release();
        }
    }
}
