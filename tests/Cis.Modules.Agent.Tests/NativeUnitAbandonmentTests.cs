using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(86400)]
    public void NativeUnitAbandonment_RetiredDisposableWorkKeepsItsChargeAndAttemptThenAllowsOrdinaryRetry(int waitSeconds)
    {
        using var fixture = new NativeUnitAbandonmentFixture();
        var before = fixture.Status();
        var adoption = File.ReadAllBytes(Path.Combine(fixture.Repository.Path, CisAgentWorkPaths.Adoption));
        var evidence = fixture.EvidenceFiles();
        Assert.True(fixture.Storage().ReservedBytes > 0);
        Assert.True(fixture.Driver!.HasExited);
        fixture.Clock.Advance(waitSeconds);

        var abandoned = fixture.Abandon();

        Assert.Equal(0, abandoned.ExitCode);
        Assert.Equal("abandoned", abandoned.Status);
        Assert.Equal(60_000, abandoned.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(0, abandoned.Work.Task.ReservedMilliseconds);
        Assert.Equal(before.Task!.UsedAttempts, abandoned.Work.Task.UsedAttempts);
        Assert.Equal(before.Task.AllocatedMilliseconds, abandoned.Work.Task.AllocatedMilliseconds);
        Assert.Equal(before.Task.RestorationMilliseconds, abandoned.Work.Task.RestorationMilliseconds);
        Assert.Equal(before.Change.AllocatedMilliseconds, abandoned.Work.Change.AllocatedMilliseconds);
        Assert.Equal(before.Change.RestorationMilliseconds, abandoned.Work.Change.RestorationMilliseconds);
        Assert.All(abandoned.Work.Executions, execution =>
        {
            Assert.True(execution.Finalized);
            Assert.Equal("Interrupted", execution.Status);
            Assert.Null(execution.ObservedStoppedAtUtc);
            Assert.NotNull(execution.NativeAbandonment);
            Assert.Equal("retired-environment", execution.NativeAbandonment.Basis);
            Assert.Equal(WorkActor, execution.NativeAbandonment.Actor);
        });
        Assert.Null(abandoned.Work.Executions.Single(execution => execution.ExecutionId == fixture.ChildId)
            .NativeContainment!.WindowsObjectPath);
        Assert.Equal(0, fixture.Storage().ReservedBytes);
        Assert.Contains(fixture.DiagnosticPath, fixture.Storage().ProtectedPaths);
        Assert.Equal(adoption, File.ReadAllBytes(Path.Combine(fixture.Repository.Path, CisAgentWorkPaths.Adoption)));
        fixture.AssertEvidenceUnchanged(evidence);
        Assert.True(CisNativeWorkSettlement.Complete(fixture.Native.InspectSettlement(fixture.Repository.Path,
            fixture.Reference), fixture.Reference));
        var settledBytes = File.ReadAllBytes(fixture.LedgerPath);
        var repeated = fixture.Abandon();
        Assert.Equal("unchanged", repeated.Status);
        Assert.False(repeated.Applied);
        Assert.Equal(settledBytes, File.ReadAllBytes(fixture.LedgerPath));

        var retry = fixture.Native.Admit(new(fixture.Repository.Path, "native/disposable-unit-retry", "incremental-unit", 30,
            fixture.Association));
        Assert.True(retry.Allowed, string.Join("; ", retry.Errors));
        using var retryScope = retry.Scope!;
        Assert.Equal(2, retry.Control!.Work!.Task!.UsedAttempts);
        Assert.NotEqual(fixture.Reference.LogicalAttemptId, retryScope.Reference.LogicalAttemptId);
        Assert.Equal(60_000, retry.Control.Work.Task.ConsumedMilliseconds);
        fixture.Clock.Advance(1);
        Assert.Equal(0, retryScope.Finish("native-finished", "The subsequent bounded unit invocation completed.",
            ["fixture:retry-finished"]).ExitCode);
    }

    [Theory]
    [InlineData("incremental-unit", true, true)]
    [InlineData("incremental-all", false, true)]
    [InlineData("incremental-all", true, false)]
    public void NativeUnitAbandonment_UsesTheActualUnitScopeRatherThanAnUnusedRecipe(string operation, bool recipe, bool allowed)
    {
        using var fixture = new NativeUnitAbandonmentFixture();
        fixture.EditLedger(ledger =>
        {
            var root = ledger["changes"]!["CIS-0001"]!["executions"]![fixture.Reference.ExecutionId]!;
            root["stage"]!["nativeOperation"] = operation;
            if (recipe)
            {
                root["stage"]!["nativeRecoveryBasis"] = "sha256:configured-mutation-recipe";
                root["nativeRestorationRequired"] = true;
            }
        });
        var before = fixture.Snapshot();
        var result = fixture.Abandon();
        Assert.Equal(allowed ? 0 : 4, result.ExitCode);
        Assert.Equal(allowed, result.Applied);
        if (allowed)
        {
            Assert.All(result.Work!.Executions, item => Assert.False(item.NativeRestorationRequired));
            Assert.Equal(recipe ? "sha256:configured-mutation-recipe" : null,
                result.Work.Executions.Single(item => item.ExecutionId == fixture.Reference.ExecutionId).Stage!.NativeRecoveryBasis);
        }
        else fixture.AssertUnchanged(before);
    }

    [Fact]
    public void NativeUnitAbandonment_KeepsACompletedChildReportAndItsVerifiedProvenance()
    {
        using var fixture = new NativeUnitAbandonmentFixture(completedChild: true);
        Assert.NotNull(fixture.CompletedReference);
        Assert.Equal("verified", fixture.Native.InspectExecution(fixture.Repository.Path, fixture.CompletedReference!).Status);
        var completedBefore = fixture.Status().Executions.Single(execution => execution.ExecutionId == fixture.CompletedReference.ExecutionId);
        var reportBefore = File.ReadAllBytes(fixture.CompletedReportPath!);
        var evidence = fixture.EvidenceFiles();
        fixture.Clock.Advance(86400);

        var result = fixture.Abandon();

        Assert.Equal(0, result.ExitCode);
        var completedAfter = result.Work!.Executions.Single(execution => execution.ExecutionId == fixture.CompletedReference.ExecutionId);
        Assert.True(completedAfter.Finalized);
        Assert.Null(completedAfter.NativeAbandonment);
        Assert.Equal(completedBefore.Status, completedAfter.Status);
        Assert.Equal(completedBefore.ObservedStoppedAtUtc, completedAfter.ObservedStoppedAtUtc);
        Assert.Equal(completedBefore.ProcessId, completedAfter.ProcessId);
        Assert.Equal(completedBefore.ProcessStartedAtUtc, completedAfter.ProcessStartedAtUtc);
        Assert.Equal(completedBefore.NativeContainment, completedAfter.NativeContainment);
        Assert.Equal("verified", fixture.Native.InspectExecution(fixture.Repository.Path, fixture.CompletedReference).Status);
        Assert.Equal(reportBefore, File.ReadAllBytes(fixture.CompletedReportPath!));
        Assert.Contains(fixture.CompletedReportPath!, fixture.Storage().ProtectedPaths);
        fixture.AssertEvidenceUnchanged(evidence);
        Assert.Equal(60_000, result.Work.Task!.ConsumedMilliseconds);
        Assert.Equal(1, result.Work.Task.UsedAttempts);
        var ledger = JsonNode.Parse(File.ReadAllBytes(fixture.LedgerPath))!;
        var claim = ledger["changes"]!["CIS-0001"]!["storage"]!["claims"]![fixture.CompletedReportPath!]!;
        Assert.Equal(fixture.CompletedReference.ExecutionId, claim["requiredByExecutionId"]!.GetValue<string>());
        var retry = fixture.Native.Admit(new(fixture.Repository.Path, "native/disposable-completed-child-retry", "incremental-unit", 30,
            fixture.Association));
        Assert.True(retry.Allowed, string.Join("; ", retry.Errors));
        using var scope = retry.Scope!;
        Assert.Equal(2, retry.Control!.Work!.Task!.UsedAttempts);
        Assert.Equal(0, scope.Finish("native-finished", "Retry preserves the completed child.", ["fixture:retry-completed"]).ExitCode);
    }

    [Theory]
    [InlineData("live-driver")]
    [InlineData("actor")]
    [InlineData("revision")]
    [InlineData("child")]
    [InlineData("mutation")]
    [InlineData("missing-evidence")]
    [InlineData("held-lease")]
    [InlineData("mixed-root")]
    public void NativeUnitAbandonment_RejectsConflictingOrUnqualifiedDisposalWithoutChangingAccounting(string invalid)
    {
        using var fixture = new NativeUnitAbandonmentFixture(liveDriver: invalid == "live-driver");
        if (invalid == "mutation") fixture.EditLedger(ledger =>
            ledger["changes"]!["CIS-0001"]!["executions"]![fixture.Reference.ExecutionId]!["stage"]!["nativeOperation"] = "mutation-run");
        if (invalid == "mixed-root") fixture.EditLedger(ledger =>
            ledger["changes"]!["CIS-0001"]!["executions"]![fixture.ChildId]!["rootId"] = fixture.ChildId);
        var request = fixture.Request();
        var actor = WorkActor;
        switch (invalid)
        {
            case "actor": actor = "Unapproved disposal actor"; break;
            case "revision": request = request with { ExpectedRevision = fixture.Reference.AdoptionId + ":0" }; break;
            case "child": request = request with { ExecutionId = fixture.ChildId }; break;
            case "missing-evidence": request = request with { Evidence = [] }; break;
        }
        var before = fixture.Snapshot();
        CisAgentWorkResult result;
        if (invalid == "held-lease")
        {
            using var held = new FileStream(fixture.LeasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            result = fixture.Abandon(request, actor);
        }
        else result = fixture.Abandon(request, actor);

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Errors);
        if (invalid == "live-driver") Assert.Contains(result.Errors, error => error.Contains("still live", StringComparison.Ordinal));
        fixture.AssertUnchanged(before);
    }

    [Fact]
    public void NativeUnitAbandonment_ProcessCleanupDoesNotConfuseAnotherHostsPreparerWithThisCallersPid()
    {
        using var fixture = new NativeUnitAbandonmentFixture(preparationOnly: true);
        fixture.EditLedger(ledger => ledger["changes"]!["CIS-0001"]!["executions"]![fixture.Reference.ExecutionId]!
            ["preparationOwner"]!["hostId"] = "fixture-other-host");
        var before = fixture.Snapshot();

        var result = fixture.Abandon(fixture.Request() with { DisposalBasis = "process-cleanup" });

        Assert.Equal(4, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, error => error.Contains("original host", StringComparison.Ordinal));
        fixture.AssertUnchanged(before);
    }

    [Fact]
    public void NativeUnitAbandonment_UsesRetainedOwnershipWhenTheTaskIsBlocked()
    {
        using var fixture = new NativeUnitAbandonmentFixture();
        fixture.Repository.Write("docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md",
            "---\ntask_status: Blocked\ntargets: [\"agent-fixture\"]\n---\n# Interrupted unit work\nRetain its original scope.\n");

        var result = fixture.Abandon();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(60_000, result.Work!.Task!.ConsumedMilliseconds);
        Assert.Contains("task_status: Blocked", File.ReadAllText(Path.Combine(fixture.Repository.Path,
            "docs/cis/changes/CIS-0001/agent-tasks/WORK-090.md")), StringComparison.Ordinal);
    }

    private sealed class NativeUnitAbandonmentFixture : IDisposable
    {
        public AgentRepository Repository { get; } = AgentRepository.Create();
        public WorkControlClock Clock { get; } = new();
        public AgentService Service { get; }
        public ICisNativeWorkController Native => Service;
        public ICisOwnedStorageController OwnedStorage => Service;
        public CisNativeWorkOptions Association { get; }
        public CisAgentWorkReference Reference { get; }
        public CisAgentWorkReference? CompletedReference { get; }
        public string ChildId { get; } = "native/disposable-interrupted-child";
        public string LedgerPath => Path.Combine(Repository.Path, CisAgentWorkPaths.Ledger);
        public string LeasePath => Path.Combine(Repository.Path, ".cis/local/testing/mutation/run.lock");
        public string DiagnosticPath => Path.GetFullPath(Path.Combine(Repository.Path, ".cis/local/testing/disposable-unit/driver.log"));
        public string? CompletedReportPath { get; }
        public Process? Driver { get; }

        public NativeUnitAbandonmentFixture(bool liveDriver = false, bool completedChild = false, bool preparationOnly = false)
        {
            AdoptWorkControl(Repository);
            Service = WorkController(new FakeProvider(), Clock);
            var allocation = WorkAllocation(180, 180, 30, ["WORK-090"]) with
            { Storage = new(16 * 1024 * 1024, [new(Repository.Path, ".cis/local")]) { RestorationBytes = 1024 * 1024 } };
            Assert.Equal(0, Service.AllocateWork(Repository.Path, "CIS-0001", WriteWorkInput(Repository,
                "disposable-unit-allocation.json", allocation), WorkActor, "Bound disposable unit recovery.").ExitCode);
            Association = new("CIS-0001", "WORK-090", AuthorityPath: Repository.Path, Actor: WorkActor);
            var admission = Native.Admit(new(Repository.Path, "native/disposable-unit-root", "incremental-unit", 60, Association));
            Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
            using var root = admission.Scope!;
            Reference = root.Reference;
            if (completedChild)
            {
                var completed = root.AdmitChild("native/disposable-completed-child", "native-unit", 10);
                Assert.True(completed.Allowed, string.Join("; ", completed.Errors));
                using var completedScope = completed.Scope!;
                CompletedReference = completedScope.Reference;
                CompletedReportPath = Path.GetFullPath(Path.Combine(Repository.Path, ".cis/local/testing/disposable-unit/accepted.trx"));
                var storage = OwnedStorage.Reserve(Repository.Path, CompletedReference, "current-unit-report", 4096,
                    [new(CompletedReportPath)], actor: WorkActor);
                Assert.True(storage.Allowed, string.Join("; ", storage.Result.Errors));
                completedScope.Launch();
                WithWorkProcess(process =>
                {
                    completedScope.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"));
                    Clock.Advance(2);
                    process.Kill(entireProcessTree: true);
                    Assert.True(process.WaitForExit(5000));
                    completedScope.Returned(safelyStopped: true);
                });
                Repository.Write(".cis/local/testing/disposable-unit/accepted.trx", "<TestRun><ResultSummary outcome=\"Completed\" /></TestRun>");
                Assert.True(OwnedStorage.Settle(Repository.Path, CompletedReference, storage.ReservationId!, true,
                    [CompletedReportPath]).Allowed);
                Assert.Equal(0, completedScope.Finish("native-finished", "Retain the completed child report.", [CompletedReportPath]).ExitCode);
            }
            var reserved = OwnedStorage.Reserve(Repository.Path, Reference, "interrupted-unit-output", 4096,
                [new(DiagnosticPath)], actor: WorkActor);
            Assert.True(reserved.Allowed, string.Join("; ", reserved.Result.Errors));
            Repository.Write(".cis/local/testing/disposable-unit/driver.log", "Interrupted disposable unit worker; no accepted result for this child.");
            Repository.Write(".cis/local/testing/disposable-unit/state.json", "{\"unit\":null,\"pending\":1}");
            Repository.Write(".cis/local/testing/profiles/disposable-unit.json", "{\"timeoutSeconds\":30,\"expectedCases\":2}");
            Repository.Write(".cis/local/testing/mutation/run.lock", "");
            var interrupted = root.AdmitChild(ChildId, "native-unit", 30);
            Assert.True(interrupted.Allowed, string.Join("; ", interrupted.Errors));
            using var child = interrupted.Scope!;
            if (!preparationOnly)
            {
                Driver = new() { StartInfo = SleepProcess(writeFirst: false) };
                Assert.True(Driver.Start());
                child.Launch();
                child.Started(Driver.Id, Driver.StartTime.ToUniversalTime().ToString("O"));
                if (!liveDriver) { Driver.Kill(entireProcessTree: true); Assert.True(Driver.WaitForExit(5000)); }
            }
            child.MarkUncertain("The old worker lacks its complete terminal receipt.");
            Assert.Equal(4, child.Finish("interrupted", "Preserve inconclusive native work.", [DiagnosticPath]).ExitCode);
            root.MarkUncertain("The interrupted child needs controlled disposal.");
            Assert.Equal(4, root.Finish("interrupted", "Retain the original invocation.", [DiagnosticPath]).ExitCode);
            if (!preparationOnly) EditLedger(ledger => ledger["changes"]!["CIS-0001"]!["executions"]![ChildId]!["nativeContainment"] =
                JsonSerializer.SerializeToNode(new CisNativeWorkContainment(CisNativeWorkContainment.WindowsOuterJob,
                    "Local\\cis-native-disposable-fixture", 1), JsonOptions));
        }

        public CisAgentWorkView Status() => Service.WorkStatus(Repository.Path, "CIS-0001", "WORK-090").Work!;
        public CisOwnedStorageView Storage() => OwnedStorage.Inspect(Repository.Path, Association).Storage!;
        public CisAgentWorkDispositionRequest Request() => new("abandon-native-unit", Status().Revision)
        {
            ExecutionId = Reference.ExecutionId,
            DisposalBasis = "retired-environment",
            Evidence = [Driver is null ? "fixture:retired-disposable-preparation" : $"fixture:worker-{Driver.Id}-retired"]
        };
        public CisAgentWorkResult Abandon(CisAgentWorkDispositionRequest? request = null, string actor = WorkActor)
            => Service.DisposeWork(Repository.Path, "CIS-0001", "WORK-090", WriteWorkInput(Repository,
                "disposable-unit-disposition.json", request ?? Request()), actor,
                "Retire this disposable invocation as inconclusive, preserving cost, attempts and valid reports.");
        public void EditLedger(Action<JsonNode> edit)
        {
            var ledger = JsonNode.Parse(File.ReadAllBytes(LedgerPath))!;
            edit(ledger);
            File.WriteAllText(LedgerPath, ledger.ToJsonString(JsonOptions));
        }
        public IReadOnlyDictionary<string, byte[]> Snapshot()
            => Directory.EnumerateFiles(Repository.Path, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(Repository.Path, path).Replace('\\', '/')
                    .StartsWith(".cis/local/work-test-input/", StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        public IReadOnlyDictionary<string, byte[]> EvidenceFiles() => Snapshot()
            .Where(pair => pair.Key.EndsWith(".log", StringComparison.Ordinal) || pair.Key.EndsWith(".trx", StringComparison.Ordinal)
                || pair.Key.EndsWith("/state.json".Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal)
                || pair.Key.EndsWith("disposable-unit.json", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        public void AssertUnchanged(IReadOnlyDictionary<string, byte[]> before)
        {
            var after = Snapshot();
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var pair in before) Assert.Equal(pair.Value, after[pair.Key]);
        }
        public void AssertEvidenceUnchanged(IReadOnlyDictionary<string, byte[]> before)
        { foreach (var pair in before) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)); }
        public void Dispose()
        {
            if (Driver is not null)
            {
                if (!Driver.HasExited) { Driver.Kill(entireProcessTree: true); Driver.WaitForExit(5000); }
                Driver.Dispose();
            }
            Repository.Dispose();
        }
    }
}
