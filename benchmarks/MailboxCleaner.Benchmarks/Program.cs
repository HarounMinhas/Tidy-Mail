using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using MailboxCleaner.Web.Application.Cleanup;
using MailboxCleaner.Web.Application.Filtering;
using MailboxCleaner.Web.Application.MailboxScanning;
using MailboxCleaner.Web.Application.MailboxStats;

BenchmarkRunner.Run<MailboxBenchmarks>();

[MemoryDiagnoser]
public class MailboxBenchmarks
{
    private readonly MailboxFilterService _filter = new();
    private readonly CleanupSuggestionService _suggestions = new();
    private readonly MailboxStatsService _stats = new();
    private IReadOnlyList<MailboxMetadata> _messages = Array.Empty<MailboxMetadata>();
    private MailboxSelectionService _selection = new();

    [Params(100, 1_000, 10_000, 50_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _messages = DummyMailboxFactory.Create(Count);
        _selection = new MailboxSelectionService();
    }

    [Benchmark] public IReadOnlyList<MailboxMetadata> Filtering() => _filter.Apply(_messages, new MailboxFilter { Domain = "example.com", IsRead = true });
    [Benchmark] public object SenderGrouping() => _messages.GroupBy(m => m.FromEmail).Select(g => new { Sender = g.Key, Count = g.Count() }).ToList();
    [Benchmark] public object DomainGrouping() => _messages.GroupBy(m => m.Domain).Select(g => new { Domain = g.Key, Count = g.Count() }).ToList();
    [Benchmark] public object Autocomplete() => _messages.SelectMany(m => new[] { m.FromEmail, m.FromName, m.Subject }).Where(v => v.Contains("sender", StringComparison.OrdinalIgnoreCase)).Take(10).ToList();
    [Benchmark] public int BulkSelection() { _selection.Clear(); _selection.SelectAllMatching(_messages.Where(m => m.IsRead)); return _selection.SelectedMessageIds.Count; }
    [Benchmark] public IReadOnlyList<CleanupSuggestion> CleanupSuggestionGeneration() => _suggestions.Generate(_messages);
    [Benchmark] public MailboxStats StatsGeneration() => _stats.Build(_messages);
}

public static class DummyMailboxFactory
{
    public static IReadOnlyList<MailboxMetadata> Create(int count)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<MailboxMetadata>(count);
        for (var i = 0; i < count; i++)
        {
            var sender = $"sender{i % 250}@{(i % 3 == 0 ? "example.com" : "service.com")}";
            var labels = i % 4 == 0 ? new[] { "INBOX", "UNREAD" } : new[] { "INBOX" };
            list.Add(new MailboxMetadata($"m{i}", $"t{i / 3}", $"Sender {i % 250}", sender, sender.Split('@')[1], $"Notification {i}", now.AddDays(-i % 1000), i % 4 != 0, labels, i % 5 == 0, 1024 + i, now, i % 7 == 0 ? "<mailto:unsubscribe@example.com>" : null, i % 7 == 0 ? "bulk" : null));
        }
        return list;
    }
}
