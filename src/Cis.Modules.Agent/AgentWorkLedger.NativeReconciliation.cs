using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private CisAgentWorkResult ReconcileNativeCore(Ledger state, Change change, Owner owner, Execution root,
        CisAgentWorkDispositionRequest request, string actor, string reason, IReadOnlyList<CisAgentWorkExecution>? verified)
    {
        var tree = NativeSubtree(change, root.Reference.ExecutionId).ToArray();
        var previouslyFinalized = tree.All(item => item.Finalized);
        if (verified is null || verified.Count != tree.Length || verified.Count is < 1 or > 4096
            || verified.Select(item => item.ExecutionId).Distinct(StringComparer.Ordinal).Count() != verified.Count
            || request.Evidence is not { Count: > 0 and <= 40 } || request.Evidence.Any(evidence => !BoundedText(evidence)))
            return Result(state, change, root.Reference.TaskId, "held", false,
                "Reconcile native work through its exact retained tree, stopped-controller/OS-containment verification and bounded controller evidence.");
        var snapshot = verified.ToDictionary(item => item.ExecutionId, StringComparer.Ordinal);
        if (tree.Any(item => !snapshot.TryGetValue(item.Reference.ExecutionId, out var saved)
            || saved.TaskId != item.Reference.TaskId || saved.OwnerTaskId != item.Reference.OwnerTaskId
            || saved.LogicalAttemptId != item.Reference.LogicalAttemptId || saved.ParentExecutionId != item.Reference.ParentExecutionId
            || saved.RootExecutionId != item.RootId || saved.Stage != item.Stage || saved.PreparationOwner != item.PreparationOwner
            || saved.LaunchIntent != item.LaunchIntent || saved.ProcessId != item.ProcessId || saved.ProcessStartedAtUtc != item.ProcessStartedAtUtc
            || saved.NativeContainment != item.NativeContainment || saved.Finalized != item.Finalized
            || saved.ObservedStoppedAtUtc != (item.ObservedStopped is { } observed ? Time(observed) : null)
            || saved.CompletedAtUtc != (item.Completed is { } completed ? Time(completed) : null)
            || saved.NativeRestorationRequired != item.NativeRestorationRequired
            || item.Stage?.NativeOperation is null))
            throw new InvalidDataException("The retained native tree changed during stopped-process verification; read its current revision and retry.");
        if (tree.Any(item => !item.Finalized && Tick() - item.Started < 30_000))
            return Result(state, change, root.Reference.TaskId, "held", false,
                "The retained native execution is inside its 30-second reconciliation grace; this delay does not itself prove stopped children.");
        foreach (var execution in tree.Where(item => item.Stopped && !item.Finalized))
        {
            // Older ordinary settlements already retained a real controller stop.
            // Do not reinterpret it as the later reconciliation time or discard an overrun.
            if (execution.ObservedStopped is null && !execution.ConservativeRecovery)
                execution.ObservedStopped = execution.Completed;
            execution.RecoveryReason = null;
            execution.NativeCancellationRequested = false;
            execution.ConservativeRecovery = true;
        }
        foreach (var execution in tree.OrderByDescending(Depth))
            if (!execution.Stopped)
                SettleCore(state, change, execution, execution.NativeRestorationRequired ? "RecoveryRequired" : "Interrupted", true,
                    reason, request.Evidence, conservativeRecovery: true);
        if (tree.Any(item => !item.Stopped || !item.Finalized || item.RecoveryReason is not null))
            return Result(state, change, root.Reference.TaskId, "held", false,
                "Native work or its owned children still require stopped-process settlement before releasing storage reservations.");
        SettleReconciledNativeStorage(change, tree, verifiedStopped: true);
        owner.Checkpoints.Add(new(root.Reference.LogicalAttemptId, actor.Trim(), Time(Tick()),
            new("Native execution tree reconciled", request.Evidence, owner.Hypothesis ?? "Retain the original native scope and recovery recipe.",
                previouslyFinalized
                    ? "Time and attempts remain unchanged; pending storage was settled from the exact already-stopped native receipts and retained evidence stays protected."
                    : "Conservatively charged the original bound after exact stopped owners and empty owned job/session verification; native restoration remains separate.",
                tree.Any(item => item.NativeRestorationRequired)
                    ? "Inspect retained diagnostics and restore only through the original recipe before further native work."
                    : "Preserve current results and retry only unfinished or invalidated unit work within the remaining allowance."),
            owner.Consumed, Reserved(change, owner.Id))
        { Origin = "reconciliation" });
        return Result(state, change, root.Reference.TaskId, "reconciled", true);

        int Depth(Execution execution)
        {
            var depth = 0;
            while (execution.Reference.ParentExecutionId is { } parent && change.Executions.TryGetValue(parent, out var ancestor))
            {
                if (++depth > 128) throw new InvalidDataException("Native reconciliation ancestry exceeds its supported bound.");
                execution = ancestor;
            }
            return depth;
        }
    }

    private static bool PendingNativeStorage(Change change, string executionId)
    {
        var references = NativeSubtree(change, executionId).Select(item => item.Reference).ToHashSet();
        return change.Storage?.Reservations.Values.Any(item => !item.Finalized && references.Contains(item.Reference)) == true;
    }
}
