using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;
using Xunit;

namespace Cis.Modules.Testing.Tests;

public sealed class IncrementalTestServiceTests
{
    [Fact]
    public void GraphSelectsTransitiveTestsAndTracksSharedDependencies()
    {
        using var fixture = new Fixture();
        var result = fixture.Plan();
        Assert.Equal(0, result.ExitCode);
        var a = result.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = result.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal("graph", a.Selection);
        Assert.Equal(["tests/ATests.cs"], a.TestFiles);
        Assert.Contains("Fixture.ATests.", a.TestFilter, StringComparison.Ordinal);
        Assert.Contains("src/Shared.cs", a.DependencyFiles);
        Assert.DoesNotContain("src/Shared.cs", b.DependencyFiles);
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot)));
    }

    [Fact]
    public void MissingTestRelationshipFallsBackToAllTestsAndAllGraphInputs()
    {
        using var fixture = new Fixture { ConnectATests = false };
        var batch = fixture.Plan().Batches.Single(item => item.SourceFile == "src/A.cs");
        Assert.Equal("all-tests", batch.Selection);
        Assert.Equal(fixture.Profile.FullTestFilter, batch.TestFilter);
        Assert.Equal(2, batch.TestFiles.Length);
        Assert.Contains("src/B.cs", batch.DependencyFiles);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("warning")]
    public void IncompleteGraphCannotAuthorizeReuseOrExecution(string fault)
    {
        using var fixture = new Fixture { GraphFault = fault };
        var result = fixture.Run("unit");
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(fixture.Calls());
        Assert.Contains(result.Diagnostics, item => item.Contains("fresh, complete graph", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundedBatchesResumeWithoutRepeatingCompletedUnitOrMutationWork()
    {
        using var fixture = new Fixture();
        var first = fixture.Run(maximumBatches: 1);
        Assert.True(first.ExitCode == 3, string.Join("; ", first.Diagnostics));
        Assert.Equal(1, first.ExecutedUnitBatches);
        Assert.Equal(1, first.ExecutedMutationBatches);
        Assert.Equal(1, first.PendingMutationBatches);
        Assert.Null(first.CompleteScopeScore);
        Assert.False(first.ScopeComplete);
        var aCalls = fixture.Calls().Count(call => call.Contains("src/A.cs", StringComparison.Ordinal));

        var resumed = fixture.Run();
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(1, resumed.ExecutedUnitBatches);
        Assert.Equal(1, resumed.ExecutedMutationBatches);
        Assert.Equal(1, resumed.ReusedMutationBatches);
        Assert.Equal(aCalls, fixture.Calls().Count(call => call.Contains("src/A.cs", StringComparison.Ordinal)));
        Assert.True(resumed.ScopeComplete);
        Assert.Equal(100, resumed.CompleteScopeScore);
        Assert.Equal(2, resumed.Killed);
        Assert.False(resumed.Qualified);
        var calls = fixture.Calls().Length;

        var unchanged = fixture.Run();
        Assert.Equal(0, unchanged.ExecutedUnitBatches);
        Assert.Equal(0, unchanged.ExecutedMutationBatches);
        Assert.Equal(2, unchanged.ReusedUnitBatches);
        Assert.Equal(2, unchanged.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls().Length);
        Assert.False(Directory.Exists(fixture.Full(IncrementalTestService.LocalRoot + "/sample/work")));
    }

    [Theory]
    [InlineData("src/A.cs", false)]
    [InlineData("src/Shared.cs", false)]
    [InlineData("tests/ATests.cs", false)]
    [InlineData("config.json", true)]
    [InlineData("tests/BTests.cs", false)]
    public void SourceFixtureTestAndConfigurationChangesInvalidateTheRelevantBatches(string changed, bool both)
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        fixture.Write(changed, File.ReadAllText(fixture.Full(changed)) + "\n// changed");

        var plan = fixture.Plan("unit");
        Assert.Equal(0, plan.ExitCode);
        var a = plan.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        var b = plan.Batches.Single(batch => batch.SourceFile == "src/B.cs");
        Assert.Equal(changed == "tests/BTests.cs" && !both, a.UnitCurrent);
        Assert.Equal(!both && changed != "tests/BTests.cs", b.UnitCurrent);
        var run = fixture.Run("unit");
        Assert.Equal(both ? 2 : 1, run.ExecutedUnitBatches);
    }

    [Fact]
    public void GraphBuildIdentityAloneDoesNotInvalidateUnchangedInputs()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        fixture.BuildSuffix = "different-build";
        var run = fixture.Run("unit");
        Assert.Equal(0, run.ExecutedUnitBatches);
        Assert.Equal(2, run.ReusedUnitBatches);
    }

    [Fact]
    public void ChangedDependencyRelationshipInvalidatesTheConsumer()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        fixture.BUsesShared = true;
        var run = fixture.Run("unit");
        Assert.Equal(1, run.ExecutedUnitBatches);
        Assert.Equal(1, run.ReusedUnitBatches);
        Assert.Contains("src/Shared.cs", run.Batches.Single(batch => batch.SourceFile == "src/B.cs").DependencyFiles);
    }

    [Fact]
    public void NewTestFileInTheSameComponentInvalidatesItsAffectedSourceBatch()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        fixture.Write("tests/NewATests.cs", "class NewATests { }");
        fixture.IncludeNewATests = true;
        var plan = fixture.Plan();
        var a = plan.Batches.Single(batch => batch.SourceFile == "src/A.cs");
        Assert.False(a.UnitCurrent);
        Assert.Contains("tests/NewATests.cs", a.TestFiles);
        Assert.True(plan.Batches.Single(batch => batch.SourceFile == "src/B.cs").UnitCurrent);
    }

    [Fact]
    public void ModifiedSavedReportCannotBeReused()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var report = Directory.GetFiles(fixture.Full(IncrementalTestService.LocalRoot + "/sample/reports"), "*-unit.xml")[0];
        File.AppendAllText(report, "changed");
        var run = fixture.Run("unit");
        Assert.Equal(1, run.ExecutedUnitBatches);
        Assert.Equal(1, run.ReusedUnitBatches);
    }

    [Fact]
    public void CompletedSurvivorIsSavedWithoutBeingConvertedIntoAKillOrRepeated()
    {
        using var fixture = new Fixture();
        fixture.Fault("src/B.cs", "mutation:survivor");
        var first = fixture.Run();
        Assert.Equal(0, first.ExitCode);
        Assert.True(first.ScopeComplete);
        Assert.Equal(50, first.CompleteScopeScore);
        Assert.False(first.ThresholdMet);
        Assert.Equal(1, first.Survived);
        var next = fixture.Run();
        Assert.Equal(0, next.ExecutedMutationBatches);
        Assert.Equal(1, next.Survived);
    }

    [Fact]
    public void ExactNativeEmptyFileDoesNotInventKillsOrPreventOtherBatchesFromCompleting()
    {
        using var fixture = new Fixture();
        fixture.Fault("src/B.cs", "mutation:empty");
        var run = fixture.Run();
        Assert.Equal(0, run.ExitCode);
        Assert.True(run.ScopeComplete);
        Assert.Equal(1, run.Killed);
        Assert.Equal(1, run.KnownDenominator);
        Assert.Equal(0, fixture.Run().ExecutedMutationBatches);
    }

    [Fact]
    public void CompletedNativeTimeoutVerdictIsACachedMissAndStillHoldsTheGate()
    {
        using var fixture = new Fixture();
        fixture.Fault("src/B.cs", "mutation:reported-timeout");
        var first = fixture.Run();
        Assert.Equal(0, first.ExitCode);
        Assert.True(first.ScopeComplete);
        Assert.Equal(50, first.CompleteScopeScore);
        Assert.Equal(1, first.Killed);
        Assert.Equal(1, first.TimedOut);
        Assert.False(first.ThresholdMet);
        var retainedRoot = IncrementalTestService.LocalRoot + "/sample/";
        var stdoutPath = fixture.Full(retainedRoot + "latest-failure-stdout.log");
        var stderrPath = fixture.Full(retainedRoot + "latest-failure-stderr.log");
        Assert.True(File.Exists(stdoutPath));
        Assert.True(File.Exists(stderrPath));
        Assert.Equal(128 * 1024, new FileInfo(stdoutPath).Length);
        Assert.Equal(128 * 1024, new FileInfo(stderrPath).Length);
        var stdout = File.ReadAllText(stdoutPath);
        var stderr = File.ReadAllText(stderrPath);
        Assert.Contains("native-timeout-stdout-tail", stdout);
        Assert.Contains("native-timeout-stderr-tail", stderr);
        Assert.DoesNotContain("native-timeout-stdout-prefix", stdout);
        Assert.DoesNotContain("native-timeout-stderr-prefix", stderr);
        Assert.DoesNotContain("native-unit-complete", stdout);
        Assert.DoesNotContain("native-recovery-complete", stdout);
        Assert.False(Directory.Exists(fixture.Full(retainedRoot + "work")));
        var calls = fixture.Calls();
        var next = fixture.Run();
        Assert.Equal(0, next.ExecutedMutationBatches);
        Assert.Equal(2, next.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls());
        Assert.Equal(1, next.TimedOut);
        Assert.False(next.ThresholdMet);
        Assert.Equal(stdout, File.ReadAllText(stdoutPath));
        Assert.Equal(stderr, File.ReadAllText(stderrPath));
        Assert.False(Directory.Exists(fixture.Full(retainedRoot + "work")));
    }

    [Fact]
    public void StrykerTemplateGetsExactBatchScopeAndFilterWithoutChangingTheOriginal()
    {
        using var fixture = new Fixture();
        const string template = "{\"stryker-config\":{\"mutate\":[\"wrong.cs\"],\"test-case-filter\":\"wrong\",\"mutation-level\":\"Complete\"}}";
        fixture.Write("stryker.json", template);
        fixture.Save(fixture.Profile with
        {
            ConfigFiles = ["config.json", "stryker.json"], StrykerConfigFile = "stryker.json",
            MutationCommand = [fixture.Profile.UnitCommand[0], fixture.Profile.UnitCommand[1], "config-mutation", "{output}", "{mutationConfig}"],
        });
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(2, result.Killed);
        Assert.Equal(template, File.ReadAllText(fixture.Full("stryker.json")));
        Assert.Contains(fixture.Calls(), call => call.StartsWith("mutation src/A.cs", StringComparison.Ordinal) && call.Contains("Fixture.ATests.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, 15000)]
    [InlineData(30000, 30000)]
    public void NativeRerunBudgetChangesOnlyTheRenderedAdditionalTimeout(int? budget, int expectedTimeout)
    {
        using var fixture = new Fixture();
        const string template = "{\"stryker-config\":{\"mutate\":[\"wrong.cs\"],\"test-case-filter\":\"wrong\",\"mutation-level\":\"Complete\",\"additional-timeout\":15000,\"timeout-ratio\":1.5,\"concurrency\":2}}";
        fixture.Write("stryker.json", template);
        fixture.Write(".cis/configuration-probe", "observe the native fixture's actual configuration input");
        fixture.Save(fixture.Profile with
        {
            SourceFiles = ["src/A.cs"],
            ConfigFiles = ["config.json", "stryker.json"],
            StrykerConfigFile = "stryker.json",
            TimeoutSeconds = 120,
            RerunAdditionalTimeoutMilliseconds = budget,
            MutationCommand = [fixture.Profile.UnitCommand[0], fixture.Profile.UnitCommand[1], "config-mutation", "{output}", "{mutationConfig}"],
        });
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, string.Join("; ", result.Diagnostics));
        Assert.Equal(1, result.ExecutedMutationBatches);
        Assert.Equal(template, File.ReadAllText(fixture.Full("stryker.json")));
        var expected = JsonNode.Parse(template)!;
        var settings = expected["stryker-config"]!;
        settings["mutate"] = new JsonArray("src/A.cs");
        settings["test-case-filter"] = Assert.Single(result.Batches).TestFilter;
        settings["additional-timeout"] = expectedTimeout;
        var observed = JsonNode.Parse(File.ReadAllText(fixture.Full(".cis/rendered-mutation-config.json")));
        Assert.True(JsonNode.DeepEquals(expected, observed), "The actual native configuration changed beyond scope, filter and the declared budget.");
    }

    [Fact]
    public void CanaryWithoutKnownKillsStopsBeforeOtherFilesAndKeepsItsNativeFindings()
    {
        using var fixture = new Fixture();
        fixture.Fault("src/A.cs", "mutation:survivor");
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.Equal(1, result.ExecutedMutationBatches);
        Assert.DoesNotContain(fixture.Calls(), call => call.Contains("src/B.cs", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, item => item.Contains("required known kills", StringComparison.Ordinal));
        Assert.Equal(1, result.Survived);
        Assert.False(fixture.SavedState().Batches.Values.Single().Mutation!.Completed);
    }

    [Fact]
    public void MutationProfileRequiresAnExplicitNativeCanary()
    {
        using var fixture = new Fixture();
        fixture.Save(fixture.Profile with { CanarySourceFile = null });
        Assert.Equal(2, fixture.Plan().ExitCode);
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("unit:unit-abort")]
    [InlineData("unit:unit-failure")]
    [InlineData("unit:unit-malformed")]
    public void AbortedOrFailedUnitResultsCannotBeReused(string fault)
    {
        using var fixture = new Fixture();
        fixture.Fault("src/A.cs", fault);
        var first = fixture.Run("unit");
        Assert.Equal(4, first.ExitCode);
        Assert.Equal(2, first.PendingUnitBatches);
        var next = fixture.Run("unit");
        Assert.Equal(1, next.ExecutedUnitBatches);
        Assert.Equal(0, next.ReusedUnitBatches);
        Assert.Equal(4, next.ExitCode);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("wrong-source")]
    public void IncompleteOrMismatchedMutationReportDoesNotCompleteABatch(string fault)
    {
        using var fixture = new Fixture();
        fixture.Fault("src/A.cs", "mutation:" + fault);
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.False(result.ScopeComplete);
        Assert.Null(result.CompleteScopeScore);
        Assert.Equal(2, result.PendingMutationBatches);
        Assert.False(fixture.SavedState().RecoveryRequired);
    }

    [Fact]
    public void TimeoutRecoversBaselineAndKeepsEarlierCompletedBatches()
    {
        using var fixture = new Fixture();
        fixture.Save(fixture.Profile with { TimeoutSeconds = 5 });
        var initial = fixture.Run(maximumBatches: 1);
        Assert.True(initial.ExitCode == 3, string.Join("; ", initial.Diagnostics));
        fixture.Fault("src/B.cs", "mutation:timeout");
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.Equal(1, result.ReusedMutationBatches);
        Assert.Equal(1, result.PendingMutationBatches);
        Assert.False(File.Exists(fixture.Full(".cis/instrumented")));
        Assert.False(fixture.SavedState().RecoveryRequired);
        var failed = fixture.SavedState().Batches.Values.Single(batch => batch.SourceFile == "src/B.cs").Mutation!;
        Assert.Equal("timeout", failed.Execution!.Status);
        Assert.False(failed.Completed);
        Assert.Equal(1, failed.PostflightPassed);

        fixture.ClearFault("src/B.cs");
        var callsForA = fixture.Calls().Count(call => call.Contains("src/A.cs", StringComparison.Ordinal));
        var resumed = fixture.Run();
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(1, resumed.ExecutedMutationBatches);
        Assert.Equal(callsForA, fixture.Calls().Count(call => call.Contains("src/A.cs", StringComparison.Ordinal)));
    }

    [Fact]
    public void InterruptedMutationMarkerForcesRecoveryBeforeAnyReuse()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var state = fixture.SavedState() with { RecoveryRequired = true, RecoverySourceFile = "src/A.cs" };
        fixture.WriteState(state);
        fixture.Write(".cis/instrumented", "unfinished mutant");
        var calls = fixture.Calls().Length;
        var run = fixture.Run();
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(0, run.ExecutedMutationBatches);
        Assert.Equal(2, fixture.Calls().Length - calls);
        Assert.StartsWith("recovery ", fixture.Calls()[calls], StringComparison.Ordinal);
        Assert.False(fixture.SavedState().RecoveryRequired);
    }

    [Fact]
    public void AStillActivePreviousGuardBlocksRecoveryAndNativeExecution()
    {
        using var fixture = new Fixture();
        fixture.WriteState(new() { ActiveExecutionUntilUtc = DateTimeOffset.UtcNow.AddMinutes(2) });
        var result = fixture.Run();
        Assert.Equal(4, result.ExitCode);
        Assert.Empty(fixture.Calls());
        Assert.Contains(result.Diagnostics, item => item.Contains("previous native guard", StringComparison.Ordinal));
    }

    [Fact]
    public void ForcedBoundedUnitRunLeavesUnexecutedForcedWorkPendingForResume()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        var forced = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "unit", 1, 120, true));
        Assert.Equal(3, forced.ExitCode);
        Assert.Equal(1, forced.ExecutedUnitBatches);
        Assert.Equal(1, forced.PendingUnitBatches);
        Assert.Equal(1, fixture.Run("unit").ExecutedUnitBatches);
    }

    [Fact]
    public void ForcedUnitRefreshPreservesCompatibleMutationsIncludingUnattemptedBatches()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        var before = fixture.SavedState();
        var calls = fixture.Calls().Count(call => call.StartsWith("mutation ", StringComparison.Ordinal));

        var forced = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "unit", 1, 120, true));

        Assert.Equal(3, forced.ExitCode);
        Assert.Equal(1, forced.ExecutedUnitBatches);
        Assert.Equal(0, forced.ExecutedMutationBatches);
        // Less than the mutation recovery reserve, but enough for the pending unit refresh.
        var resumed = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "all", 4, 30));
        Assert.Equal(0, resumed.ExitCode);
        Assert.Equal(1, resumed.ExecutedUnitBatches);
        Assert.Equal(0, resumed.ExecutedMutationBatches);
        Assert.Equal(2, resumed.ReusedMutationBatches);
        Assert.Equal(calls, fixture.Calls().Count(call => call.StartsWith("mutation ", StringComparison.Ordinal)));
        var after = fixture.SavedState();
        foreach (var (id, batch) in before.Batches)
        {
            Assert.Equal(batch.Mutation!.ReportSha256, after.Batches[id].Mutation!.ReportSha256);
            Assert.Equal(batch.Mutation.Fingerprint, after.Batches[id].Mutation!.Fingerprint);
            Assert.True(File.Exists(fixture.Full(IncrementalTestService.LocalRoot + "/sample/" + after.Batches[id].Mutation!.ReportPath)));
        }
    }

    [Fact]
    public void FailedForcedUnitRefreshStillInvalidatesThatBatchesMutation()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run().ExitCode);
        fixture.Fault("src/A.cs", "unit:unit-failure");

        var result = new IncrementalTestService(fixture).Run(new(fixture.Path, "profile.json", "unit", 1, 120, true));

        Assert.Equal(4, result.ExitCode);
        Assert.Null(result.CompleteScopeScore);
        var state = fixture.SavedState();
        Assert.Null(state.Batches.Values.Single(batch => batch.SourceFile == "src/A.cs").Mutation);
        Assert.NotNull(state.Batches.Values.Single(batch => batch.SourceFile == "src/B.cs").Mutation);
    }

    [Fact]
    public void RetiringASourceRemovesItsSavedReportsAndState()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        fixture.Save(fixture.Profile with { SourceFiles = ["src/A.cs"] });
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        Assert.Single(fixture.SavedState().Batches);
        Assert.Single(Directory.GetFiles(fixture.Full(IncrementalTestService.LocalRoot + "/sample/reports")));
    }

    [Fact]
    public void ExpiredUnitGuardMarkerDoesNotRequireAMutationRecoveryCommand()
    {
        using var fixture = new Fixture();
        fixture.Save(fixture.Profile with { MutationCommand = null, RecoveryCommand = null });
        fixture.WriteState(new() { ActiveExecutionUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Equal(0, fixture.Run("unit").ExitCode);
        Assert.False(fixture.SavedState().RecoveryRequired);
    }

    [Fact]
    public void StorageCapStopsBeforeStartingNativeWork()
    {
        using var fixture = new Fixture();
        fixture.Save(fixture.Profile with { MaxRetainedBytes = 10 });
        Assert.Equal(4, fixture.Run().ExitCode);
        Assert.Empty(fixture.Calls());
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("/outside.cs")]
    public void UnsafeScopeIsRejectedBeforeAnyExecution(string path)
    {
        using var fixture = new Fixture();
        fixture.Save(fixture.Profile with { SourceFiles = [path] });
        Assert.Equal(2, fixture.Plan().ExitCode);
        Assert.Empty(fixture.Calls());
    }

    internal sealed class Fixture : IDisposable, ICisGraphSnapshotReader
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cis-incremental-" + Guid.NewGuid().ToString("N"));
        internal IncrementalTestProfile Profile { get; private set; }
        internal bool ConnectATests { get; init; } = true;
        internal bool BUsesShared { get; set; }
        internal bool IncludeNewATests { get; set; }
        internal string BuildSuffix { get; set; } = "";
        internal string? GraphFault { get; init; }
        internal Fixture()
        {
            Directory.CreateDirectory(Path);
            Write("src/A.cs", "public static class A { public static bool Value => true; }");
            Write("src/B.cs", "public static class B { public static bool Value => true; }");
            Write("src/Shared.cs", "public static class Shared { public static bool Value => true; }");
            Write("tests/ATests.cs", "class ATests { }");
            Write("tests/BTests.cs", "class BTests { }");
            Write("config.json", "{}");
            var fixture = System.IO.Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (dotnet is null || !File.Exists(dotnet))
                dotnet = System.IO.Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
            Profile = new()
            {
                SchemaVersion = 1, Id = "sample", SourceFiles = ["src/A.cs", "src/B.cs"], TestFiles = ["tests/ATests.cs", "tests/BTests.cs"],
                ConfigFiles = ["config.json"], ToolFiles = [dotnet, fixture], FullTestFilter = "FullyQualifiedName~Fixture.",
                UnitCommand = [dotnet, fixture, "unit", "{output}", "{sourceFile}", "{testFilter}"], UnitReportPath = "unit.trx",
                MutationCommand = [dotnet, fixture, "mutation", "{output}", "{sourceFile}", "{testFilter}"],
                CanarySourceFile = "src/A.cs",
                RecoveryCommand = [dotnet, fixture, "recovery", "{output}", "{sourceFile}"],
                TimeoutSeconds = 10, RecoveryTimeoutSeconds = 10, MinFreeBytes = 0,
            };
            Save(Profile);
        }
        internal string Full(string relative) => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        internal void Write(string relative, string content) { var file = Full(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); File.WriteAllText(file, content); }
        internal void Save(IncrementalTestProfile profile) { Profile = profile; Write("profile.json", JsonSerializer.Serialize(profile, Json)); }
        internal string[] Calls() => File.Exists(Full(".cis/native-calls.log")) ? File.ReadAllLines(Full(".cis/native-calls.log")) : [];
        internal void Fault(string source, string fault) => Write(".cis/fault-" + System.IO.Path.GetFileName(source), fault);
        internal void ClearFault(string source) => File.Delete(Full(".cis/fault-" + System.IO.Path.GetFileName(source)));
        internal IncrementalTestResult Plan(string layer = "all") => new IncrementalTestService(this).Plan(new(Path, "profile.json", layer));
        internal IncrementalTestResult Run(string layer = "all", int maximumBatches = 4) => new IncrementalTestService(this).Run(new(Path, "profile.json", layer, maximumBatches, 120));
        internal IncrementalTestState SavedState() => JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllText(Full(IncrementalTestService.LocalRoot + "/sample/state.json")), Json)!;
        internal void WriteState(IncrementalTestState state) => Write(IncrementalTestService.LocalRoot + "/sample/state.json", JsonSerializer.Serialize(state, Json));

        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var files = new List<string> { "src/A.cs", "src/B.cs", "src/Shared.cs", "tests/ATests.cs", "tests/BTests.cs", "config.json" };
            var nodes = new List<CisGraphNode> { Node("a", "src/A.cs"), Node("b", "src/B.cs"), Node("shared", "src/Shared.cs"),
                Node("at", "tests/ATests.cs", "Fixture.ATests"), Node("bt", "tests/BTests.cs", "Fixture.BTests") };
            var edges = new List<CisGraphEdge> { Edge("a", "shared"), Edge("bt", "b") };
            if (ConnectATests) edges.Add(Edge("at", "a"));
            if (BUsesShared) edges.Add(Edge("b", "shared"));
            if (IncludeNewATests)
            {
                files.Add("tests/NewATests.cs");
                nodes.Add(Node("new-at", "tests/NewATests.cs", "Fixture.NewATests"));
                nodes.Add(Node("new-at-test", "tests/NewATests.cs", "Fixture.NewATests") with { Kind = "test" });
                edges.Add(Edge("new-at", "a"));
            }
            var build = new CisGraphBuildMetadata("build-" + BuildSuffix, "sample", null, true, GraphFault == "partial" ? "partial" : "complete");
            var graph = new CisGraphDocument(6, build, nodes, edges, new(0, GraphFault == "warning" ? 1 : 0, 0));
            var manifest = new CisGraphManifest(6, build.Id, "sample", "docs", null, true,
                files.Select(file => new CisGraphInput(file, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(Full(file))))))).ToArray(), ["fixture/1"]);
            return new("available", GraphFault is "stale" or "missing" ? 4 : 0, Path, GraphFault == "stale" ? "stale" : "fresh",
                GraphFault == "missing" ? null : graph, manifest, ["compiler.calls"], [], []);
        }
        private static CisGraphNode Node(string key, string file, string? testClass = null) => new(key, "sample", "symbol", testClass is null ? "method" : "class", key, key,
            ["compiler-bound", "csharp"], "derived", "active", [new(file, "line", "1")], testClass is null ? new Dictionary<string, string>() : new() { ["qualifiedName"] = testClass, ["component"] = "fixture-tests" }, []);
        private static CisGraphEdge Edge(string from, string to) => new(from + "->" + to, "calls", from, to, "discovered", "high", [], new Dictionary<string, string>());
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
