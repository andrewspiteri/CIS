using System.CommandLine;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Feedback;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Feedback.Tests;

public sealed class FeedbackModuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cis-feedback-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Host_RecordsEveryInvocationWithoutArgumentValues()
    {
        InitializeRepository();
        using var application = new CisHostBuilder()
            .AddModule(new FeedbackModule())
            .AddModule(new ProbeModule(addSavings: false))
            .Build();

        var exitCode = application.Invoke(["probe", "run", "--repo", _root, "--secret", "do-not-store"]);

        Assert.Equal(0, exitCode);
        var ledger = ToolUsageStore.LedgerPath(_root);
        var text = File.ReadAllText(ledger);
        Assert.DoesNotContain(_root, text, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-store", text, StringComparison.Ordinal);
        var entry = JsonSerializer.Deserialize<ToolUsageEntry>(File.ReadLines(ledger).Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(entry);
        Assert.Equal("probe run", entry.Command);
        Assert.Equal(0, entry.PossibleTokenSavings);
        Assert.Equal("none", entry.SavingsConfidence);
    }

    [Fact]
    public void Host_UsesCommandOwnedSavingsCounterfactual()
    {
        InitializeRepository();
        using var application = new CisHostBuilder()
            .AddModule(new FeedbackModule())
            .AddModule(new ProbeModule(addSavings: true))
            .Build();

        application.Invoke(["probe", "run", "--repo", _root]);

        var entry = JsonSerializer.Deserialize<ToolUsageEntry>(
            File.ReadLines(ToolUsageStore.LedgerPath(_root)).Single(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(entry);
        Assert.Equal(1_000, entry.BaselineEstimatedTokens);
        Assert.Equal(100, entry.ActualEstimatedTokens);
        Assert.Equal(900, entry.PossibleTokenSavings);
        Assert.Equal("high", entry.SavingsConfidence);
    }

    [Fact]
    public void Summary_AggregatesLedgerEntries()
    {
        InitializeRepository();
        var store = new ToolUsageStore();
        store.Record(new CisToolUsageCapture(
            ["probe", "run", "--repo", _root],
            _root,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            DateTimeOffset.UtcNow,
            25,
            0,
            40,
            0,
            [new CisTokenSavingsCandidate(100, 10, "test", "high")]));

        var summary = new FeedbackService(store).Summary(_root, null);

        Assert.Equal(1, summary.InvocationCount);
        Assert.Equal(90, summary.PossibleTokenSavings);
        Assert.Equal(1, summary.EstimatedInvocationCount);
    }

    [Fact]
    public void Summary_ClassifiesGovernedOutcomesWithoutCallingEveryNonZeroExitAFailure()
    {
        InitializeRepository();
        var store = new ToolUsageStore();
        var now = DateTimeOffset.UtcNow;
        Record(store, ["probe", "success", "--repo", _root], now.AddMinutes(-6), 0, 40);
        Record(store, ["repo", "doctor", "--repo", _root], now.AddMinutes(-5), 5, 40);
        Record(store, ["probe", "blocked", "--repo", _root], now.AddMinutes(-4), 4, 40);
        Record(store, ["probe", "invalid", "--repo", _root], now.AddMinutes(-3), 2, 40);
        Record(store, ["probe", "failed", "--repo", _root], now.AddMinutes(-2), 1, 40);
        Record(store, ["probe", "cancelled", "--repo", _root], now.AddMinutes(-1), 130, 40);
        Record(store, ["--version"], now, 0, 40);

        var summary = new FeedbackService(store).Summary(_root, null);
        var entries = store.Read(_root, limit: 20).Entries;

        Assert.Equal(7, summary.InvocationCount);
        Assert.Equal(2, summary.SuccessfulCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(5, summary.NonSuccessfulCount);
        Assert.Equal(1, summary.BlockedCount);
        Assert.Equal(1, summary.GovernedFindingCount);
        Assert.Equal(1, summary.InvalidRequestCount);
        Assert.Equal(1, summary.CancelledCount);
        Assert.Contains(entries, item => item.Command == "version" && item.Outcome == "succeeded");
        Assert.Contains(entries, item => item.Command == "repo doctor" && item.Outcome == "governed-findings");
    }

    [Fact]
    public void Opportunities_UseRecentPerInvocationEvidenceAndSuppressResolvedOrSelfReferentialNoise()
    {
        InitializeRepository();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new ToolUsageStore();

        for (var index = 0; index < 12; index++)
            Record(store, ["small", "query", "--repo", _root], now.AddMinutes(-index - 1), 0, 400);
        Record(store, ["large", "query", "--repo", _root], now.AddMinutes(-2), 0, 8_000);
        Record(store, ["large", "query", "--repo", _root], now.AddMinutes(-1), 0, 8_000);
        Record(store, ["feedback", "opportunities", "--repo", _root], now.AddSeconds(-30), 0, 12_000);
        Record(store, ["old", "large", "--repo", _root], now.AddDays(-2), 0, 40_000);

        Record(store, ["broken", "run", "--repo", _root], now.AddMinutes(-2), 1, 40);
        Record(store, ["broken", "run", "--repo", _root], now.AddMinutes(-1), 1, 40);
        Record(store, ["resolved", "run", "--repo", _root], now.AddMinutes(-5), 1, 40);
        Record(store, ["resolved", "run", "--repo", _root], now.AddMinutes(-4), 1, 40);
        Record(store, ["resolved", "run", "--repo", _root], now.AddSeconds(-10), 0, 40);

        for (var index = 0; index < 4; index++)
            Record(store, ["duplicate", "query", "--repo", _root], now.AddSeconds(-20 + index), 0, 40);

        var result = new FeedbackService(store, () => now).Opportunities(_root, null);

        Assert.Equal(now - FeedbackService.DefaultOpportunityLookback, result.SinceUtc);
        Assert.Contains(result.Opportunities, item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "large query");
        Assert.DoesNotContain(result.Opportunities, item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "small query");
        Assert.DoesNotContain(result.Opportunities, item => item.Command.StartsWith("feedback ", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Opportunities, item => item.Command == "old large");
        Assert.Contains(result.Opportunities, item => item.Code == "CIS-FEEDBACK-REPEATED-FAILURE" && item.Command == "broken run");
        Assert.DoesNotContain(result.Opportunities, item => item.Code.Contains("REPEATED", StringComparison.Ordinal) && item.Command == "resolved run");
        Assert.Contains(result.Opportunities, item => item.Code == "CIS-FEEDBACK-DUPLICATE-QUERY" && item.Command == "duplicate query");
    }

    [Fact]
    public void Opportunities_DoNotRecommendRunningDoctorBecauseDoctorFoundIssues()
    {
        InitializeRepository();
        var now = DateTimeOffset.UtcNow;
        var store = new ToolUsageStore();
        Record(store, ["repo", "doctor", "--repo", _root], now.AddMinutes(-2), 5, 8_000);
        Record(store, ["repo", "doctor", "--repo", _root], now.AddMinutes(-1), 5, 8_000);

        var result = new FeedbackService(store, () => now).Opportunities(_root, null);

        Assert.DoesNotContain(result.Opportunities, item => item.Code.Contains("REPEATED", StringComparison.Ordinal));
        Assert.Contains(result.Opportunities, item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "repo doctor");
    }

    [Fact]
    public void Opportunities_SeparateCompactProjectionsFromFullCommandEvidence()
    {
        InitializeRepository();
        var now = DateTimeOffset.UtcNow;
        var store = new ToolUsageStore();
        Record(store, ["agent", "runs", "--repo", _root], now.AddMinutes(-2), 0, 8_000);
        store.Record(new CisToolUsageCapture(
            ["agent", "runs", "--summary", "--repo", _root], _root,
            now.AddMinutes(-1), now, 25, 0, 8_000, 0,
            [new CisTokenSavingsCandidate(10_000, 2_000,
                "full manifests versus summary", "high")]));

        var result = new FeedbackService(store, () => now).Opportunities(_root, null);

        Assert.Contains(result.Opportunities,
            item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "agent runs");
        Assert.DoesNotContain(result.Opportunities,
            item => item.Code == "CIS-FEEDBACK-COMPACTION" && item.Command == "agent runs --summary");
    }

    [Fact]
    public void Store_CompactsDisposableLedgerByAgeAndCount()
    {
        InitializeRepository();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var store = new ToolUsageStore(2, 1, TimeSpan.FromDays(30), () => now);
        Record(store, ["probe", "old", "--repo", _root], now.AddDays(-31), 0, 40);
        Record(store, ["probe", "one", "--repo", _root], now.AddMinutes(-3), 0, 40);
        Record(store, ["probe", "two", "--repo", _root], now.AddMinutes(-2), 0, 40);
        Record(store, ["probe", "three", "--repo", _root], now.AddMinutes(-1), 0, 40);

        var result = store.Read(_root, limit: 10);

        Assert.Equal(2, result.Entries.Count);
        Assert.DoesNotContain(result.Entries, item => item.Command is "probe old" or "probe one");
        Assert.Contains(result.Entries, item => item.Command == "probe two");
        Assert.Contains(result.Entries, item => item.Command == "probe three");
    }

    [Fact]
    public void Store_ReportsMalformedHistoryWithoutDiscardingValidEntries()
    {
        InitializeRepository();
        var store = new ToolUsageStore();
        Record(store, ["probe", "run", "--repo", _root], DateTimeOffset.UtcNow, 0, 40);
        File.AppendAllText(ToolUsageStore.LedgerPath(_root), "{not-json}\n");

        var result = store.Read(_root, limit: 10);

        Assert.Equal("partial", result.Status);
        Assert.Equal(5, result.ExitCode);
        Assert.Single(result.Entries);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("0h")]
    [InlineData("-1d")]
    [InlineData("NaNh")]
    [InlineData("Infinityh")]
    [InlineData("12x")]
    public void Since_RejectsNonPositiveNonFiniteOrUnsupportedDurations(string value)
    {
        Assert.False(FeedbackService.TryParseSince(value, out _, out var error));
        Assert.NotNull(error);
    }

    private void InitializeRepository()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".cis"));
        File.WriteAllText(Path.Combine(_root, ".cis", "repository.yml"), "schema_version: 1\nrepository:\n  id: test\ndocumentation_root: docs\n");
    }

    private void Record(ToolUsageStore store, IReadOnlyList<string> arguments,
        DateTimeOffset startedAt, int exitCode, int outputCharacters)
    {
        store.Record(new CisToolUsageCapture(arguments, _root,
            startedAt, startedAt.AddMilliseconds(25), 25, exitCode, outputCharacters, 0, []));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ProbeModule(bool addSavings) : ICisModule
    {
        public string Name => "probe";
        public string Description => "Probe.";
        public void RegisterServices(IServiceCollection services) { }

        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
            var module = new Command(Name, Description);
            var command = new Command("run");
            var repo = new Option<string>("--repo") { Required = true };
            var secret = new Option<string?>("--secret");
            command.Options.Add(repo);
            command.Options.Add(secret);
            command.SetAction(_ =>
            {
                Console.WriteLine("compact");
                if (addSavings)
                {
                    services.GetRequiredService<ICisTokenSavingsCollector>().Add(
                        new CisTokenSavingsCandidate(1_000, 100, "test", "high"));
                }

                return 0;
            });
            module.Subcommands.Add(command);
            commands.Add(module);
        }
    }
}
