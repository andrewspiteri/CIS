using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;
using GovernedUnitFixture = Cis.Modules.Testing.Tests.IncrementalGovernedUnitRetryTests.GovernedUnitFixture;

namespace Cis.Modules.Testing.Tests;

[Collection("Incremental environment")]
public sealed class IncrementalMaterialIdentityTests
{
    [Fact]
    public void AcceptedUnitReportsSurviveProfileFormattingRetentionAndMutationOnlySettings()
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true, recoveryConfigured: true);
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var reports = Reports(fixture);

        var parsed = JsonNode.Parse(File.ReadAllBytes(fixture.Native.Full("profile.json")))!;
        fixture.Native.Write("profile.json", parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        AssertWarm(fixture, calls, reports);

        fixture.Native.Save(fixture.Native.Profile with
        {
            MaxRunBytes = 3 * 1024 * 1024,
            MaxRetainedBytes = 24 * 1024 * 1024,
            MinFreeBytes = 4096,
            TimeoutSeconds = 15,
            RecoveryTimeoutSeconds = 15
        });
        AssertWarm(fixture, calls, reports);

        fixture.Native.Save(fixture.Native.Profile with
        {
            MutationCommand = [.. fixture.Native.Profile.MutationCommand!, "--mutation-only"],
            RecoveryCommand = [.. fixture.Native.Profile.RecoveryCommand!, "--recovery-only"],
            CanarySourceFile = "src/B.cs",
            MinimumCanaryKills = 2,
            BreakThreshold = 85
        });
        AssertWarm(fixture, calls, reports);
    }

    [Fact]
    public void InconclusiveGraphUsesParsedProfileAndExcludesMutationOnlyConfigurationAndProducerMetadata()
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true, recoveryConfigured: true);
        fixture.Native.Write("stryker.json", "{\"stryker-config\":{\"additional-timeout\":1000,\"mutate\":[]}}");
        fixture.Native.Save(fixture.Native.Profile with
        {
            ConfigFiles = ["config.json", "profile.json", "stryker.json"],
            StrykerConfigFile = "stryker.json",
            MutationCommand = [fixture.Native.Profile.UnitCommand[0], fixture.Native.Profile.UnitCommand[1],
                "mutation-config", "{output}", "{mutationConfig}"]
        });
        var graph = new ProfileGraph(fixture.Native);
        var service = new IncrementalTestService(graph, fixture.Controller, fixture.Controller);
        AssertCompleted(service.Run(fixture.Options(), 0, TestContext.Current.CancellationToken));
        var calls = fixture.Native.Calls();
        var reports = Reports(fixture);

        var profile = JsonNode.Parse(File.ReadAllBytes(fixture.Native.Full("profile.json")))!;
        fixture.Native.Write("profile.json", profile.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        AssertWarm(fixture, calls, reports, service);

        fixture.Native.Write("stryker.json", "{\"stryker-config\":{\"mutate\":[],\"additional-timeout\":1500}}");
        fixture.Native.Save(fixture.Native.Profile with { RerunAdditionalTimeoutMilliseconds = 2000 });
        graph.Extractor = "fixture/compatible-producer-update";
        AssertWarm(fixture, calls, reports, service);
    }

    [Fact]
    public void AmbientTemporaryDirectoriesDoNotInvalidateCommandsThatReplaceThem()
    {
        var names = new[] { "TEMP", "TMP", "TMPDIR" };
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        using var fixture = new GovernedUnitFixture();
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var reports = Reports(fixture);
        var alternate = fixture.Native.Full(".cis/local/alternate-ambient-temp");
        Directory.CreateDirectory(alternate);
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, alternate);

            AssertWarm(fixture, calls, reports);
        }
        finally
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Fact]
    public void DeclaredTemporaryDirectoriesRemainMaterialAndReachNativeCommands()
    {
        using var fixture = new GovernedUnitFixture();
        var first = fixture.Native.Full(".cis/local/declared-temp-one");
        var second = fixture.Native.Full(".cis/local/declared-temp-two");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var names = new[] { "TEMP", "TMP", "TMPDIR" };
        fixture.Native.Write(".cis/environment-probe.json", JsonSerializer.Serialize(names));
        fixture.Native.Save(fixture.Native.Profile with { Environment = names.ToDictionary(name => name, _ => first) });
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var observed = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllBytes(fixture.Native.Full(".cis/environment-observed.json")))!;
        Assert.All(names, name => Assert.Equal(first, observed[name]));

        fixture.Native.Save(fixture.Native.Profile with { Environment = names.ToDictionary(name => name, _ => second) });
        var pending = fixture.Service.Plan(fixture.Options());
        Assert.Equal(2, pending.PendingUnitBatches);
        var refreshed = fixture.Run();

        AssertCompleted(refreshed);
        Assert.Equal(2, refreshed.ExecutedUnitBatches);
        Assert.Equal(calls.Length + 2, fixture.Native.Calls().Length);
        observed = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllBytes(fixture.Native.Full(".cis/environment-observed.json")))!;
        Assert.All(names, name => Assert.Equal(second, observed[name]));
    }

    [Fact]
    public void SourceTestsDependenciesFilterConfigurationAndToolInputsStillInvalidateTheirAffectedWork()
    {
        using var fixture = new GovernedUnitFixture();
        fixture.Native.Write("native-tool.version", "native-toolchain-1");
        fixture.Native.Save(fixture.Native.Profile with
        { ToolFiles = [.. fixture.Native.Profile.ToolFiles, fixture.Native.Full("native-tool.version")] });
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var a = fixture.Native.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Unit!;
        var aPath = fixture.Native.Full(IncrementalTestService.LocalRoot + "/sample/" + a.ReportPath);
        var aBytes = File.ReadAllBytes(aPath);

        ChangedFile("src/A.cs", "public static class A { public static bool Value => false; }", "src/A.cs");
        ChangedFile("tests/BTests.cs", "class BTests { void Changed() {} }", "src/B.cs");
        ChangedFile("src/Shared.cs", "public static class Shared { public static bool Value => false; }", "src/A.cs");
        ChangedFile("config.json", "{\"material\":true}", "src/A.cs", "src/B.cs");
        ChangedFile("native-tool.version", "native-toolchain-2", "src/A.cs", "src/B.cs");
        var profile = fixture.Native.Profile;
        fixture.Native.Save(profile with { FullTestFilter = "FullyQualifiedName~Fixture.Changed" });
        AssertPending("src/A.cs", "src/B.cs");
        fixture.Native.Save(profile);
        Assert.Equal(calls, fixture.Native.Calls());

        fixture.Native.Write("src/B.cs", "public static class B { public static bool Value => false; }");
        var result = fixture.Run();
        AssertCompleted(result);
        Assert.Equal(1, result.ExecutedUnitBatches);
        Assert.Equal(1, result.ReusedUnitBatches);
        Assert.Equal(aBytes, File.ReadAllBytes(aPath));

        void ChangedFile(string path, string changed, params string[] affected)
        {
            var original = File.ReadAllBytes(fixture.Native.Full(path));
            fixture.Native.Write(path, changed);
            AssertPending(affected);
            File.WriteAllBytes(fixture.Native.Full(path), original);
        }

        void AssertPending(params string[] affected)
        {
            var plan = fixture.Service.Plan(fixture.Options());
            Assert.Equal(0, plan.ExitCode);
            Assert.Equal(affected.Length, plan.PendingUnitBatches);
            Assert.All(plan.Batches, batch => Assert.Equal(!affected.Contains(batch.SourceFile, StringComparer.Ordinal), batch.UnitCurrent));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KnownUnitGraphDependenciesKeepMutationConfigurationMaterialEvenInFallback(bool conclusive)
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true, recoveryConfigured: true);
        ConfigureMutationInputs(fixture, "config/stryker.json");
        var service = new IncrementalTestService(
            new ProfileGraph(fixture.Native, "config/stryker.json", conclusive, dependsOnConfiguration: true),
            fixture.Controller, fixture.Controller);
        AssertCompleted(service.Run(fixture.Options(), 0, TestContext.Current.CancellationToken));
        var calls = fixture.Native.Calls();
        Assert.Equal(2, calls.Length);
        var expected = conclusive ? 1 : 2;

        fixture.Native.Write("config/stryker.json", "{\"stryker-config\":{\"additional-timeout\":1500,\"mutate\":[]}}");
        var plan = service.Plan(fixture.Options());

        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(expected, plan.PendingUnitBatches);
        Assert.False(plan.Batches.Single(batch => batch.SourceFile == "src/A.cs").UnitCurrent);
        Assert.Equal(conclusive, plan.Batches.Single(batch => batch.SourceFile == "src/B.cs").UnitCurrent);
        var retried = service.Run(fixture.Options(), 0, TestContext.Current.CancellationToken);
        AssertCompleted(retried);
        Assert.Equal(expected, retried.ExecutedUnitBatches);
        Assert.Equal(2 - expected, retried.ReusedUnitBatches);
        Assert.Equal(calls.Length + expected, fixture.Native.Calls().Length);
    }

    [Theory]
    [InlineData("relative-backslashes")]
    [InlineData("basename")]
    [InlineData("declared-environment")]
    public void UnitConfigurationReferencesNormalizeSeparatorsAndPreserveAmbiguousBasenames(string reference)
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true, recoveryConfigured: true);
        ConfigureMutationInputs(fixture, "config/stryker.json");
        if (reference == "declared-environment")
            fixture.Native.Save(fixture.Native.Profile with
            { Environment = new() { ["CIS_UNIT_CONFIGURATION"] = ".\\config\\stryker.json" } });
        else
            fixture.Native.Save(fixture.Native.Profile with
            {
                UnitCommand = [.. fixture.Native.Profile.UnitCommand,
                    reference == "basename" ? "--configuration=stryker.json" : "--configuration=.\\config\\stryker.json"]
            });
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        Assert.Equal(2, calls.Length);

        fixture.Native.Write("config/stryker.json", "{\"stryker-config\":{\"additional-timeout\":1500,\"mutate\":[]}}");
        var plan = fixture.Service.Plan(fixture.Options());

        Assert.Equal(0, plan.ExitCode);
        Assert.Equal(2, plan.PendingUnitBatches);
        Assert.Equal(calls, fixture.Native.Calls());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedMutationConfigurationIsBoundToEachStageThatRendersIt(bool unitRendersConfiguration)
    {
        using var fixture = new GovernedUnitFixture(mutationConfigured: true, recoveryConfigured: true);
        ConfigureMutationInputs(fixture, "config/first.json", "config/second.json");
        fixture.Native.Write("config/second.json", "{\"stryker-config\":{\"additional-timeout\":2000,\"mutate\":[]}}");
        fixture.Native.Save(fixture.Native.Profile with
        {
            Environment = new() { ["CIS_UNIT_CONFIGURATION_CHOICES"] = "config/first.json;config/second.json" },
            UnitCommand = unitRendersConfiguration
                ? [.. fixture.Native.Profile.UnitCommand, "{mutationConfig}"] : fixture.Native.Profile.UnitCommand
        });
        AssertCompleted(fixture.Run());
        var before = fixture.Service.Plan(fixture.Options());
        var calls = fixture.Native.Calls();

        fixture.Native.Save(fixture.Native.Profile with { StrykerConfigFile = "config/second.json" });
        var after = fixture.Service.Plan(fixture.Options());

        Assert.Equal(0, after.ExitCode);
        Assert.Equal(unitRendersConfiguration ? 2 : 0, after.PendingUnitBatches);
        foreach (var prior in before.Batches)
        {
            var current = after.Batches.Single(batch => batch.Id == prior.Id);
            Assert.NotEqual(prior.MutationFingerprint, current.MutationFingerprint);
            Assert.Equal(!unitRendersConfiguration, prior.UnitFingerprint == current.UnitFingerprint);
        }
        Assert.Equal(calls, fixture.Native.Calls());
    }

    private static void ConfigureMutationInputs(GovernedUnitFixture fixture, params string[] files)
    {
        foreach (var file in files)
            fixture.Native.Write(file, "{\"stryker-config\":{\"additional-timeout\":1000,\"mutate\":[]}}");
        fixture.Native.Save(fixture.Native.Profile with
        {
            ConfigFiles = [.. fixture.Native.Profile.ConfigFiles, .. files],
            StrykerConfigFile = files[0],
            MutationCommand = [fixture.Native.Profile.UnitCommand[0], fixture.Native.Profile.UnitCommand[1],
                "mutation-config", "{output}", "{mutationConfig}"]
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentExecutablePathSpellingsKeepAcceptedUnitEvidenceCurrentWithoutNativeReplay(bool windowsSlashes)
    {
        if (windowsSlashes && !OperatingSystem.IsWindows()) Assert.Skip("Windows alternate path separators.");
        using var fixture = new GovernedUnitFixture();
        var executable = fixture.Native.Profile.UnitCommand[0];
        var spelling = windowsSlashes ? executable.Replace('\\', '/')
            : Path.Combine(Path.GetDirectoryName(executable)!, ".", Path.GetFileName(executable));
        fixture.Native.Save(fixture.Native.Profile with
        { UnitCommand = [spelling, .. fixture.Native.Profile.UnitCommand.Skip(1)] });
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var reports = Reports(fixture);
        Assert.Equal(2, calls.Length);
        var stage = fixture.Native.SavedState().Batches.Values.First().Unit!;
        Assert.NotEqual(spelling, stage.Execution!.Command![0]);
        Assert.Equal(Path.GetFullPath(spelling), Path.GetFullPath(stage.Execution.Command[0]));

        var plan = fixture.Service.Plan(fixture.Options());
        var status = fixture.Service.Plan(fixture.Options()); // CLI status uses the same read-only currentness path.
        var verified = fixture.Service.Verify(new(fixture.Native.Path, "profile.json") { Layer = "unit" });

        Assert.True(plan.ScopeComplete);
        Assert.Equal(0, status.PendingUnitBatches);
        Assert.Equal(0, verified.ExitCode);
        AssertWarm(fixture, calls, reports);
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("filter")]
    public void SavedCommandTamperingStillInvalidatesEvidenceWithoutLaunchingNativeWork(string damage)
    {
        using var fixture = new GovernedUnitFixture();
        AssertCompleted(fixture.Run());
        var calls = fixture.Native.Calls();
        var state = fixture.Native.SavedState();
        var key = state.Batches.Keys.Order(StringComparer.Ordinal).First();
        var saved = state.Batches[key];
        var stage = saved.Unit!;
        var command = stage.Execution!.Command!.ToArray();
        var executable = stage.Execution.ExecutablePath;
        if (damage == "executable")
            command[0] = executable = fixture.Native.Full("different-native-executable");
        else
            command[Array.IndexOf(fixture.Native.Profile.UnitCommand, "{testFilter}")] = "FullyQualifiedName~Different.Tests";
        state.Batches[key] = saved with
        { Unit = stage with { Execution = stage.Execution with { Command = command, ExecutablePath = executable } } };
        fixture.Native.WriteState(state);

        var plan = fixture.Service.Plan(fixture.Options());
        var verified = fixture.Service.Verify(new(fixture.Native.Path, "profile.json") { Layer = "unit" });

        Assert.Equal(1, plan.PendingUnitBatches);
        Assert.False(plan.Batches.Single(batch => batch.Id == key).UnitCurrent);
        Assert.Equal(4, verified.ExitCode);
        Assert.Equal(stage.Fingerprint, fixture.Native.SavedState().Batches[key].Unit!.Fingerprint);
        Assert.Equal(calls, fixture.Native.Calls());
    }

    private static void AssertCompleted(IncrementalTestResult result)
    {
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.True(result.ScopeComplete);
    }

    private static Dictionary<string, byte[]> Reports(GovernedUnitFixture fixture)
        => fixture.Native.SavedState().Batches.Values.Where(batch => batch.Unit is not null)
            .Select(batch => fixture.Native.Full(IncrementalTestService.LocalRoot + "/sample/" + batch.Unit!.ReportPath))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWarm(GovernedUnitFixture fixture, string[] calls,
        IReadOnlyDictionary<string, byte[]> reports, IncrementalTestService? service = null)
    {
        service ??= fixture.Service;
        var result = service.Run(fixture.Options(), 0, TestContext.Current.CancellationToken);
        AssertCompleted(result);
        Assert.Equal(0, result.ExecutedUnitBatches);
        Assert.Equal(2, result.ReusedUnitBatches);
        Assert.Equal(calls, fixture.Native.Calls());
        Assert.Equal(0, service.Plan(fixture.Options()).PendingUnitBatches);
        foreach (var report in reports) Assert.Equal(report.Value, File.ReadAllBytes(report.Key));
    }

    private sealed class ProfileGraph(IncrementalTestServiceTests.Fixture fixture, string configurationPath = "stryker.json",
        bool conclusive = false, bool dependsOnConfiguration = false) : ICisGraphSnapshotReader
    {
        public string Extractor { get; set; } = "fixture/1";
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var snapshot = fixture.Read(repositoryPath);
            var additional = new[] { "profile.json", configurationPath }.Select(path => new CisGraphInput(path,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.Full(path))))));
            var graph = snapshot.Graph!;
            if (dependsOnConfiguration)
                graph = graph with
                {
                    Nodes = [.. graph.Nodes, new CisGraphNode("unit-config", "sample", "file", "configuration", "unit-config",
                        "Unit configuration", [], "derived", "active", [new(configurationPath, "line", "1")], new Dictionary<string, string>(), [])],
                    Edges = [.. graph.Edges, new CisGraphEdge("a-unit-config", "depends-on", "a", "unit-config", "discovered", "high",
                        [], new Dictionary<string, string>())]
                };
            return snapshot with
            {
                Graph = graph,
                GraphCapabilities = conclusive ? snapshot.GraphCapabilities : [],
                Manifest = snapshot.Manifest! with
                { Inputs = [.. snapshot.Manifest.Inputs, .. additional], Extractors = [Extractor] }
            };
        }
    }
}
