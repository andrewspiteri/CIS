using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;
using Cis.Modules.Repository;
using Cis.Modules.Testing;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Fact]
    public void NativeWork_IncrementalUnitLaunchesAndSettlesWithCoveredWorkspaceDirectoryAlias()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var controller = new AgentService(new CisRepositoryContextResolver());
        var allocation = WorkAllocation(180, 300, 30, ["WORK-090"]) with
        {
            Controllers = [WorkActor],
            Storage = new(128 * 1024 * 1024,
                [new(repository.Path, ".cis/local"), new(repository.Path, "workspace") { Scratch = true }]),
        };
        Assert.Equal(0, controller.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "workspace-alias.json", allocation), WorkActor,
            "Run one actual unit through the owned workspace measurement and native monitor.").ExitCode);
        repository.Write("src/X.cs", "public static class X { public static bool Value => true; }");
        repository.Write("src/Y.cs", "public static class Y { public static bool Value => true; }");
        repository.Write("tests/XTests.cs", "class XTests { }");
        repository.Write("tests/YTests.cs", "class YTests { }");
        repository.Write("config.json", "{}");
        repository.Write("workspace/packages/client-core/index.ts", "export const value = true;");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (dotnet is null || !File.Exists(dotnet))
            dotnet = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var nativeFixture = Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
        var profile = new IncrementalTestProfile
        {
            SchemaVersion = 1,
            Id = "workspace-alias",
            SourceFiles = ["src/X.cs"],
            TestFiles = ["tests/XTests.cs"],
            ConfigFiles = ["config.json"],
            ToolFiles = [dotnet, nativeFixture],
            FullTestFilter = "FullyQualifiedName~Fixture.",
            UnitCommand = [dotnet, nativeFixture, "unit", "{output}", "{sourceFile}", "{testFilter}"],
            UnitReportPath = "unit.trx",
            TimeoutSeconds = 10,
            MaxRetainedBytes = 16 * 1024 * 1024,
            MinFreeBytes = 0,
        };
        repository.Write("unit-profile.json", JsonSerializer.Serialize(profile, JsonOptions));
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor);
        var ownedStorage = (ICisOwnedStorageController)controller;
        var beforeAlias = ownedStorage.Inspect(repository.Path, association);
        Assert.True(beforeAlias.Allowed, string.Join("; ", beforeAlias.Errors));
        var alias = Path.GetFullPath(Path.Combine(repository.Path, "workspace/node_modules/@workspace/client-core"));
        var target = Path.GetFullPath(Path.Combine(repository.Path, "workspace/packages/client-core"));
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "cis-agent-tests");
        Assert.True(CisPathSafety.IsUnderRoot(fixtureRoot, repository.Path));
        Assert.True(Guid.TryParseExact(Path.GetFileName(repository.Path), "N", out _));
        Assert.True(CisPathSafety.IsUnderRoot(repository.Path, alias));
        Assert.True(CisPathSafety.IsUnderRoot(repository.Path, target));
        Assert.False(CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(repository.Path)!, repository.Path));
        Assert.Null(AliasAttributes());
        Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "/d", "/c", "mklink", "/J", alias, target }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, error);
            }
            else Directory.CreateSymbolicLink(alias, target);
            var counted = ownedStorage.Inspect(repository.Path, association);
            Assert.True(counted.Allowed, string.Join("; ", counted.Errors));
            Assert.Equal(beforeAlias.Storage!.OwnedBytes, counted.Storage!.OwnedBytes);
            var service = new IncrementalTestService(new ProtectedEvidenceGraph(repository.Path), controller, controller);

            var result = service.Run(new(repository.Path, "unit-profile.json", "unit", 1, 60) { Work = association },
                0, TestContext.Current.CancellationToken);

            Assert.True(result.ExitCode == 0, JsonSerializer.Serialize(result, JsonOptions));
            Assert.Equal(1, result.ExecutedUnitBatches);
            Assert.Equal(0, result.ExecutedMutationBatches);
            var statePath = Path.Combine(repository.Path, result.StatePath!);
            var state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(statePath), JsonOptions)!;
            var unit = Assert.Single(state.Batches.Values).Unit!;
            Assert.Equal("completed", unit.Status);
            Assert.True(unit.Completed);
            Assert.True(unit.Total > 0);
            Assert.Equal(unit.Total, unit.Passed);
            Assert.Equal(0, unit.Failed);
            Assert.Equal(0, unit.Skipped);
            var execution = Assert.IsType<MutationNativeExecution>(unit.Execution);
            Assert.Equal("exited", execution.Status);
            Assert.Equal(0, execution.ExitCode);
            Assert.True(execution.SafelyStopped);
            Assert.Equal("governed-native", execution.WorkCoverage);
            Assert.True(execution.OwnedProcessPeak > 0);
            Assert.True(execution.PeakOwnedBytes > 0);
            Assert.True(execution.PeakOperationBytes > 0);
            Assert.Empty(execution.SettlementErrors);
            var report = Path.Combine(Path.GetDirectoryName(statePath)!, unit.ReportPath);
            Assert.True(File.Exists(report));
            Assert.Equal(unit.ReportSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(report))));
            var calls = File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log"));
            Assert.StartsWith("unit ", Assert.Single(calls), StringComparison.Ordinal);
            Assert.Equal(0, result.WorkControl!.Work!.Task!.ReservedMilliseconds);
            Assert.All(result.WorkControl.Work.Executions, item => Assert.True(item.Finalized));
            var storage = ownedStorage.Inspect(repository.Path, association);
            Assert.True(storage.Allowed, string.Join("; ", storage.Errors));
            Assert.Equal(0, storage.Storage!.ReservedBytes);
            var stateBytes = File.ReadAllBytes(statePath);

            var verified = service.Verify(new(repository.Path, "unit-profile.json") { Layer = "unit" });

            Assert.True(verified.ExitCode == 0, JsonSerializer.Serialize(verified, JsonOptions));
            Assert.Equal("verified", verified.Status);
            Assert.Equal("unit", verified.Layer);
            Assert.True(verified.ScopeComplete);
            var verifiedBatch = Assert.Single(verified.Batches);
            Assert.Equal("src/X.cs", verifiedBatch.SourceFile);
            Assert.Equal(unit.Fingerprint, verifiedBatch.UnitFingerprint);
            Assert.Equal(unit.ReportSha256, verifiedBatch.UnitReportSha256);
            Assert.Equal(unit.Passed, verifiedBatch.UnitPassed);
            Assert.Equal(calls, File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log")));
            Assert.Equal(stateBytes, File.ReadAllBytes(statePath));
            Assert.Equal(unit.ReportSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(report))));
            Assert.True(File.GetAttributes(alias).HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal("export const value = true;", File.ReadAllText(Path.Combine(target, "index.ts")));
        }
        finally
        {
            // Detach only this newly created alias before AgentRepository's
            // ordinary cleanup, and never delete an ordinary replacement/target.
            Assert.False(CisPathSafety.ContainsReparsePoint(Path.GetPathRoot(alias)!, Path.GetDirectoryName(alias)!));
            if (AliasAttributes() is { } attributes)
            {
                Assert.True(attributes.HasFlag(FileAttributes.ReparsePoint));
                Directory.Delete(alias, recursive: false);
                Assert.Null(AliasAttributes());
            }
        }

        FileAttributes? AliasAttributes()
        {
            try { return File.GetAttributes(alias); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        }
    }

    [Fact]
    public void NativeWork_IncrementalExactRestorationPublishesItsOriginalLineageAndClearsRecovery()
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        const string worker = "Evidence worker";
        var controller = new AgentService(new CisRepositoryContextResolver());
        var allocation = WorkAllocation(300, 600, 60, ["WORK-090"]) with
        {
            Controllers = [WorkActor, worker],
            Storage = new(128 * 1024 * 1024, [new(repository.Path, ".cis/local")])
            { RestorationBytes = 16 * 1024 * 1024 },
        };
        Assert.Equal(0, controller.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "restoration-reports.json", allocation), WorkActor,
            "Restore the original bounded native lineage without granting cross-owner supersession.").ExitCode);
        repository.Write("src/X.cs", "public static class X { public static bool Value => true; }");
        repository.Write("src/Y.cs", "public static class Y { public static bool Value => true; }");
        repository.Write("tests/XTests.cs", "class XTests { }");
        repository.Write("tests/YTests.cs", "class YTests { }");
        repository.Write("config.json", "{}");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (dotnet is null || !File.Exists(dotnet))
            dotnet = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var nativeFixture = Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
        var profile = new IncrementalTestProfile
        {
            SchemaVersion = 1,
            Id = "restoration-reports",
            SourceFiles = ["src/X.cs", "src/Y.cs"],
            TestFiles = ["tests/XTests.cs", "tests/YTests.cs"],
            ConfigFiles = ["config.json"],
            ToolFiles = [dotnet, nativeFixture],
            FullTestFilter = "FullyQualifiedName~Fixture.",
            UnitCommand = [dotnet, nativeFixture, "unit", "{output}", "{sourceFile}", "{testFilter}"],
            MutationCommand = [dotnet, nativeFixture, "mutation", "{output}", "{sourceFile}", "{testFilter}"],
            RecoveryCommand = [dotnet, nativeFixture, "recovery", "{output}", "{sourceFile}"],
            UnitReportPath = "unit.trx",
            CanarySourceFile = "src/X.cs",
            MinimumCanaryKills = 1,
            TimeoutSeconds = 10,
            RecoveryTimeoutSeconds = 10,
            MaxRetainedBytes = 16 * 1024 * 1024,
            MinFreeBytes = 0,
        };
        repository.Write("recovery-profile.json", JsonSerializer.Serialize(profile, JsonOptions));
        var association = new CisNativeWorkOptions("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: worker);
        var options = new IncrementalTestOptions(repository.Path, "recovery-profile.json", "unit", 4, 120)
        { Work = association };
        var service = new IncrementalTestService(new ProtectedEvidenceGraph(repository.Path), controller, controller);
        var seeded = service.Run(options, 0, TestContext.Current.CancellationToken);
        Assert.True(seeded.ExitCode == 0, JsonSerializer.Serialize(seeded, JsonOptions));
        var statePath = Path.Combine(repository.Path, seeded.StatePath!);
        var state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(statePath), JsonOptions)!;
        Assert.Null(state.RecoveryBasis); // Ordinary unit execution does not pin an unused mutation recipe.
        var unitCalls = File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log"));
        // Select the genuine mutation-capable route to retain its recipe. Its
        // pilot gate must hold expansion before any additional native launch.
        var mutationReadiness = service.Run(options with { Layer = "all" }, 0, TestContext.Current.CancellationToken);
        Assert.Equal("pilot-required", mutationReadiness.Status);
        Assert.Equal(4, mutationReadiness.ExitCode);
        Assert.Equal(0, mutationReadiness.ExecutedUnitBatches);
        Assert.Equal(0, mutationReadiness.ExecutedMutationBatches);
        Assert.Equal(unitCalls, File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log")));
        state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(statePath), JsonOptions)!;
        Assert.NotNull(state.RecoveryBasis);
        var latestPath = Path.Combine(Path.GetDirectoryName(statePath)!, "latest-run.json");
        var reportBytes = state.Batches.Values.ToDictionary(batch => Path.Combine(Path.GetDirectoryName(statePath)!, batch.Unit!.ReportPath),
            batch => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(statePath)!, batch.Unit!.ReportPath)));

        // Establish an interrupted original through the real authority. Only its
        // launch intent is controlled; recovery and healthy postflight launch the
        // existing native fixture through the actual incremental writer below.
        var interrupted = controller.Admit(new(repository.Path, "native/controlled-original", "incremental-all", 30, association)
        { RecoveryBasis = state.RecoveryBasis });
        Assert.True(interrupted.Allowed, string.Join("; ", interrupted.Errors));
        using (var original = interrupted.Scope!)
        {
            var claim = controller.Reserve(repository.Path, original.Reference, "required-original-metadata", 0,
                [new(statePath, Required: true), new(latestPath, Required: true)], actor: worker);
            Assert.True(claim.Allowed, string.Join("; ", claim.Result.Errors));
            Assert.True(controller.Settle(repository.Path, original.Reference, claim.ReservationId!, true,
                [statePath, latestPath]).Allowed);
            state = state with { WorkReference = original.Reference, RecoveryRequired = true, RecoverySourceFile = "src/X.cs" };
            File.WriteAllBytes(statePath, JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
            repository.Write(".cis/instrumented", "controlled interrupted native instrumentation");
            original.Launch();
            original.Returned(true);
            var stopped = original.Finish("RecoveryRequired", "Controlled original stopped with its pinned restoration still required.", []);
            Assert.Equal(0, stopped.ExitCode);
            Assert.True(stopped.Work!.Executions.Single(item => item.ExecutionId == original.Reference.ExecutionId).NativeRestorationRequired);
        }
        var before = controller.InspectSettlement(repository.Path, interrupted.Reference!).Work!;
        var calls = File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log"));

        var restored = service.Run(options with
        {
            MaximumSeconds = 30,
            Work = association with { RecoveryOfExecutionId = interrupted.Reference!.ExecutionId }
        }, 0, TestContext.Current.CancellationToken);

        Assert.True(restored.ExitCode == 0, JsonSerializer.Serialize(restored, JsonOptions));
        Assert.Equal("restored", restored.Status);
        var final = restored.WorkControl!.Work!;
        Assert.Equal("Restored", final.Executions.Single(item => item.ExecutionId == restored.WorkReference!.ExecutionId).Status);
        Assert.False(final.Executions.Single(item => item.ExecutionId == interrupted.Reference!.ExecutionId).NativeRestorationRequired);
        Assert.All(final.Executions, item => { Assert.True(item.Finalized); Assert.Null(item.RecoveryReason); });
        Assert.Equal(interrupted.Reference!.LogicalAttemptId, restored.WorkReference!.LogicalAttemptId);
        Assert.Equal(before.Task!.UsedAttempts, final.Task!.UsedAttempts);
        Assert.Equal(before.Task.AllocatedMilliseconds, final.Task.AllocatedMilliseconds);
        Assert.Equal(0, final.Task.ReservedMilliseconds);
        Assert.True(final.Task.RestorationMilliseconds < before.Task.RestorationMilliseconds);
        Assert.False(File.Exists(Path.Combine(repository.Path, ".cis/instrumented")));
        var added = File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log")).Skip(calls.Length).ToArray();
        Assert.Collection(added, call => Assert.StartsWith("recovery ", call, StringComparison.Ordinal),
            call => Assert.StartsWith("unit ", call, StringComparison.Ordinal));
        Assert.All(reportBytes, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
        var latest = JsonSerializer.Deserialize<IncrementalTestResult>(File.ReadAllBytes(latestPath), JsonOptions)!;
        Assert.Equal("restored", latest.Status);
        Assert.Equal(restored.WorkReference, latest.WorkReference);
        Assert.False(JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(statePath), JsonOptions)!.RecoveryRequired);
        Assert.Equal(0, controller.InspectSettlement(repository.Path, restored.WorkReference!).ExitCode);
        Assert.Equal(0, ((ICisOwnedStorageController)controller).Inspect(repository.Path, association).Storage!.ReservedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWork_IncrementalWriterPreservesAnotherOwnersRequiredReportBytes(bool forcedReplacement)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository, secondTask: true);
        const string worker = "Evidence worker";
        var controller = new AgentService(new CisRepositoryContextResolver());
        var allocation = WorkAllocation(300, 600, 30, ["WORK-090", "WORK-091"]) with
        {
            Controllers = [WorkActor, worker],
            Storage = new(128 * 1024 * 1024, [new(repository.Path, ".cis/local")]),
        };
        Assert.Equal(0, controller.AllocateWork(repository.Path, "CIS-0001",
            WriteWorkInput(repository, "protected-reports.json", allocation), WorkActor,
            "Bound two existing owners without granting the worker cross-owner supersession.").ExitCode);
        repository.Write("src/X.cs", "public static class X { public static bool Value => true; }");
        repository.Write("src/Y.cs", "public static class Y { public static bool Value => true; }");
        repository.Write("tests/XTests.cs", "class XTests { }");
        repository.Write("tests/YTests.cs", "class YTests { }");
        repository.Write("config.json", "{}");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (dotnet is null || !File.Exists(dotnet))
            dotnet = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var nativeFixture = Path.Combine(AppContext.BaseDirectory, "native-fixture", "Cis.Testing.NativeFixture.dll");
        var profile = new IncrementalTestProfile
        {
            SchemaVersion = 1,
            Id = "protected-reports",
            SourceFiles = ["src/X.cs", "src/Y.cs"],
            TestFiles = ["tests/XTests.cs", "tests/YTests.cs"],
            ConfigFiles = ["config.json"],
            ToolFiles = [dotnet, nativeFixture],
            FullTestFilter = "FullyQualifiedName~Fixture.",
            UnitCommand = [dotnet, nativeFixture, "unit", "{output}", "{sourceFile}", "{testFilter}"],
            UnitReportPath = "unit.trx",
            TimeoutSeconds = 10,
            MaxRetainedBytes = 16 * 1024 * 1024,
            MinFreeBytes = 0,
        };
        repository.Write("unit-profile.json", JsonSerializer.Serialize(profile, JsonOptions));
        var service = new IncrementalTestService(new ProtectedEvidenceGraph(repository.Path), controller, controller);
        IncrementalTestOptions Options(string task, string actor, bool force = false) => new(repository.Path,
            "unit-profile.json", "unit", 4, 120, force)
        { Work = new("CIS-0001", task, AuthorityPath: repository.Path, Actor: actor) };
        var seeded = service.Run(Options("WORK-090", worker), 0, TestContext.Current.CancellationToken);
        Assert.True(seeded.ExitCode == 0, JsonSerializer.Serialize(seeded, JsonOptions));
        var statePath = Path.Combine(repository.Path, seeded.StatePath!);
        var state = JsonSerializer.Deserialize<IncrementalTestState>(File.ReadAllBytes(statePath), JsonOptions)!;
        var reportPaths = state.Batches.ToDictionary(pair => pair.Key,
            pair => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(statePath)!, pair.Value.Unit!.ReportPath)));
        var original = reportPaths.ToDictionary(pair => pair.Key, pair => File.ReadAllBytes(pair.Value));
        var calls = File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log"));
        var reused = service.Run(Options("WORK-090", worker), 0, TestContext.Current.CancellationToken);
        Assert.True(reused.ExitCode == 0, JsonSerializer.Serialize(reused, JsonOptions));
        Assert.Equal(0, reused.ExecutedUnitBatches);
        Assert.Equal(2, reused.ReusedUnitBatches);
        if (forcedReplacement)
        {
            // This existing controlled fault produces different native report
            // contents if the forced replacement is permitted to reach execution.
            repository.Write(".cis/fault-X.cs", "unit:unit-failure");
        }
        else repository.Write("unit-profile.json", JsonSerializer.Serialize(profile with { SourceFiles = ["src/X.cs"] }, JsonOptions));

        var held = service.Run(Options("WORK-091", worker, forcedReplacement), 0, TestContext.Current.CancellationToken);

        Assert.Equal(4, held.ExitCode);
        Assert.Contains(held.Diagnostics, text => text.Contains("Required evidence", StringComparison.Ordinal));
        Assert.Equal(calls, File.ReadAllLines(Path.Combine(repository.Path, ".cis/native-calls.log")));
        foreach (var pair in reportPaths)
        {
            Assert.True(File.Exists(pair.Value), "The original protected report was removed: " + pair.Key);
            var actual = File.ReadAllBytes(pair.Value);
            Assert.Equal(original[pair.Key], actual);
            Assert.Equal(SHA256.HashData(original[pair.Key]), SHA256.HashData(actual));
        }
        if (forcedReplacement)
        {
            var authorized = service.Run(Options("WORK-091", WorkActor, true), 0, TestContext.Current.CancellationToken);
            Assert.Equal(4, authorized.ExitCode); // The deliberately failed unit remains failed.
            Assert.Equal(1, authorized.ExecutedUnitBatches);
            var x = seeded.Batches.Single(batch => batch.SourceFile == "src/X.cs").Id;
            Assert.NotEqual(original[x], File.ReadAllBytes(reportPaths[x]));
        }
    }

    private sealed class ProtectedEvidenceGraph(string repository) : ICisGraphSnapshotReader
    {
        public CisGraphSnapshotReadResult Read(string repositoryPath)
        {
            var paths = new[] { "src/X.cs", "src/Y.cs", "tests/XTests.cs", "tests/YTests.cs", "config.json" };
            var nodes = new[] { Node("x", paths[0]), Node("y", paths[1]), Node("xt", paths[2], "Fixture.XTests"), Node("yt", paths[3], "Fixture.YTests") };
            var edges = new[] { Edge("xt", "x"), Edge("yt", "y") };
            var build = new CisGraphBuildMetadata("protected-report-graph", "agent-fixture", null, true, "complete");
            var graph = new CisGraphDocument(6, build, nodes, edges, new(0, 0, 0));
            var manifest = new CisGraphManifest(6, build.Id, "agent-fixture", "docs/cis", null, true,
                paths.Select(path => new CisGraphInput(path, Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(repository, path)))))).ToArray(), ["fixture/1"]);
            return new("available", 0, repository, "fresh", graph, manifest, ["compiler.calls"], [], []);
        }

        private static CisGraphNode Node(string key, string path, string? test = null)
            => new(key, "agent-fixture", "symbol", test is null ? "method" : "class", key, key,
                ["compiler-bound", "csharp"], "derived", "active", [new(path, "line", "1")],
                test is null ? new Dictionary<string, string>() : new() { ["qualifiedName"] = test, ["component"] = "fixture-tests" }, []);
        private static CisGraphEdge Edge(string from, string to)
            => new(from + "->" + to, "calls", from, to, "discovered", "high", [], new Dictionary<string, string>());
    }
}
