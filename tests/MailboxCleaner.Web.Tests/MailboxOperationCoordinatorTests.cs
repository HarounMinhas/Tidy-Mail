using MailboxCleaner.Web.Application.MailboxScanning;

namespace MailboxCleaner.Web.Tests;

public sealed class MailboxOperationCoordinatorTests
{
    [Fact]
    public async Task SameAccountOperations_AreSerialized_ButSeparateAccountsMayProceed()
    {
        var coordinator = new MailboxOperationCoordinator();
        using var first = await coordinator.EnterAsync("user:alice", CancellationToken.None);

        var competing = coordinator.EnterAsync("user:alice", CancellationToken.None);
        Assert.False(competing.IsCompleted);

        using var otherAccount = await coordinator.EnterAsync("user:bob", CancellationToken.None);
        Assert.False(competing.IsCompleted);

        first.Dispose();
        using var resumed = await competing;
        Assert.True(competing.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InvalidAccountCannotEnterTheMailboxCriticalSection()
    {
        var coordinator = new MailboxOperationCoordinator();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => coordinator.EnterAsync("anonymous", CancellationToken.None));
    }
}
