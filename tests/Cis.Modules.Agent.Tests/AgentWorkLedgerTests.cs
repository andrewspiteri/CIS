using Cis.Abstractions;
using Cis.Modules.Agent;

namespace Cis.Modules.Agent.Tests;

public sealed class AgentWorkLedgerTests
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
        using var f = new Fixture(); var task = f.Identity("WORK-A");
        var allocation = new CisAgentWorkAllocationRequest(180, 5, 0, [new(task.TaskId, 120, 5)],
            "approved fixture allocation", "fixture storage ceiling", AcknowledgeHistoricalUnknown: true);
        Assert.Equal(0, f.Ledger.Allocate(f.Context, task.ChangeId, allocation, [task], "Controller", "Fixture baseline.").ExitCode);
        var reserved = f.Ledger.Admit(f.Context, task, "preparing/1", 60, 3600);
        var early = new CisAgentWorkDispositionRequest("reconcile", reserved.Result.Work!.Revision) { ExecutionId = reserved.Reference!.ExecutionId };
        Assert.NotEqual(0, f.Ledger.Disposition(f.Context, task, early, "Controller", "Wait for the preparation grace period.").ExitCode);
        f.Time = f.Time.AddSeconds(31);
        var reconciled = f.Ledger.Disposition(f.Context, task, early, "Controller", "Reconcile the orphaned never-launched preparation.");
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
