using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Host;
using Cis.Modules.Feedback;
using Microsoft.Extensions.DependencyInjection;

namespace Cis.Modules.Feedback.Tests;

public sealed partial class FeedbackModuleTests
{
    [Fact]
    public void UnknownRawArgumentsCannotBecomeDirectCaptureCommandOrOptions()
    {
        InitializeRepository();
        var secret = "private-positional-" + new string('s', 20_000);
        var now = DateTimeOffset.UtcNow;
        var store = new ToolUsageStore();
        store.Record(new CisToolUsageCapture(["C:/private/token", secret, "--repo", _root, "--" + secret], _root,
            now, now, 0, 2, 0, 0, []));
        var entry = Assert.Single(store.Read(_root).Entries);
        Assert.Equal("unknown", entry.Command);
        Assert.Empty(entry.Options);
        var row = File.ReadAllText(ToolUsageStore.LedgerPath(_root));
        Assert.True(row.Length < 2048);
        Assert.DoesNotContain("C:/private", row, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, row, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserRejectsUnknownSecretOptionWithoutPersistingItsNameOrPositionalPath()
    {
        InitializeRepository();
        using var application = new CisHostBuilder().AddModule(new FeedbackModule()).AddModule(new ProbeModule(false)).Build();
        var unknown = "--private-" + new string('s', 4096);
        Assert.Equal(2, application.Invoke(["probe", "run", "--repo", _root, unknown, "C:/private/token"]));
        var entry = Assert.Single(new ToolUsageStore().Read(_root).Entries);
        Assert.Equal("probe run", entry.Command);
        Assert.Equal(["--repo"], entry.Options);
        var row = File.ReadAllText(ToolUsageStore.LedgerPath(_root));
        Assert.True(row.Length < 4096);
        Assert.DoesNotContain(unknown, row, StringComparison.Ordinal);
        Assert.DoesNotContain("C:/private/token", row, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsedOptionsHaveFiniteNamesAndCountWithoutChangingCommandOutput()
    {
        InitializeRepository();
        using var application = new CisHostBuilder().AddModule(new FeedbackModule()).AddModule(new ManyOptionsProbe()).Build();
        Assert.Equal(0, application.Invoke(["options-probe", "run", "--repo", _root,
            .. Enumerable.Range(0, 40).Select(index => "--flag-" + index), "--" + new string('a', 130)]));
        var entry = Assert.Single(new ToolUsageStore().Read(_root).Entries);
        Assert.Equal("options-probe run", entry.Command);
        Assert.Equal(32, entry.Options.Count);
        Assert.All(entry.Options, option => Assert.True(option.Length <= 64));
        Assert.Equal(6, entry.StandardOutputCharacters);
        Assert.Equal((long?)6, entry.StandardOutputUtf8Bytes);
        Assert.True(entry.Observations!.DroppedObservations >= 10);
        Assert.DoesNotContain(new string('a', 130), File.ReadAllText(ToolUsageStore.LedgerPath(_root)), StringComparison.Ordinal);
    }

    [Fact]
    public void HostRecordsExactCanonicalOperationAndObservedUtf8SizeWithoutArgumentValues()
    {
        InitializeRepository();
        using var application = new CisHostBuilder().AddModule(new FeedbackModule()).AddModule(new TelemetryProbeModule()).Build();
        Assert.Equal(0, application.Invoke(["telemetry-probe", "task", "done", "not-a-recorded-value", "--repo", _root]));
        var entry = Assert.Single(new ToolUsageStore().Read(_root).Entries);
        Assert.Equal(3, entry.SchemaVersion);
        Assert.Equal("telemetry-probe task", entry.Command);
        var observation = Assert.IsType<CisInvocationObservations>(entry.Observations);
        Assert.Equal("telemetry-probe task finish", observation.Operation);
        Assert.Equal(entry.InvocationId, observation.InvocationId);
        Assert.Equal("CIS-0005", observation.ChangeId);
        Assert.Equal("WORK-006", observation.TaskId);
        Assert.Equal((long?)Encoding.UTF8.GetByteCount("é🚀"), entry.StandardOutputUtf8Bytes);
        Assert.Equal((long?)Encoding.UTF8.GetByteCount("λ"), entry.StandardErrorUtf8Bytes);
        Assert.Equal(3, entry.StandardOutputCharacters);
        Assert.Equal(35L, observation.DeliveredContextUtf8Bytes);
        Assert.Equal("native-inputs-current", Assert.Single(observation.Verification).Reason);
        var retained = File.ReadAllText(ToolUsageStore.LedgerPath(_root));
        Assert.DoesNotContain("not-a-recorded-value", retained, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, retained, StringComparison.Ordinal);
        Assert.DoesNotContain("é", retained, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectCaptureCannotBypassObservationPrivacyOrCountBounds()
    {
        InitializeRepository();
        var now = DateTimeOffset.UtcNow;
        var store = new ToolUsageStore();
        store.Record(new CisToolUsageCapture(["probe", "run", "--repo", _root], _root, now, now, 0, 0, 0, 0, [])
        {
            Observations = new(Guid.NewGuid().ToString("N"), "C:/private/parent", "probe run secret=value", "C:/private/change", "WORK-006",
                [], Enumerable.Range(0, 20).Select(index => new CisGraphObservation("consumer-" + index, null, "reused", "current-inputs")).ToArray(),
                [], ["secret=value"], null, 0),
        });
        var observations = Assert.IsType<CisInvocationObservations>(Assert.Single(store.Read(_root).Entries).Observations);
        Assert.Null(observations.ParentInvocationId);
        Assert.Null(observations.Operation);
        Assert.Null(observations.ChangeId);
        Assert.Null(observations.TaskId);
        Assert.Empty(observations.BlockerCodes);
        Assert.Equal(16, observations.Graph.Count);
        Assert.Equal(9, observations.DroppedObservations);
        var retained = File.ReadAllText(ToolUsageStore.LedgerPath(_root));
        Assert.DoesNotContain("C:/private", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("secret=value", retained, StringComparison.Ordinal);
    }

    [Fact]
    public void OldRowsRemainReadableWithUnknownAttributionAndByteSizes()
    {
        InitializeRepository();
        var store = new ToolUsageStore();
        Record(store, ["probe", "run", "--repo", _root], DateTimeOffset.UtcNow, 0, 40);
        var row = JsonNode.Parse(File.ReadAllText(ToolUsageStore.LedgerPath(_root)))!.AsObject();
        row["schemaVersion"] = 1;
        row.Remove("observations");
        row.Remove("standardOutputUtf8Bytes");
        row.Remove("standardErrorUtf8Bytes");
        row.Remove("buildIdentity");
        File.WriteAllText(ToolUsageStore.LedgerPath(_root), row.ToJsonString() + "\n");
        var historical = Assert.Single(store.Read(_root).Entries);
        Assert.Equal(1, historical.SchemaVersion);
        Assert.Null(historical.Observations);
        Assert.Null(historical.StandardOutputUtf8Bytes);
        Assert.Null(historical.StandardErrorUtf8Bytes);
        Assert.Equal(0, historical.PossibleTokenSavings);
        Assert.Equal("none", historical.SavingsConfidence);
    }

    [Fact]
    public void ParserFailureStillRecordsMatchedOperationWithoutInventingTaskOrContext()
    {
        InitializeRepository();
        using var application = new CisHostBuilder().AddModule(new FeedbackModule()).AddModule(new TelemetryProbeModule()).Build();
        Assert.Equal(2, application.Invoke(["telemetry-probe", "task", "finish", "--repo", _root]));
        var entry = Assert.Single(new ToolUsageStore().Read(_root).Entries);
        Assert.Equal("invalid-request", entry.Outcome);
        var observation = Assert.IsType<CisInvocationObservations>(entry.Observations);
        Assert.Equal("telemetry-probe task finish", observation.Operation);
        Assert.Null(observation.ChangeId);
        Assert.Null(observation.DeliveredContextUtf8Bytes);
        Assert.NotNull(entry.StandardErrorUtf8Bytes);
    }

    private sealed class TelemetryProbeModule : ICisModule
    {
        public string Name => "telemetry-probe";
        public string Description => "Observation fixture.";
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
            var module = new Command(Name);
            var task = new Command("task");
            var finish = new Command("finish");
            finish.Aliases.Add("done");
            finish.Arguments.Add(new Argument<string>("value"));
            finish.Options.Add(new Option<string>("--repo") { Required = true });
            finish.SetAction(_ =>
            {
                CisInvocationTelemetry.AssociateTask("CIS-0005", "WORK-006");
                using (CisInvocationTelemetry.MeasurePhase("evidence"))
                    CisInvocationTelemetry.RecordVerification("unit", "reused", 2, "native-inputs-current");
                CisInvocationTelemetry.RecordGraph("context.task", "sha256:" + new string('a', 64), "reused", "current-inputs");
                CisInvocationTelemetry.RecordDeliveredContext(35);
                Console.Write("é");
                Console.Write('\ud83d');
                Console.Write('\ude80');
                Console.Error.Write("λ");
                return 0;
            });
            task.Subcommands.Add(finish);
            module.Subcommands.Add(task);
            commands.Add(module);
        }
    }

    private sealed class ManyOptionsProbe : ICisModule
    {
        public string Name => "options-probe";
        public string Description => "Bounded parsed-option fixture.";
        public void RegisterServices(IServiceCollection services) { }
        public void RegisterCommands(ICisCommandRegistry commands, IServiceProvider services)
        {
            var module = new Command(Name);
            var run = new Command("run");
            run.Options.Add(new Option<string>("--repo") { Required = true });
            foreach (var index in Enumerable.Range(0, 40)) run.Options.Add(new Option<bool>("--flag-" + index));
            run.Options.Add(new Option<bool>("--" + new string('a', 130)));
            run.SetAction(_ => { Console.Write("stable"); return 0; });
            module.Subcommands.Add(run);
            commands.Add(module);
        }
    }
}
