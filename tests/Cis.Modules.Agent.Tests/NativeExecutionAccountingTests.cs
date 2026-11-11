using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void NativeAccounting_UnknownStopChargesTheAdmittedBoundWithoutReconciliationWaiting(int delaySeconds)
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateNativeAccounting(f, identity);
        var stage = new CisAgentWorkStage("native-testing", "unit-profile", "native-verification", f.Context.RepositoryId)
        { NativeOperation = "unit-verification" };
        var root = f.Ledger.Admit(f.Context, identity, "native/unknown-stop-root", 60, 180, stage: stage, controller: "Controller");
        Assert.True(root.Admitted);
        var child = f.Ledger.Admit(f.Context, identity, "native/unknown-stop-child", 30, 180,
            root.Reference!.ExecutionId, stage: stage, controller: "Controller", nativeChildContinuation: true);
        Assert.True(child.Admitted);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        f.Time = f.Time.AddSeconds(delaySeconds);

        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var result = f.Ledger.Disposition(f.Context, identity, new("reconcile", before.Revision)
        { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:verified-stopped-native-tree"] },
            "Controller", "Reconcile the same stopped invocation without charging session waiting.",
            verifiedNativeTree: f.Ledger.NativeExecutionTree(f.Context, root.Reference));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(60_000, result.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(0, result.Work.Task.ReservedMilliseconds);
        Assert.Equal(110_000, result.Work.Task.RemainingMilliseconds);
        Assert.Equal(1, result.Work.Task.UsedAttempts);
        Assert.All(result.Work.Executions, execution =>
        {
            Assert.True(execution.Finalized);
            Assert.Null(execution.ObservedStoppedAtUtc);
        });
        var repeated = f.Ledger.Disposition(f.Context, identity, new("reconcile", result.Work.Revision)
        { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:same-stopped-native-tree"] },
            "Controller", "Inspect the idempotent settlement.");
        Assert.Equal("unchanged", repeated.Status);
        Assert.Equal(result.Work.Task, repeated.Work!.Task);
    }

    [Theory]
    [InlineData(15, 60)]
    [InlineData(15, 3600)]
    [InlineData(15, 86400)]
    [InlineData(90, 60)]
    [InlineData(90, 3600)]
    [InlineData(90, 86400)]
    public void NativeAccounting_ObservedDurationAndOverrunSurviveDelayedFinalization(int durationSeconds, int delaySeconds)
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateNativeAccounting(f, identity);
        var stage = new CisAgentWorkStage("native-testing", "unit-profile", "native-verification", f.Context.RepositoryId)
        { NativeOperation = "unit-verification" };
        var root = f.Ledger.Admit(f.Context, identity, "native/observed-stop", 60, 180, stage: stage, controller: "Controller");
        Assert.True(root.Admitted);
        f.Time = f.Time.AddSeconds(durationSeconds);
        var observed = f.Time;
        f.Time = f.Time.AddSeconds(delaySeconds);

        var result = f.Ledger.Settle(f.Context, root.Reference!, "Interrupted", true,
            "Retain the actual stop observation, including any overrun.", ["fixture:observed-stop"],
            conservativeRecovery: true, observedStoppedAt: observed);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(durationSeconds * 1000L, result.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal((170 - durationSeconds) * 1000L, result.Work.Task.RemainingMilliseconds);
        Assert.Equal(observed.ToString("O"), Assert.Single(result.Work.Executions).ObservedStoppedAtUtc);
        Assert.Equal(durationSeconds * 1000L, Assert.Single(result.Work.Executions).ActiveMilliseconds);
        var again = f.Ledger.Settle(f.Context, root.Reference!, "Interrupted", true,
            conservativeRecovery: true, observedStoppedAt: observed);
        Assert.Equal("unchanged", again.Status);
        Assert.Equal(result.Work.Task, again.Work!.Task);
    }

    [Fact]
    public void NativeAccounting_LegacyObservedParentOverrunIsNotReplacedByLaterReconciliation()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateNativeAccounting(f, identity);
        var stage = new CisAgentWorkStage("native-testing", "unit-profile", "native-verification", f.Context.RepositoryId)
        { NativeOperation = "unit-verification" };
        var root = f.Ledger.Admit(f.Context, identity, "native/observed-parent", 60, 180, stage: stage, controller: "Controller");
        Assert.True(root.Admitted);
        var child = f.Ledger.Admit(f.Context, identity, "native/unknown-child", 30, 180,
            root.Reference!.ExecutionId, stage: stage, controller: "Controller", nativeChildContinuation: true);
        Assert.True(child.Admitted);
        f.Time = f.Time.AddSeconds(90);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference, "interrupted", true).ExitCode);
        // Reproduce an older ordinary stopped parent, which retained its actual
        // stop in Completed/closed intervals before a separate observation existed.
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!;
        ledger["changes"]![identity.ChangeId]!["executions"]![root.Reference.ExecutionId]!.AsObject().Remove("observedStopped");
        File.WriteAllText(ledgerPath, ledger.ToJsonString());
        f.Time = f.Time.AddDays(1);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;

        var result = f.Ledger.Disposition(f.Context, identity, new("reconcile", before.Revision)
        { ExecutionId = root.Reference.ExecutionId, Evidence = ["fixture:verified-stopped-tree"] }, "Controller",
            "Retain the genuine observed parent overrun and exclude reconciliation waiting.",
            verifiedNativeTree: f.Ledger.NativeExecutionTree(f.Context, root.Reference));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(90_000, result.Work!.Task!.ConsumedMilliseconds);
        Assert.NotNull(result.Work.Executions.Single(execution => execution.ExecutionId == root.Reference.ExecutionId).ObservedStoppedAtUtc);
        Assert.Null(result.Work.Executions.Single(execution => execution.ExecutionId == child.Reference!.ExecutionId).ObservedStoppedAtUtc);
    }

    private static void AllocateNativeAccounting(Fixture f, CisAgentWorkIdentity identity)
    {
        var allocation = new CisAgentWorkAllocationRequest(180, 3, 10, [new(identity.TaskId, 180, 3, 10)],
            "fixture:approved-allocation", "fixture:finite-storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller",
            "Bound native work without charging later reconciliation waiting.").ExitCode);
    }
}

