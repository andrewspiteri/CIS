using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private static bool NativeLedgerMaintenance(CisAgentWorkStage stage, string? restorationOfExecutionId)
        => restorationOfExecutionId is not null || stage.Name == "native-restoration"
            || stage.NativeOperation == "artifacts-clean"
            || stage.NativeRecoveryBasis is not null && stage.NativeOperation is ("native-restoration" or "pilot-restoration"
                or "pilot-postflight" or "pilot-preflight-restoration" or "pilot-preflight-postflight");
    private static long ProtectedRestoration(Change change) => Math.Max(0, change.Restoration - change.RestorationSpent);
    private static long ProtectedRestoration(Owner owner) => Math.Max(0, owner.Restoration - owner.RestorationSpent);
    private static long ReservedRestoration(Change change, string? ownerId)
        => change.Executions.Values.Where(execution => !execution.Finalized && execution.Reference.ExecutionId == execution.RootId
            && execution.RestorationOfExecutionId is not null && (ownerId is null || execution.Reference.OwnerTaskId == ownerId))
            .Sum(execution => execution.Bound);

    private static IEnumerable<Execution> NativeSubtree(Change change, string executionId)
        => change.Executions.Values.Where(execution =>
        {
            var current = execution;
            for (var depth = 0; depth < 128; depth++)
            {
                if (current.Reference.ExecutionId == executionId) return true;
                if (current.Reference.ParentExecutionId is not { } parent || !change.Executions.TryGetValue(parent, out var ancestor)) return false;
                current = ancestor;
            }
            throw new InvalidDataException("Native ancestry exceeds the supported accounting bound.");
        });

    private static string? NativeRestorationHold(Change change, Owner owner)
    {
        var pending = change.Executions.Values.Where(execution => execution.Reference.OwnerTaskId == owner.Id && execution.Stopped
            && execution.NativeRestorationRequired && execution.Stage?.NativeOperation is not null).ToArray();
        var required = pending.FirstOrDefault(execution => !pending.Any(ancestor => ancestor != execution
            && NativeSubtree(change, ancestor.Reference.ExecutionId).Contains(execution)));
        return required is null ? null : "Retained native work requires restoration. Resume the same profile with --recovery-of "
            + required.Reference.ExecutionId + " and its unchanged recovery recipe; a different profile or run ID does not clear this hold.";
    }

    private static bool NativeContinuationOwnsRestoration(Change change, Execution parent)
    {
        var subtree = NativeSubtree(change, parent.Reference.ExecutionId).ToHashSet();
        return change.Executions.Values.Where(execution => execution.Reference.OwnerTaskId == parent.Reference.OwnerTaskId
            && execution.Stopped && execution.NativeRestorationRequired).All(subtree.Contains);
    }

    private static string? NativeCancellationHold(Change change, Owner owner)
        => change.Executions.Values.Any(execution => !execution.Finalized && execution.NativeCancellationRequested
            && execution.Reference.OwnerTaskId == owner.Id)
            ? "Native cancellation is in progress; inspect the recorded processes and retained recovery state before another launch."
            : null;

    internal CisAgentWorkResult RequestNativeCancellation(CisRepositoryContext authority, CisAgentWorkReference reference,
        string expectedRevision, string actor, string reason)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            CheckRevision(state, expectedRevision); Authorize(change, actor); Text(reason, "Native cancellation reason");
            if (execution.Stage?.NativeOperation is null) throw new InvalidDataException("Use native cancellation only for a retained native execution.");
            if (execution.Finalized || execution.Stopped && NativeSubtree(change, reference.ExecutionId).All(item => item.Stopped && item.RecoveryReason is null))
                return Result(state, change, reference.TaskId, "unchanged", false);
            execution.NativeCancellationRequested = true;
            var owner = change.Owners[reference.OwnerTaskId];
            owner.Checkpoints.Add(new(reference.LogicalAttemptId, actor.Trim(), Time(Tick()),
                new("Native cancellation requested for " + reference.ExecutionId + ": " + reason,
                    ["native-execution:" + reference.ExecutionId], "Stop only verified recorded owned identities and retain unresolved recovery evidence.",
                    "Cancellation does not release uncertain reservations or waive native restoration.",
                    "Inspect the exact native execution and reconcile only after owned work is proven stopped."),
                MeasuredOwnerMilliseconds(change, owner), Reserved(change, owner.Id))
            { Origin = "native-cancellation" });
            return Result(state, change, reference.TaskId, "cancelling", true);
        });

    internal IReadOnlyList<CisAgentWorkExecution> NativeExecutionTree(CisRepositoryContext authority, CisAgentWorkReference reference)
    {
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        var state = Load(authority) ?? throw new InvalidDataException("Native accounting is missing; preserve its reservation.");
        var change = ChangeFor(state, reference.ChangeId);
        if (!change.Executions.TryGetValue(reference.ExecutionId, out var execution) || execution.Reference != reference)
            throw new InvalidDataException("Native cancellation requires its exact retained work reference.");
        var ids = NativeSubtree(change, reference.ExecutionId).Select(item => item.Reference.ExecutionId).ToHashSet(StringComparer.Ordinal);
        return Result(state, change, reference.TaskId, "status", false).Work!.Executions.Where(item => ids.Contains(item.ExecutionId)).ToArray();
    }

    internal IReadOnlyList<(string ChangeId, string TaskId)> NativeOwners(CisRepositoryContext authority)
    {
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        var state = Load(authority);
        if (state is null || state.CorrectionOnly) return [];
        return state.Changes.Values.SelectMany(change => change.Owners.Values
            .Where(owner => owner.Milliseconds > 0).Select(owner => (change.Id, owner.Id))).ToArray();
    }

    internal (CisAgentWorkReference Reference, CisAgentWorkExecution Execution)? NativeExecution(
        CisRepositoryContext authority, string executionId)
    {
        using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
        var state = Load(authority);
        if (state is null || state.CorrectionOnly) return null;
        var matches = state.Changes.Values.Where(change => change.Executions.ContainsKey(executionId)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("The execution identity is ambiguous across owning changes.");
        if (matches.SingleOrDefault() is not { } change) return null;
        var execution = change.Executions[executionId];
        var view = Result(state, change, execution.Reference.TaskId, "status", false).Work!;
        return (execution.Reference, view.Executions.Single(item => item.ExecutionId == executionId));
    }
}
