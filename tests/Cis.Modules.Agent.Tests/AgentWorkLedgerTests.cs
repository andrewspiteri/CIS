using System.Text.Json.Nodes;
using Cis.Abstractions;
using Cis.Modules.Agent;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public async Task WorkLedger_NestedActiveIntervalsAreInclusiveWhileIndependentRootsAndSettlementRemainDistinct()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "cis-agent-work-ledger-tests");
        var root = Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var current = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
            var baseline = current;
            var context = new CisRepositoryContext(root, "fixture-authority", "docs", Path.Combine(root, "docs"), Path.Combine(root, "docs/catalog.yml"));
            var ledger = new AgentWorkLedger(() => current);
            var parent = new CisAgentWorkIdentity(context.RepositoryId, "CIS-0001", "WORK-A", [], ["approved outcome A"]);
            var child = new CisAgentWorkIdentity(context.RepositoryId, "CIS-0001", "WORK-CHILD", ["WORK-A"], ["approved outcome A"]);
            var sibling = new CisAgentWorkIdentity(context.RepositoryId, "CIS-0001", "WORK-B", [], ["approved outcome B"]);
            var allocation = new CisAgentWorkAllocationRequest(180, 10, 0,
                [new("WORK-A", 120, 5), new("WORK-B", 120, 5)], "approved fixture allocation", "fixture storage ceiling",
                AcknowledgeHistoricalUnknown: true)
            { Controllers = ["Controller"] };
            Assert.Equal(0, ledger.Allocate(context, "CIS-0001", allocation, [parent, sibling], "Controller", "Synthetic authorized allocation.").ExitCode);

            var parentRun = ledger.Admit(context, parent, "parent/1", 60, 3600);
            Assert.True(parentRun.Admitted);
            current = baseline.AddSeconds(2);
            var childRun = ledger.Admit(context, child, "child/1", 30, 3600, parentRun.Reference!.ExecutionId);
            Assert.True(childRun.Admitted);
            Assert.Equal(parentRun.Reference.LogicalAttemptId, childRun.Reference!.LogicalAttemptId);
            Assert.Equal("WORK-A", childRun.Reference.OwnerTaskId);
            current = baseline.AddSeconds(3);
            var independent = ledger.Admit(context, sibling, "independent/1", 60, 3600);
            Assert.True(independent.Admitted);

            current = baseline.AddSeconds(8);
            Assert.Equal(0, ledger.Settle(context, independent.Reference!, "Succeeded", true, "Independent result.", ["independent result evidence"]).ExitCode);
            current = baseline.AddSeconds(9);
            Assert.Equal(0, ledger.Settle(context, childRun.Reference, "Succeeded", true, "Nested result.", ["child evidence"]).ExitCode);
            current = baseline.AddSeconds(10);
            var ledgerPath = Path.Combine(root, AgentWorkLedger.RelativePath);
            using var retainedSnapshot = new FileStream(ledgerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var reads = Task.Run(() => Enumerable.Range(0, 8).Select(_ => ledger.Read(context, "CIS-0001", parent.TaskId)).ToArray(), TestContext.Current.CancellationToken);
            var settled = ledger.Settle(context, parentRun.Reference, "Succeeded", true, "Parent result.", ["parent evidence"]);
            Assert.All(await reads, read => Assert.True(read.ExitCode == 0, string.Join("; ", read.Errors)));
            Assert.True(retainedSnapshot.Length > 0);
            Assert.True(settled.ExitCode == 0, string.Join("; ", settled.Errors));

            Assert.Equal(10_000, settled.Work!.Task!.ConsumedMilliseconds);
            Assert.Equal(15_000, settled.Work.Change.ConsumedMilliseconds);
            Assert.Equal(0, settled.Work.Change.ReservedMilliseconds);
            Assert.Equal(2, settled.Work.Change.UsedAttempts);
            Assert.Equal(7_000, settled.Work.Executions.Single(execution => execution.ExecutionId == "child/1").ActiveMilliseconds);
            Assert.Equal(10_000, settled.Work.Executions.Single(execution => execution.ExecutionId == "parent/1").ActiveMilliseconds);
            current = baseline.AddSeconds(20);
            Assert.Equal("unchanged", ledger.Settle(context, parentRun.Reference, "Cancelled", true).Status);
            var before = File.ReadAllBytes(ledgerPath);
            var reopened = new AgentWorkLedger(() => current).Read(context, "CIS-0001", child.TaskId);
            Assert.Equal("WORK-A", reopened.Work!.OwnerTaskId);
            Assert.Equal(15_000, reopened.Work.Change.ConsumedMilliseconds);
            Assert.Equal(20_000, reopened.Work.ElapsedMilliseconds);
            Assert.Equal(before, File.ReadAllBytes(ledgerPath));
        }
        finally
        {
            if (!CisPathSafety.IsUnderRoot(fixtureRoot, root)) throw new InvalidOperationException();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void WorkLedger_AllocationAuthorityAddsIndependentRootWhileAnotherOwnersUncertaintyRetainsItsCapacity()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A"); var sibling = f.Identity("WORK-B"); var child = f.Identity("WORK-B-CHILD", [sibling.TaskId]);
        var initial = new CisAgentWorkAllocationRequest(240, 10, 5, [new(first.TaskId, 90, 3, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Delegate"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, initial, [first], "Allocator", "Initial approved scope.").ExitCode);
        var uncertain = f.Ledger.Admit(f.Context, first, "uncertain/1", 60, 3600);
        Assert.True(uncertain.Admitted);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, uncertain.Reference!).ExitCode);
        f.Time = f.Time.AddSeconds(1);
        f.Ledger.MarkRecoveryUncertain(f.Context, uncertain.Reference!, "The first owner's launched process is unresolved.");
        var before = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
        var add = initial with { Tasks = [new(sibling.TaskId, 120, 3, 5)], ExpectedRevision = before.Revision };
        var denied = f.Ledger.Allocate(f.Context, first.ChangeId, add, [sibling], "Delegate", "A delegated controller cannot create another owner.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains(denied.Errors, error => error.Contains("original allocation authority", StringComparison.Ordinal));
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, add, [sibling], "Allocator", "Assign unused approved outer capacity.").ExitCode);

        var independent = f.Ledger.Admit(f.Context, sibling, "independent/1", 60, 3600);
        Assert.True(independent.Admitted, string.Join("\n", independent.Result.Errors));
        f.Time = f.Time.AddSeconds(2);
        var nested = f.Ledger.Admit(f.Context, child, "independent-child/1", 30, 3600, independent.Reference!.ExecutionId);
        Assert.True(nested.Admitted);
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, nested.Reference!, "Succeeded", true).ExitCode);
        f.Time = f.Time.AddSeconds(2);
        var finished = f.Ledger.Settle(f.Context, independent.Reference, "Succeeded", true);
        Assert.Equal(0, finished.ExitCode);
        Assert.Equal(5_000, finished.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(60_000, finished.Work.Change.ReservedMilliseconds);
        Assert.Equal("known", finished.Work.RecoveryState);
        Assert.Equal("uncertain", f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!.RecoveryState);
        Assert.False(f.Ledger.Admit(f.Context, first, "unchanged-repeat/1", 30, 3600).Admitted);

        var fixedIncrease = initial with { Tasks = [new(first.TaskId, 100, 3, 5)], ExpectedRevision = finished.Work.Revision };
        Assert.NotEqual(0, f.Ledger.Allocate(f.Context, first.ChangeId, fixedIncrease, [first], "Allocator", "Cannot override the fixed task cap.").ExitCode);
        var descendant = f.Identity("WORK-A-CHILD", [first.TaskId]);
        var split = initial with { Tasks = [new(descendant.TaskId, 30, 1)], ExpectedRevision = finished.Work.Revision };
        Assert.NotEqual(0, f.Ledger.Allocate(f.Context, first.ChangeId, split, [descendant], "Allocator", "Decomposition retains its existing owner.").ExitCode);
        Assert.Equal(60_000, f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!.Task!.ReservedMilliseconds);
    }

    [Fact]
    public void WorkLedger_NeverLaunchedReconciliationChargesConservativelyAndCannotReleaseALaunchedIntent()
    {
        using var f = new Fixture(); var task = f.Identity("WORK-A"); var historicalTask = f.Identity("WORK-HISTORICAL");
        var allocation = new CisAgentWorkAllocationRequest(180, 5, 0, [new(task.TaskId, 120, 5), new(historicalTask.TaskId, 60, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true);
        Assert.Equal(0, f.Ledger.Allocate(f.Context, task.ChangeId, allocation, [task, historicalTask], "Controller", "Fixture baseline.").ExitCode);
        var historical = f.Ledger.Admit(f.Context, historicalTask, "historical-preparing/1", 30, 3600);
        Assert.True(historical.Admitted);
        var preparationOwner = new CisAgentWorkPreparationOwner("fixture-host", 123, f.Time.ToString("O"));
        var reserved = f.Ledger.Admit(f.Context, task, "preparing/1", 60, 3600, preparationOwner: preparationOwner);
        var early = new CisAgentWorkDispositionRequest("reconcile", reserved.Result.Work!.Revision) { ExecutionId = reserved.Reference!.ExecutionId };
        Assert.NotEqual(0, f.Ledger.Disposition(f.Context, task, early, "Controller", "Wait for the preparation grace period.").ExitCode);
        f.Time = f.Time.AddSeconds(31);
        var historicalState = f.Ledger.Read(f.Context, historicalTask.ChangeId, historicalTask.TaskId).Work!;
        var unknown = f.Ledger.Disposition(f.Context, historicalTask,
            new("reconcile", historicalState.Revision) { ExecutionId = historical.Reference!.ExecutionId }, "Controller", "Missing historical ownership does not prove stopped work.", preparationOwner, true);
        Assert.NotEqual(0, unknown.ExitCode);
        Assert.Equal("uncertain", unknown.Work!.RecoveryState);
        Assert.Equal(30_000, unknown.Work.Task!.ReservedMilliseconds);
        var stillLive = f.Ledger.Disposition(f.Context, task, early, "Controller", "A live preparer retains its reservation.", preparationOwner, false);
        Assert.NotEqual(0, stillLive.ExitCode);
        Assert.Equal(60_000, stillLive.Work!.Task!.ReservedMilliseconds);
        var reconciled = f.Ledger.Disposition(f.Context, task, early, "Controller", "Reconcile the orphaned never-launched preparation.", preparationOwner, true);
        Assert.Equal(0, reconciled.ExitCode);
        Assert.Equal(60_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(0, reconciled.Work.Task.ReservedMilliseconds);
        Assert.True(reconciled.Work.Executions.Single().Finalized);
        Assert.False(reconciled.Work.Executions.Single().LaunchIntent);
        Assert.NotEqual(0, f.Ledger.MarkLaunchIntent(f.Context, reserved.Reference).ExitCode);

        var launched = f.Ledger.Admit(f.Context, task, "launched/1", 30, 3600);
        Assert.True(launched.Admitted);
        var intent = f.Ledger.MarkLaunchIntent(f.Context, launched.Reference!);
        f.Time = f.Time.AddSeconds(31);
        var held = f.Ledger.Disposition(f.Context, task,
            new("reconcile", intent.Work!.Revision) { ExecutionId = launched.Reference!.ExecutionId }, "Controller", "A missing PID does not prove stopped execution.");
        Assert.NotEqual(0, held.ExitCode);
        Assert.Equal(30_000, held.Work!.Task!.ReservedMilliseconds);
        Assert.False(held.Work.Executions.Single(execution => execution.ExecutionId == launched.Reference.ExecutionId).Finalized);
    }

    [Fact]
    public void WorkLedger_DelegatedTransfersPreserveProtectedRestorationAlongsideMeasuredAndReservedWork()
    {
        using var f = new Fixture(); var first = f.Identity("WORK-A"); var sibling = f.Identity("WORK-B");
        var initial = new CisAgentWorkAllocationRequest(240, 10, 30,
            [new(first.TaskId, 120, 5, 30, false, true), new(sibling.TaskId, 60, 3, 10, false, true)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Delegate"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, initial, [first, sibling], "Allocator", "Approve protected restoration and transferable capacity.").ExitCode);
        var measured = f.Ledger.Admit(f.Context, first, "measured/1", 30, 3600);
        Assert.True(measured.Admitted);
        f.Time = f.Time.AddSeconds(10);
        Assert.Equal(0, f.Ledger.Settle(f.Context, measured.Reference!, "Succeeded", true).ExitCode);
        var active = f.Ledger.Admit(f.Context, first, "active/1", 30, 3600);
        Assert.True(active.Admitted);

        var transfer = initial with
        {
            Tasks = [new(first.TaskId, 100, 5, 30, false, true), new(sibling.TaskId, 80, 3, 10, false, true)],
            ExpectedRevision = active.Result.Work!.Revision
        };
        var transferred = f.Ledger.Allocate(f.Context, first.ChangeId, transfer, [first, sibling], "Delegate", "Transfer only unused capacity to the sibling task.");
        Assert.True(transferred.ExitCode == 0, string.Join("; ", transferred.Errors));
        var protectedTask = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!.Task!;
        Assert.Equal(100_000, protectedTask.AllocatedMilliseconds);
        Assert.Equal(10_000, protectedTask.ConsumedMilliseconds);
        Assert.Equal(30_000, protectedTask.ReservedMilliseconds);
        Assert.Equal(30_000, protectedTask.RestorationMilliseconds);
        Assert.Equal(80_000, f.Ledger.Read(f.Context, sibling.ChangeId, sibling.TaskId).Work!.Task!.AllocatedMilliseconds);

        var reduceReserve = transfer with
        {
            Tasks = [new(first.TaskId, 100, 5, 0, false, true), new(sibling.TaskId, 80, 3, 10, false, true)],
            ExpectedRevision = transferred.Work!.Revision
        };
        var denied = f.Ledger.Allocate(f.Context, first.ChangeId, reduceReserve, [first, sibling], "Delegate", "Ordinary redistribution cannot turn protected restoration into execution capacity.");
        Assert.NotEqual(0, denied.ExitCode);
        Assert.Contains(denied.Errors, error => error.Contains("restoration", StringComparison.OrdinalIgnoreCase));
        var unchanged = f.Ledger.Read(f.Context, first.ChangeId, first.TaskId).Work!;
        Assert.Equal(transferred.Work.Revision, unchanged.Revision);
        Assert.Equal(protectedTask, unchanged.Task);
        f.Time = f.Time.AddSeconds(10);
        Assert.Equal(0, f.Ledger.Settle(f.Context, active.Reference!, "Succeeded", true).ExitCode);
        Assert.False(f.Ledger.Admit(f.Context, first, "cannot-spend-restoration/1", 60, 3600).Admitted);
    }

    [Fact]
    public void WorkLedger_CumulativeExecutionWindowRequiresControllerReflectionAndKeepsLegacyProgressAndAttempt()
    {
        using var f = new Fixture(); var task = f.Identity("WORK-A");
        var initial = new CisAgentWorkAllocationRequest(10_800, 5, 0, [new(task.TaskId, 10_800, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true);
        Assert.Equal(0, f.Ledger.Allocate(f.Context, task.ChangeId, initial, [task], "Controller", "Approve a bounded multi-window logical attempt.").ExitCode);
        string? logicalAttempt = null;
        for (var index = 0; index < 2; index++)
        {
            var run = f.Ledger.Admit(f.Context, task, "window/" + index, 1800, 3600);
            Assert.True(run.Admitted, string.Join("; ", run.Result.Errors));
            logicalAttempt ??= run.Reference!.LogicalAttemptId;
            Assert.Equal(logicalAttempt, run.Reference!.LogicalAttemptId);
            f.Time = f.Time.AddSeconds(1800);
            Assert.Equal(0, f.Ledger.Settle(f.Context, run.Reference, "Succeeded", true, "Retained bounded result.", ["retained result evidence"]).ExitCode);
        }
        var measured = f.Ledger.Read(f.Context, task.ChangeId, task.TaskId).Work!;
        Assert.Equal(3_600_000, measured.Task!.ConsumedMilliseconds);
        Assert.Equal(1, measured.Task.UsedAttempts);
        Assert.All(measured.Checkpoints, checkpoint => Assert.Equal("CIS measured execution", checkpoint.Actor));
        var disposition = f.Ledger.Disposition(f.Context, task,
            new("continue", measured.Revision) { Evidence = ["retained result evidence"], NextAction = "Inspect the next bounded action." },
            "Controller", "Recording a disposition does not assess outcome and approach.");
        Assert.Equal(0, disposition.ExitCode);
        RemoveCheckpointOrigins(f.Context.RepositoryPath);
        var reopened = new AgentWorkLedger(() => f.Time);
        var due = reopened.Admit(f.Context, task, "reflection-due/1", 30, 3600);
        Assert.False(due.Admitted);
        Assert.NotEqual(0, due.Result.ExitCode);
        Assert.Contains("progress", due.Result.Work!.NextAction, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(logicalAttempt, due.Result.Work.LogicalAttemptId);
        Assert.Equal(3_600_000, due.Result.Work.Task!.ConsumedMilliseconds);

        var checkpoint = new CisAgentWorkCheckpoint("The retained result supports another bounded step.", ["retained result evidence"],
            "Continue the measured approach within the unchanged scope.", "Continue after inspecting the measured cost and result.", "Run one short verification.");
        var reflected = reopened.Progress(f.Context, task, new(checkpoint, due.Result.Work.Revision), "Controller");
        Assert.Equal(0, reflected.ExitCode);
        // Historical explicit controller Progress has no origin metadata. Its
        // producer shape remains distinct from automatic/disposition summaries.
        RemoveCheckpointOrigins(f.Context.RepositoryPath);
        var continued = new AgentWorkLedger(() => f.Time).Admit(f.Context, task, "after-controller-reflection/1", 30, 3600);
        Assert.True(continued.Admitted, string.Join("; ", continued.Result.Errors));
        Assert.Equal(logicalAttempt, continued.Reference!.LogicalAttemptId);
        Assert.Equal(1, continued.Result.Work!.Task!.UsedAttempts);
        Assert.Equal(3_600_000, continued.Result.Work.Task.ConsumedMilliseconds);
        f.Time = f.Time.AddSeconds(1);
        var duringExecution = f.Ledger.Progress(f.Context, task, new(checkpoint, continued.Result.Work.Revision), "Controller");
        Assert.Equal(0, duringExecution.ExitCode);
        Assert.Equal(3_601_000, duringExecution.Work!.Checkpoints.Last().ActiveMilliseconds);
        f.Time = f.Time.AddSeconds(29);
        Assert.Equal(0, f.Ledger.Settle(f.Context, continued.Reference, "Succeeded", true).ExitCode);
        var nextWindow = f.Ledger.Admit(f.Context, task, "continued-window/1", 30, 3600);
        Assert.True(nextWindow.Admitted, string.Join("; ", nextWindow.Result.Errors));
        f.Time = f.Time.AddSeconds(1);
        Assert.Equal(0, f.Ledger.Settle(f.Context, nextWindow.Reference!, "Succeeded", true).ExitCode);
        var narrowDue = f.Ledger.Admit(f.Context, task, "narrower-reflection-due/1", 30, 30);
        Assert.False(narrowDue.Admitted);
        Assert.True(narrowDue.Result.Work!.ReflectionDue);
        var ledgerPath = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var beforeStatus = File.ReadAllBytes(ledgerPath);
        Assert.False(f.Ledger.Read(f.Context, task.ChangeId, task.TaskId).Work!.ReflectionDue);
        var currentPolicyStatus = f.Ledger.Read(f.Context, task.ChangeId, task.TaskId, reflectionWindowSeconds: 30);
        Assert.True(currentPolicyStatus.Work!.ReflectionDue);
        Assert.Equal(30_000, currentPolicyStatus.Work.ReflectionWindowMilliseconds);
        Assert.Equal(narrowDue.Result.Work.Revision, currentPolicyStatus.Work.Revision);
        Assert.Equal(beforeStatus, File.ReadAllBytes(ledgerPath));
        var held = f.Ledger.Disposition(f.Context, task, new("hold", f.Ledger.Read(f.Context, task.ChangeId, task.TaskId).Work!.Revision),
            "Controller", "A separate engineering hold still requires its own resolution.");
        Assert.Equal(0, held.ExitCode);
        var moreReflection = f.Ledger.Progress(f.Context, task, new(checkpoint, held.Work!.Revision), "Controller");
        Assert.Equal(0, moreReflection.ExitCode);
        Assert.False(f.Ledger.Admit(f.Context, task, "separate-hold/1", 30, 3600).Admitted);
    }

    private static void RemoveCheckpointOrigins(string root)
    {
        var path = Path.Combine(root, CisAgentWorkPaths.Ledger);
        var state = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        foreach (var change in state["changes"]!.AsObject().Select(item => item.Value!.AsObject()))
            foreach (var owner in change["owners"]!.AsObject().Select(item => item.Value!.AsObject()))
                foreach (var checkpoint in owner["checkpoints"]!.AsArray()) checkpoint!.AsObject().Remove("origin");
        File.WriteAllText(path, state.ToJsonString());
    }

    [Fact]
    public async Task WorkLedger_StoryRootsClampAtomicallyAndUncertainChildrenKeepTheirInclusiveReservation()
    {
        using var f = new Fixture();
        var first = f.Identity("WORK-A"); var second = f.Identity("WORK-B");
        var allocation = new CisAgentWorkAllocationRequest(180, 10, 10,
            [new(first.TaskId, 100, 4, 10), new(second.TaskId, 100, 4, 10)], "approved fixture allocation", "fixture storage ceiling",
            AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"] };
        Assert.Equal(0, f.Ledger.Allocate(f.Context, first.ChangeId, allocation, [first, second], "Controller", "Shared story capacity.").ExitCode);
        var denied = f.Ledger.Admit(f.Context, first, "unauthorized", 60, 3600, controller: "Other actor");
        Assert.False(denied.Admitted);
        Assert.Empty(f.Ledger.Read(f.Context, first.ChangeId).Work!.Executions);
        var roots = await Task.WhenAll(new[] { first, second }.Select(identity => Task.Run(() =>
            new AgentWorkLedger(() => f.Time).Admit(f.Context, identity, identity.TaskId + "/root", 3600, 3600,
                clampToRemaining: true, stage: new("story-task", identity.TaskId, "orchestration", "authority"), controller: "Controller"),
            TestContext.Current.CancellationToken)));
        Assert.All(roots, root => Assert.True(root.Admitted, string.Join("; ", root.Result.Errors)));
        Assert.Equal([80, 90], roots.Select(root => root.TimeoutSeconds).Order().ToArray());
        Assert.Equal(170_000, f.Ledger.Read(f.Context, first.ChangeId).Work!.Change.ReservedMilliseconds);
        var firstRoot = roots[0]; var secondRoot = roots[1];
        var correction = f.Ledger.BeginCorrection(f.Context, first, firstRoot.Reference, "feature/first", "Controller", ["required:material-basis"]);
        Assert.NotNull(correction.Round);
        f.Time = f.Time.AddSeconds(1);
        var child = f.Ledger.Admit(f.Context, first, "first/correction", 3600, 3600, firstRoot.Reference!.ExecutionId,
            firstRoot.Reference.LogicalAttemptId, clampToRemaining: true,
            stage: new("story-task", "feature/first", "correction", "backend", correction.Round!.Id), controller: "Controller");
        Assert.True(child.Admitted, string.Join("; ", child.Result.Errors));
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        f.Time = f.Time.AddSeconds(3);
        Assert.NotEqual(0, f.Ledger.Settle(f.Context, child.Reference!, "Interrupted", false).ExitCode);
        var waiting = f.Ledger.Settle(f.Context, firstRoot.Reference, "Interrupted", true);
        Assert.Equal(0, waiting.ExitCode);
        Assert.Equal(170_000, waiting.Work!.Change.ReservedMilliseconds);
        Assert.Equal("uncertain", waiting.Work.RecoveryState);
        f.Time = f.Time.AddSeconds(1);
        var stopped = f.Ledger.Settle(f.Context, child.Reference!, "Interrupted", true);
        Assert.Equal(5_000, stopped.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(secondRoot.TimeoutSeconds * 1000L, stopped.Work.Change.ReservedMilliseconds);
        Assert.Equal("unchanged", f.Ledger.Settle(f.Context, child.Reference!, "Interrupted", true).Status);
        f.Time = f.Time.AddSeconds(1);
        var allStopped = f.Ledger.Settle(f.Context, secondRoot.Reference!, "Succeeded", true);
        Assert.Equal(11_000, allStopped.Work!.Change.ConsumedMilliseconds);
        Assert.Equal(0, allStopped.Work.Change.ReservedMilliseconds);
        Assert.Equal(2, allStopped.Work.Change.UsedAttempts);
    }

    [Fact]
    public void WorkLedger_CorrectionHistorySurvivesAdoptionAndOneDispositionDoesNotRefillAutomaticRounds()
    {
        using var f = new Fixture();
        const string relationship = "feature/stable-instance";
        var first = f.Ledger.BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:material-basis"]);
        Assert.Equal(1, first.Round!.Number);
        Assert.True(first.Result.Work!.CorrectionOnly);
        Assert.False(f.Ledger.HasGovernedState(f.Context));
        Assert.False(File.Exists(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Adoption)));
        Assert.Equal(0, f.Ledger.RecordCorrectionStage(f.Context, null, null, relationship, first.Round.Id, "Controller",
            "backend", "implementation-1", "candidate-1", ["retained result-1"]).ExitCode);
        Assert.Equal(0, f.Ledger.CompleteCorrection(f.Context, null, null, relationship, first.Round.Id, "Controller").ExitCode);
        var reopened = new AgentWorkLedger(() => f.Time);
        var second = reopened.BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:material-basis"]);
        Assert.Equal(2, second.Round!.Number);
        Assert.Equal(0, reopened.CompleteCorrection(f.Context, null, null, relationship, second.Round.Id, "Controller").ExitCode);
        var exhausted = new AgentWorkLedger(() => f.Time).BeginCorrection(f.Context, null, null, relationship, "Controller", ["required:material-basis"]);
        Assert.Null(exhausted.Round);
        Assert.NotEqual(0, exhausted.Result.ExitCode);
        var beforeRead = File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger));
        var readLimit = reopened.ReadCorrections(f.Context, relationship);
        Assert.Equal("held", readLimit.Status);
        Assert.Contains("simplify disposition", readLimit.Work!.NextAction, StringComparison.Ordinal);
        Assert.Equal(beforeRead, File.ReadAllBytes(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)));
        var unmeasuredGrant = new CisAgentWorkDispositionRequest("simplify", exhausted.Result.Work!.Revision)
        {
            CorrectionRelationshipId = relationship,
            CorrectionFindingReferences = ["required:material-basis"],
            CorrectionMaximumSeconds = 60,
            Hypothesis = "Use the existing native operation.",
            NextAction = "Correct the one required invariant and review it.",
            Evidence = ["current review"]
        };
        Assert.NotEqual(0, reopened.DispositionCorrection(f.Context, relationship, unmeasuredGrant, "Controller", "A measured bound is required.").ExitCode);

        var identity = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(300, 5, 10, [new(identity.TaskId, 250, 4, 10)],
            "approved fixture allocation", "fixture storage ceiling", exhausted.Result.Work.Revision, true)
        { Controllers = ["Controller"] };
        var adopted = reopened.Allocate(f.Context, identity.ChangeId, allocation, [identity], "Controller", "Acknowledge historical effort and bound continuation.");
        Assert.Equal(0, adopted.ExitCode);
        var reconciled = reopened.ReconcileHistory(f.Context, identity, relationship, new Dictionary<string, long>
        { ["implementation-1"] = 20_000, ["review-1"] = 10_000 }, adopted.Work!.Revision, "Controller", "Conservatively retain old run bounds.");
        Assert.Equal(30_000, reconciled.Work!.Task!.ConsumedMilliseconds);
        var again = reopened.ReconcileHistory(f.Context, identity, relationship, new Dictionary<string, long>
        { ["implementation-1"] = 20_000, ["review-1"] = 10_000 }, reconciled.Work.Revision, "Controller", "Idempotent historical reconciliation.");
        Assert.Equal("unchanged", again.Status);
        Assert.Equal(30_000, again.Work!.Task!.ConsumedMilliseconds);
        var granted = reopened.Disposition(f.Context, identity, unmeasuredGrant with { ExpectedRevision = again.Work.Revision }, "Controller", "One justified bounded simpler correction.");
        Assert.Equal(0, granted.ExitCode);
        var root = reopened.Admit(f.Context, identity, "governed/root", 120, 3600,
            stage: new("story-task", relationship, "orchestration", "authority"), controller: "Controller");
        Assert.True(root.Admitted);
        var third = reopened.BeginCorrection(f.Context, identity, root.Reference, relationship, "Controller", ["required:material-basis"]);
        Assert.Equal(3, third.Round!.Number);
        Assert.Equal(60, third.Round.MaximumSeconds);
        Assert.NotNull(third.Round.DispositionReference);
        Assert.Equal(third.Round.Id, reopened.BeginCorrection(f.Context, identity, root.Reference, relationship, "Controller", ["required:material-basis"]).Round!.Id);
        f.Time = f.Time.AddSeconds(3); // Correction workspace preparation consumes the same inclusive grant.
        var correction = reopened.Admit(f.Context, identity, "bounded/correction", 120, 3600, root.Reference!.ExecutionId,
            root.Reference.LogicalAttemptId, clampToRemaining: true,
            stage: new("story-task", relationship, "correction", "backend", third.Round.Id), controller: "Controller");
        Assert.Equal(57, correction.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(20);
        Assert.Equal(0, reopened.Settle(f.Context, correction.Reference!, "Succeeded", true).ExitCode);
        var review = reopened.Admit(f.Context, identity, "bounded/review", 120, 3600, root.Reference.ExecutionId,
            root.Reference.LogicalAttemptId, clampToRemaining: true,
            stage: new("story-task", relationship, "review", "backend", third.Round.Id), controller: "Controller");
        Assert.True(review.Admitted);
        Assert.Equal(37, review.TimeoutSeconds);
        f.Time = f.Time.AddSeconds(5);
        Assert.Equal(0, reopened.Settle(f.Context, review.Reference!, "Succeeded", true).ExitCode);
        Assert.Equal(0, reopened.CompleteCorrection(f.Context, identity, root.Reference, relationship, third.Round.Id, "Controller").ExitCode);
        Assert.Null(reopened.BeginCorrection(f.Context, identity, root.Reference, relationship, "Controller", ["required:material-basis"]).Round);
        f.Time = f.Time.AddSeconds(1);
        var settled = reopened.Settle(f.Context, root.Reference, "changes-requested", true);
        Assert.Equal(59_000, settled.Work!.Task!.ConsumedMilliseconds);
        Assert.Equal(3, reopened.ReadCorrections(f.Context, relationship).Work!.Corrections.Count);
        Assert.Equal(1, settled.Work.Task.UsedAttempts);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string FixtureRoot = Path.Combine(Path.GetTempPath(), "cis-agent-work-ledger-tests");
        private readonly string root = Path.Combine(FixtureRoot, Guid.NewGuid().ToString("N"));
        public DateTimeOffset Time { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public CisRepositoryContext Context { get; }
        public AgentWorkLedger Ledger { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root);
            Context = new(root, "fixture-authority", "docs", Path.Combine(root, "docs"), Path.Combine(root, "docs/catalog.yml"));
            Ledger = new(() => Time);
        }
        public CisAgentWorkIdentity Identity(string taskId, IReadOnlyList<string>? ancestors = null)
            => new(Context.RepositoryId, "CIS-0001", taskId, ancestors ?? [], ["approved fixture outcome"]);
        public void Dispose()
        {
            if (!CisPathSafety.IsUnderRoot(FixtureRoot, root)) throw new InvalidOperationException();
            Directory.Delete(root, true);
        }
    }
}
