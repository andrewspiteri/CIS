using System.Text.Json.Nodes;
using Cis.Abstractions;

namespace Cis.Modules.Agent.Tests;

public sealed partial class AgentWorkLedgerTests
{
    [Fact]
    public void SettledHistoryCompaction_PreservesCurrentNativeProofAndAccountingWhileNormalAdmissionContinues()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateCompactionFixture(f, identity);
        var stage = CompactionStage(f);
        var root = f.Ledger.Admit(f.Context, identity, "native/required-root", 60, 180, stage: stage, controller: "Controller");
        var child = f.Ledger.Admit(f.Context, identity, "native/required-child", 30, 180,
            root.Reference!.ExecutionId, stage: stage, controller: "Controller");
        Assert.True(child.Admitted);
        var report = Path.Combine(f.Context.RepositoryPath, ".cis/local/current-unit.trx");
        var storage = f.Ledger.ReserveStorage(f.Context, child.Reference!, "current-report", 1024,
            [new(report, false, true)], false, false, "Controller");
        Assert.True(storage.Allowed, string.Join("; ", storage.Result.Errors));
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, "retained current native report fixture");
        Assert.True(f.Ledger.SettleStorage(f.Context, child.Reference!, storage.ReservationId!, true, [report]).Allowed);
        Assert.Equal(0, f.Ledger.MarkLaunchIntent(f.Context, child.Reference!).ExitCode);
        Assert.Equal(0, f.Ledger.AttachProcess(f.Context, child.Reference!, 12345, f.Time.ToString("O")).ExitCode);
        f.Time = f.Time.AddSeconds(2);
        Assert.Equal(0, f.Ledger.Settle(f.Context, child.Reference!, "native-finished", true).ExitCode);
        Assert.Equal(0, f.Ledger.Settle(f.Context, root.Reference, "native-finished", true).ExitCode);
        CloseCompactionAttempt(f, identity);
        var currentProof = f.Ledger.NativeExecution(f.Context, child.Reference!.ExecutionId)!.Value;
        CisAgentWorkReference? obsolete = null;
        for (var index = 0; index < 12; index++)
        {
            var prior = f.Ledger.Admit(f.Context, identity, "native/settled-" + index, 30, 180, stage: stage, controller: "Controller");
            Assert.True(prior.Admitted, string.Join("; ", prior.Result.Errors));
            obsolete ??= prior.Reference;
            f.Time = f.Time.AddSeconds(2);
            Assert.Equal(0, f.Ledger.Settle(f.Context, prior.Reference!, "native-finished", true).ExitCode);
            CloseCompactionAttempt(f, identity);
        }
        InflateCompactionFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var reportBefore = File.ReadAllBytes(report);

        var next = f.Ledger.Admit(f.Context, identity, "native/ordinary-next", 30, 180, stage: stage, controller: "Controller");

        Assert.True(next.Admitted, string.Join("; ", next.Result.Errors) + next.Result.Work?.HoldReason);
        var after = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(before.Task!.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Change.ConsumedMilliseconds, after.Change.ConsumedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts + 1, after.Task.UsedAttempts);
        Assert.Equal(before.Change.UsedAttempts + 1, after.Change.UsedAttempts);
        var retained = f.Ledger.NativeExecution(f.Context, child.Reference.ExecutionId)!.Value;
        Assert.Equal(currentProof.Reference, retained.Reference);
        Assert.Equal(currentProof.Execution, retained.Execution);
        Assert.NotNull(f.Ledger.NativeExecution(f.Context, root.Reference.ExecutionId));
        Assert.Equal(reportBefore, File.ReadAllBytes(report));
        Assert.Null(f.Ledger.NativeExecution(f.Context, obsolete!.ExecutionId));
        var compacted = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)))!;
        Assert.True(compacted["changes"]![identity.ChangeId]!["compactedExecutionCount"]!.GetValue<long>() > 0);
        Assert.True(compacted["changes"]![identity.ChangeId]!["owners"]![identity.TaskId]!["compactedCheckpointCount"]!.GetValue<long>() > 0);
        Assert.True(new FileInfo(Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger)).Length < 1024 * 1024);

        Assert.Equal(0, f.Ledger.Settle(f.Context, next.Reference!, "native-finished", true).ExitCode);
        CloseCompactionAttempt(f, identity);
        var closed = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        var reopen = f.Ledger.Admit(f.Context, identity, "native/cannot-reopen", 30, 180,
            logicalAttemptId: obsolete.LogicalAttemptId, stage: stage, controller: "Controller");
        Assert.False(reopen.Admitted);
        Assert.Contains(reopen.Result.Errors, error => error.Contains("current logical attempt", StringComparison.Ordinal));
        var stillClosed = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Null(stillClosed.LogicalAttemptId);
        Assert.Equal(closed.Task!.UsedAttempts, stillClosed.Task!.UsedAttempts);
        Assert.Equal(closed.Task.ConsumedMilliseconds, stillClosed.Task.ConsumedMilliseconds);
    }

    [Fact]
    public void HistoryCompaction_PreservesUnresolvedAncestryAndDistinctOpenAttemptCheckpoints()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateCompactionFixture(f, identity);
        var stage = CompactionStage(f);
        for (var index = 0; index < 8; index++)
        {
            var prior = f.Ledger.Admit(f.Context, identity, "native/closed-" + index, 30, 180, stage: stage, controller: "Controller");
            f.Time = f.Time.AddSeconds(1);
            Assert.Equal(0, f.Ledger.Settle(f.Context, prior.Reference!, "native-finished", true).ExitCode);
            CloseCompactionAttempt(f, identity);
        }
        var root = f.Ledger.Admit(f.Context, identity, "native/unresolved-root", 60, 180, stage: stage, controller: "Controller");
        var child = f.Ledger.Admit(f.Context, identity, "native/unresolved-child", 30, 180,
            root.Reference!.ExecutionId, stage: stage, controller: "Controller");
        Assert.True(child.Admitted);
        for (var index = 0; index < 12; index++)
        {
            var work = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
            Assert.Equal(0, f.Ledger.Progress(f.Context, identity, new(new("Open result " + index,
                ["fixture:distinct-open-" + index], "Retain observed work.", "Continue the current attempt.", "Inspect its result."), work.Revision),
                "Controller").ExitCode);
        }
        InflateCompactionFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;

        var nested = f.Ledger.Admit(f.Context, identity, "native/ordinary-child", 10, 180,
            root.Reference.ExecutionId, stage: stage, controller: "Controller", nativeChildContinuation: true);

        Assert.True(nested.Admitted, string.Join("; ", nested.Result.Errors) + nested.Result.Work?.HoldReason);
        var after = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(before.LogicalAttemptId, after.LogicalAttemptId);
        Assert.Equal(before.Task!.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts, after.Task.UsedAttempts);
        Assert.Equal(before.Task.ReservedMilliseconds, after.Task.ReservedMilliseconds);
        Assert.NotNull(f.Ledger.NativeExecution(f.Context, root.Reference.ExecutionId));
        var unresolved = f.Ledger.NativeExecution(f.Context, child.Reference!.ExecutionId)!.Value;
        Assert.False(unresolved.Execution.Finalized);
        Assert.Equal(root.Reference.ExecutionId, unresolved.Reference.ParentExecutionId);
        Assert.Equal(12, after.Checkpoints.Count(checkpoint => checkpoint.Checkpoint.Outcome.StartsWith("Open result ", StringComparison.Ordinal)));
    }

    [Fact]
    public void HistoryCompaction_PreservesAnOldFinalizedNativeRestorationObligation()
    {
        using var f = new Fixture();
        var identity = f.Identity("WORK-A");
        AllocateCompactionFixture(f, identity);
        CisAgentWorkReference? original = null;
        for (var index = 0; index < 8; index++)
        {
            var run = f.Ledger.Admit(f.Context, identity, "native/restoration-history-" + index, 30, 180,
                stage: CompactionStage(f), controller: "Controller");
            Assert.True(run.Admitted);
            original ??= run.Reference;
            f.Time = f.Time.AddSeconds(1);
            Assert.Equal(0, f.Ledger.Settle(f.Context, run.Reference!, "native-finished", true).ExitCode);
            CloseCompactionAttempt(f, identity);
        }
        var path = Path.Combine(f.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var state = JsonNode.Parse(File.ReadAllText(path))!;
        var execution = state["changes"]![identity.ChangeId]!["executions"]![original!.ExecutionId]!;
        // Reproduce retained legacy accounting: its old charge finalized, while
        // the original build still has an explicit unresolved restoration recipe.
        execution["nativeRestorationRequired"] = true;
        execution["stage"]!["nativeRecoveryBasis"] = "fixture:unchanged-original-recovery-recipe";
        File.WriteAllText(path, state.ToJsonString());
        InflateCompactionFixture(f, identity);
        var before = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Contains("restor", before.HoldReason!, StringComparison.OrdinalIgnoreCase);
        var proof = f.Ledger.NativeExecution(f.Context, original.ExecutionId)!.Value;

        var progress = f.Ledger.Progress(f.Context, identity, new(new("Retain the unresolved native obligation.",
            ["fixture:retained-restoration-obligation"], "Keep the original recovery recipe.", "Do not treat accounting settlement as recovery.",
            "Complete the separately authorized restoration."), before.Revision), "Controller");

        Assert.Equal(0, progress.ExitCode);
        var retained = f.Ledger.NativeExecution(f.Context, original.ExecutionId)!.Value;
        Assert.Equal(proof.Reference, retained.Reference);
        Assert.Equal(proof.Execution, retained.Execution);
        Assert.True(retained.Execution.Finalized);
        Assert.True(retained.Execution.NativeRestorationRequired);
        var after = f.Ledger.Read(f.Context, identity.ChangeId, identity.TaskId).Work!;
        Assert.Equal(before.HoldReason, after.HoldReason);
        Assert.Equal(before.Task!.ConsumedMilliseconds, after.Task!.ConsumedMilliseconds);
        Assert.Equal(before.Task.UsedAttempts, after.Task.UsedAttempts);
        Assert.Null(after.LogicalAttemptId);
        Assert.True(new FileInfo(path).Length < 1024 * 1024);
    }

    private static void AllocateCompactionFixture(Fixture fixture, CisAgentWorkIdentity identity)
    {
        var allocation = new CisAgentWorkAllocationRequest(3600, 100, 0, [new(identity.TaskId, 3600, 100)],
            "approved finite fixture allocation", "owned fixture storage", AcknowledgeHistoricalUnknown: true)
        { Controllers = ["Controller"], Storage = new(64 * 1024 * 1024, [new(fixture.Context.RepositoryPath, ".cis/local")]) };
        Assert.Equal(0, fixture.Ledger.Allocate(fixture.Context, identity.ChangeId, allocation, [identity],
            "Controller", "Qualify bounded settled-history compaction.").ExitCode);
    }

    private static CisAgentWorkStage CompactionStage(Fixture fixture)
        => new("native-testing", "fixture-profile", "unit", fixture.Context.RepositoryId) { NativeOperation = "unit" };

    private static void CloseCompactionAttempt(Fixture fixture, CisAgentWorkIdentity identity)
    {
        var before = fixture.Ledger.Read(fixture.Context, identity.ChangeId, identity.TaskId).Work!;
        var checkpoint = new CisAgentWorkCheckpoint("The scoped fixture result settled.", ["fixture:observed-result"],
            "Use the same accepted scope.", "Close this measured logical attempt.", "Select genuinely pending work.")
        { CloseAttempt = true, MaterialNewInformation = true };
        Assert.Equal(0, fixture.Ledger.Progress(fixture.Context, identity, new(checkpoint, before.Revision), "Controller").ExitCode);
    }

    private static void InflateCompactionFixture(Fixture fixture, CisAgentWorkIdentity identity)
    {
        var path = Path.Combine(fixture.Context.RepositoryPath, CisAgentWorkPaths.Ledger);
        var state = JsonNode.Parse(File.ReadAllText(path))!;
        var owner = state["changes"]![identity.ChangeId]!["owners"]![identity.TaskId]!;
        var checkpoint = owner["checkpoints"]![0]!.DeepClone();
        checkpoint["checkpoint"]!["evidence"] = new JsonArray(Enumerable.Range(0, 40)
            .Select(_ => (JsonNode?)JsonValue.Create(new string('e', 4000))).ToArray());
        var checkpoints = owner["checkpoints"]!.AsArray();
        for (var index = 0; index < 40; index++) checkpoints.Add(checkpoint.DeepClone());
        File.WriteAllText(path, state.ToJsonString());
        Assert.InRange(new FileInfo(path).Length, 6 * 1024 * 1024, 8 * 1024 * 1024 - 65536);
    }
}
