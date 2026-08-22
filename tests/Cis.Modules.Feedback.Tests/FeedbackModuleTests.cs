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

    private void InitializeRepository()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".cis"));
        File.WriteAllText(Path.Combine(_root, ".cis", "repository.yml"), "schema_version: 1\nrepository:\n  id: test\ndocumentation_root: docs\n");
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