public sealed partial class AgentExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWork_FinishRetainsObservedStopAfterItsFirstLedgerTransactionFails(bool launched)
    {
        using var repository = AgentRepository.Create();
        AdoptWorkControl(repository);
        var clock = new WorkControlClock();
        var service = WorkController(new FakeProvider(), clock);
        AllocateWorkFixture(repository, service);
        var native = (ICisNativeWorkController)service;
        var admission = native.Admit(new(repository.Path, "native/stop-before-busy-ledger", "unit-verification", 30,
            new("CIS-0001", "WORK-090", AuthorityPath: repository.Path, Actor: WorkActor)));
        Assert.True(admission.Allowed, string.Join("; ", admission.Errors));
        var scope = admission.Scope!;
        if (launched)
        {
            scope.Launch();
            WithWorkProcess(process =>
            {
                scope.Started(process.Id, process.StartTime.ToUniversalTime().ToString("O"));
                clock.Advance(2);
                process.Kill(entireProcessTree: true);
                Assert.True(process.WaitForExit(5000));
                scope.Returned(safelyStopped: true);
            });
        }
        else clock.Advance(2);
        var observedStop = clock.Read();
        var ledgerPath = Path.Combine(repository.Path, CisAgentWorkPaths.Ledger);
        var before = File.ReadAllBytes(ledgerPath);
        using (var held = new FileStream(Path.Combine(repository.Path, CisAgentWorkPaths.Lock), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None))
        {
            var first = scope.Finish("native-finished", "The owned invocation was observed stopped.", ["fixture:observed-stop"]);
            Assert.Equal(4, first.ExitCode);
            Assert.False(first.Applied);
        }
        Assert.Equal(before, File.ReadAllBytes(ledgerPath));
        clock.Advance(3600);

        scope.Dispose();

        var settled = native.InspectSettlement(repository.Path, scope.Reference);
        Assert.True(CisNativeWorkSettlement.Complete(settled, scope.Reference), string.Join("; ", settled.Errors));
        Assert.Equal(2_000, settled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(0, settled.Work.Task.ReservedMilliseconds);
        Assert.Equal(observedStop.ToString("O"), Assert.Single(settled.Work.Executions).ObservedStoppedAtUtc);
        var retained = File.ReadAllBytes(ledgerPath);
        scope.Dispose();
        Assert.Equal(retained, File.ReadAllBytes(ledgerPath));
    }
}
