using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;
using MailboxCleaner.Web.Infrastructure.Demo;
using MailboxCleaner.Web.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;

namespace MailboxCleaner.Web.Tests;

/// <summary>
/// Uses the SAME IGmailClient, mailbox scan, cache, stats and bulk-action services
/// as the application. Nothing talks to a Google API or stores real OAuth tokens.
/// </summary>
public sealed class DemoMailboxIntegrationTests
{
    private const string Alice = "user:demo-alice";
    private const string Bob = "user:demo-bob";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task DependencyInjection_ScanBuildsRealStatsAndCleanupSuggestions()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var scan = await alice.ServiceProvider.GetRequiredService<MailboxScanService>()
            .ScanAsync(Alice, null, Ct);
        var store = alice.ServiceProvider.GetRequiredService<IMailboxMetadataStore>();
        var metadata = await store.GetMetadataAsync(Alice, Ct);

        Assert.Equal(MailboxScanStatus.Completed, scan.Status);
        Assert.Equal(48, metadata.Count);
        Assert.Equal(48, scan.ScannedMessages);
        Assert.NotNull(scan.LastHistoryId);
        Assert.Contains(metadata, message => message.HasAttachment);
        Assert.Contains(metadata, message => !message.IsRead);
        Assert.Contains(metadata, message => message.IsNewsletterLike);
        Assert.Contains(metadata, message => message.ReceivedAt < DateTimeOffset.UtcNow.AddYears(-1));
        Assert.Equal(48, new MailboxStatsService().Build(metadata).TotalScannedMessages);
        Assert.NotEmpty(new CleanupSuggestionService().Generate(metadata));
    }

    [Fact]
    public async Task Actions_ActuallyChangeDummyGmail_AndHistoryUpdatesTheRealScanCache()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var client = alice.ServiceProvider.GetRequiredService<IGmailClient>();
        var scanner = alice.ServiceProvider.GetRequiredService<MailboxScanService>();
        var actions = alice.ServiceProvider.GetRequiredService<GmailBulkActionService>();
        var store = alice.ServiceProvider.GetRequiredService<IMailboxMetadataStore>();

        await scanner.ScanAsync(Alice, null, Ct);
        var initialHistory = await client.GetMailboxHistoryIdAsync(Ct);
        const string first = "alice-demo-005";
        const string second = "alice-demo-006";
        const string third = "alice-demo-007";
        const string fourth = "alice-demo-008";
        const string fifth = "alice-demo-009";

        Assert.Equal(1, (await actions.ApplyAsync(Alice, CleanupBulkAction.MarkUnread, [first], null, null, Ct)).TotalSucceeded);
        Assert.Contains("UNREAD", (await client.FetchMessageMetadataAsync(Ct))
            .Single(m => m.Id == first).Labels);
        Assert.Equal(1, (await actions.ApplyAsync(Alice, CleanupBulkAction.MarkRead, [first], null, null, Ct)).TotalSucceeded);
        Assert.DoesNotContain("UNREAD", (await client.FetchMessageMetadataAsync(Ct))
            .Single(m => m.Id == first).Labels);

        var added = await actions.ApplyAsync(Alice, CleanupBulkAction.AddLabel,
            [first], null, "Testing", Ct);
        Assert.Equal(1, added.TotalSucceeded);
        Assert.False(string.IsNullOrWhiteSpace(added.TargetLabelId));
        var afterAdd = (await client.FetchMessageMetadataAsync(Ct)).Single(m => m.Id == first);
        Assert.Contains("INBOX", afterAdd.Labels);
        Assert.Contains("Label_DemoProjects", afterAdd.Labels);
        Assert.Contains(added.TargetLabelId!, afterAdd.Labels);

        var moved = await actions.ApplyAsync(Alice, CleanupBulkAction.MoveToLabel,
            [first], added.TargetLabelId, null, Ct);
        Assert.Equal(1, moved.TotalSucceeded);
        var afterMove = (await client.FetchMessageMetadataAsync(Ct)).Single(m => m.Id == first);
        Assert.DoesNotContain("INBOX", afterMove.Labels);
        Assert.DoesNotContain("Label_DemoProjects", afterMove.Labels);
        Assert.Contains(added.TargetLabelId!, afterMove.Labels);

        await actions.ApplyAsync(Alice, CleanupBulkAction.Archive, [second], null, null, Ct);
        Assert.DoesNotContain("INBOX", (await client.FetchMessageMetadataAsync(Ct))
            .Single(m => m.Id == second).Labels);

        var beforeTrash = (await client.FetchMessageMetadataAsync(Ct)).Count;
        await actions.ApplyAsync(Alice, CleanupBulkAction.Trash, [third, fourth, fifth], null, null, Ct);
        // Cache must match the Gmail-visible mailbox immediately, not only after rescan.
        Assert.Equal(45, (await store.GetMetadataAsync(Alice, Ct)).Count);
        var afterTrash = await client.FetchMessageMetadataAsync(Ct);
        Assert.Equal(beforeTrash - 3, afterTrash.Count);
        Assert.DoesNotContain(afterTrash, m => m.Id == third || m.Id == fourth || m.Id == fifth);

        var delta = await client.FetchHistoryDeltaAsync(initialHistory!, Ct);
        Assert.NotNull(delta);
        Assert.Contains(third, delta.DeletedMessageIds);
        Assert.Contains(first, delta.UpdatedMessages.Select(m => m.Id));
        var updated = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Completed, updated.Status);
        Assert.Equal(45, (await store.GetMetadataAsync(Alice, Ct)).Count);
        Assert.Equal(45, new MailboxStatsService().Build(
            await store.GetMetadataAsync(Alice, Ct)).TotalScannedMessages);
    }

    [Fact]
    public async Task DistinctCookieSubjects_UseCompletelyIsolatedDummyMailboxState()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        using var bob = CreateAccountScope(services, Bob);
        var aClient = alice.ServiceProvider.GetRequiredService<IGmailClient>();
        var bClient = bob.ServiceProvider.GetRequiredService<IGmailClient>();
        var aScan = alice.ServiceProvider.GetRequiredService<MailboxScanService>();
        var bScan = bob.ServiceProvider.GetRequiredService<MailboxScanService>();
        var aActions = alice.ServiceProvider.GetRequiredService<GmailBulkActionService>();
        var bActions = bob.ServiceProvider.GetRequiredService<GmailBulkActionService>();
        var metadata = alice.ServiceProvider.GetRequiredService<IMailboxMetadataStore>();

        await aScan.ScanAsync(Alice, null, Ct);
        await bScan.ScanAsync(Bob, null, Ct);
        Assert.Equal(48, (await metadata.GetMetadataAsync(Alice, Ct)).Count);
        Assert.Equal(48, (await metadata.GetMetadataAsync(Bob, Ct)).Count);

        var outcome = await aActions.ApplyAsync(Alice, CleanupBulkAction.Trash,
            ["alice-demo-001"], null, null, Ct);
        Assert.Equal(1, outcome.TotalSucceeded);
        Assert.Contains((await bClient.FetchMessageMetadataAsync(Ct)), m => m.Id == "bob-demo-001");
        var crossAccount = await bActions.ApplyAsync(Bob, CleanupBulkAction.Trash,
            ["alice-demo-002"], null, null, Ct);
        Assert.Equal(0, crossAccount.TotalSucceeded);
        Assert.Equal(1, crossAccount.TotalFailed);

        Assert.DoesNotContain((await aClient.FetchMessageMetadataAsync(Ct)), m => m.Id == "alice-demo-001");
        Assert.Equal(48, (await bClient.FetchMessageMetadataAsync(Ct)).Count);
        Assert.Throws<UnauthorizedAccessException>(() =>
            services.GetRequiredService<DemoMailboxStore>().GetMailbox("user:google-real-account"));
    }

    [Fact]
    public async Task ControlledFailures_ExerciseDefinitiveRejectionsAndUncertainPostWriteResults()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var state = services.GetRequiredService<DemoMailboxStore>();
        var client = alice.ServiceProvider.GetRequiredService<IGmailClient>();
        var action = alice.ServiceProvider.GetRequiredService<GmailBulkActionService>();
        var scan = alice.ServiceProvider.GetRequiredService<MailboxScanService>();
        await scan.ScanAsync(Alice, null, Ct);
        state.SetFailure(Alice, "alice-demo-001", DemoFailure.Rejected);
        state.SetFailure(Alice, "alice-demo-002", DemoFailure.UncertainAfterApplying);

        var outcome = await action.ApplyAsync(Alice, CleanupBulkAction.MarkUnread,
            ["alice-demo-001", "alice-demo-002", "alice-demo-003"], null, null, Ct);
        Assert.Equal(1, outcome.TotalSucceeded);
        Assert.Equal(1, outcome.TotalFailed);
        Assert.Equal(1, outcome.TotalUnknown);
        Assert.True(outcome.RequiresReconciliation);
        Assert.Equal(["alice-demo-001"], outcome.FailedMessageIds);
        Assert.Equal(["alice-demo-002"], outcome.UnknownMessageIds);
        var after = await client.FetchMessageMetadataAsync(Ct);
        Assert.DoesNotContain("UNREAD", after.Single(m => m.Id == "alice-demo-001").Labels);
        Assert.Contains("UNREAD", after.Single(m => m.Id == "alice-demo-002").Labels);
        Assert.Contains("UNREAD", after.Single(m => m.Id == "alice-demo-003").Labels);

        state.SetFailure(Alice, "alice-demo-001", DemoFailure.None);
        Assert.Equal(1, (await action.ApplyAsync(Alice, CleanupBulkAction.MarkUnread,
            outcome.FailedMessageIds, null, null, Ct)).TotalSucceeded);
        Assert.Contains("UNREAD", (await client.FetchMessageMetadataAsync(Ct))
            .Single(m => m.Id == "alice-demo-001").Labels);
    }

    [Fact]
    public async Task SimulatedGmailFailure_DoesNotHidePreviousSuccessfulDashboardSnapshot()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var scanner = alice.ServiceProvider.GetRequiredService<MailboxScanService>();
        var metadataStore = alice.ServiceProvider.GetRequiredService<IMailboxMetadataStore>();
        var dummy = services.GetRequiredService<DemoMailboxStore>();

        var first = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Completed, first.Status);
        dummy.FailNextSync(Alice);

        var failed = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Failed, failed.Status);
        Assert.Contains("Simulated Gmail read failure", failed.ErrorMessage);
        var persisted = await metadataStore.GetCurrentScanAsync(Alice, Ct);
        Assert.Equal(MailboxScanStatus.Completed, persisted?.Status);
        Assert.Equal(first.CompletedAt, persisted?.CompletedAt);
        Assert.Equal(first.LastHistoryId, persisted?.LastHistoryId);
        Assert.Equal(48, (await metadataStore.GetMetadataAsync(Alice, Ct)).Count);

        var retry = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Completed, retry.Status);
        Assert.Null(retry.ErrorMessage);
    }

    [Fact]
    public async Task CancelledRecentRescan_PreservesSnapshotAndVisibleRetryMarker()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var scanner = alice.ServiceProvider.GetRequiredService<MailboxScanService>();
        var metadataStore = alice.ServiceProvider.GetRequiredService<IMailboxMetadataStore>();
        var dummy = services.GetRequiredService<DemoMailboxStore>();

        var completed = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Completed, completed.Status);
        Assert.False(metadataStore.IsScanStale(completed, TimeSpan.FromHours(12)));

        dummy.CancelNextSync(Alice);
        var cancelled = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Cancelled, cancelled.Status);

        var persisted = await metadataStore.GetCurrentScanAsync(Alice, Ct);
        Assert.NotNull(persisted);
        Assert.Equal(MailboxScanStatus.Completed, persisted.Status);
        Assert.Equal(completed.CompletedAt, persisted.CompletedAt);
        Assert.Equal(completed.StartedAt, persisted.StartedAt);
        Assert.Equal(completed.LastHistoryId, persisted.LastHistoryId);
        Assert.Equal(completed.ScannedMessages, persisted.ScannedMessages);
        Assert.False(metadataStore.IsScanStale(persisted, TimeSpan.FromHours(12)));
        // Overview.razor exposes Retry scan when ErrorMessage is nonempty,
        // even if the last successful scan is still younger than 12 hours.
        Assert.Contains("cancelled", persisted.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(48, (await metadataStore.GetMetadataAsync(Alice, Ct)).Count);

        var retry = await scanner.ScanAsync(Alice, null, Ct);
        Assert.Equal(MailboxScanStatus.Completed, retry.Status);
        Assert.Null((await metadataStore.GetCurrentScanAsync(Alice, Ct))?.ErrorMessage);
    }

    [Fact]
    public async Task ExpiredHistoryCursor_FallsBackToFullDemoScan()
    {
        using var services = BuildServices();
        using var alice = CreateAccountScope(services, Alice);
        var client = alice.ServiceProvider.GetRequiredService<IGmailClient>();
        Assert.Null(await client.FetchHistoryDeltaAsync("1", Ct));
        Assert.Null(await client.FetchHistoryDeltaAsync("not-a-history-id", Ct));
        Assert.Equal(48, (await client.FetchMessageMetadataAsync(true, null, Ct)).Count);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DemoMailboxStore>();
        services.AddSingleton<IMailboxMetadataStore, MailboxMetadataStore>();
        services.AddSingleton<MailboxOperationCoordinator>();
        services.AddScoped<MailboxUserContext>();
        services.AddScoped<IUserMailboxKeyProvider, NoHttpContextKeyProvider>();
        services.AddScoped<IGmailClient, DemoGmailClient>();
        services.AddScoped<MailboxScanService>();
        services.AddScoped<GmailBulkActionService>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IServiceScope CreateAccountScope(ServiceProvider provider, string account)
    {
        var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<MailboxUserContext>().CurrentUserKey = account;
        return scope;
    }

    private sealed class NoHttpContextKeyProvider : IUserMailboxKeyProvider
    {
        public string GetCurrentUserKey() => throw new UnauthorizedAccessException(
            "A Blazor circuit must supply an authenticated demo subject.");
    }
}
