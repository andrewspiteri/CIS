using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    internal CisAgentWorkResult AbandonNativeUnit(CisRepositoryContext authority, CisAgentWorkReference reference,
        CisAgentWorkDispositionRequest request, string actor, string reason, IReadOnlyList<CisAgentWorkExecution> inspected)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("The original work allocation is missing.");
            CheckRevision(state, request.ExpectedRevision);
            var change = ChangeFor(state, reference.ChangeId);
            Authorize(change, actor); Text(reason, "Unit disposal reason");
            if (request.Kind != "abandon-native-unit" || request.ExecutionId != reference.ExecutionId
                || request.DisposalBasis is not ("process-cleanup" or "retired-environment")
                || request.Evidence is not { Count: > 0 and <= 40 } || request.Evidence.Any(item => !BoundedText(item)))
                throw new InvalidDataException("Unit disposal needs its exact invocation, current revision, actual cleanup/retirement basis and bounded factual evidence.");
            if (request.Hypothesis is not null || request.NextAction is not null || request.CorrectionRelationshipId is not null
                || request.CorrectionMaximumSeconds is not null || request.CorrectionFindingReferences is not { Count: 0 }
                || request.HistoricalRunIds is not { Count: 0 })
                throw new InvalidDataException("Unit disposal does not grant a correction, new scope or extra allowance.");
            if (!change.Executions.TryGetValue(reference.ExecutionId, out var root) || root.Reference != reference
                || reference.ParentExecutionId is not null || root.RootId != reference.ExecutionId)
                throw new InvalidDataException("Dispose of the exact original unit root, not a partial child.");
            var owner = change.Owners[reference.OwnerTaskId];
            var tree = NativeSubtree(change, reference.ExecutionId).ToArray();
            var explicitUnit = root.Stage?.NativeOperation is "incremental-unit" or "unit-verification" or "unit-native-driver";
            if (tree.Length is < 1 or > 4096 || inspected.Count != tree.Length
                || change.Executions.Values.Count(item => item.RootId == reference.ExecutionId) != tree.Length
                || tree.Any(item => item.RootId != reference.ExecutionId || item.RestorationOfExecutionId is not null
                    || item.Reference.OwnerTaskId != owner.Id || item.Reference.TaskId != reference.TaskId
                    || item.Reference.LogicalAttemptId != reference.LogicalAttemptId
                    || !explicitUnit && (item.NativeRestorationRequired || item.Stage?.NativeRecoveryBasis is not null)
                    || item.Stage?.NativeOperation is not ("incremental-unit" or "incremental-all" or "native-unit" or "unit-verification" or "unit-native-driver")))
                throw new InvalidDataException("Controlled disposal applies only to the complete disposable unit tree. Mutation and restoration retain their separate recovery requirements.");
            if (inspected.Select(item => item.ExecutionId).Distinct(StringComparer.Ordinal).Count() != tree.Length)
                throw new InvalidDataException("The inspected unit tree is incomplete or ambiguous.");
            foreach (var item in tree)
            {
                var saved = inspected.SingleOrDefault(value => value.ExecutionId == item.Reference.ExecutionId);
                if (saved is null || saved.TaskId != item.Reference.TaskId || saved.OwnerTaskId != item.Reference.OwnerTaskId
                    || saved.LogicalAttemptId != item.Reference.LogicalAttemptId || saved.RootExecutionId != item.RootId
                    || saved.ParentExecutionId != item.Reference.ParentExecutionId || saved.Stage != item.Stage
                    || saved.PreparationOwner != item.PreparationOwner || saved.ProcessId != item.ProcessId
                    || saved.ProcessStartedAtUtc != item.ProcessStartedAtUtc || saved.LaunchIntent != item.LaunchIntent
                    || saved.NativeContainment != item.NativeContainment || saved.Finalized != item.Finalized)
                    throw new InvalidDataException("The interrupted invocation changed during inspection; read its current revision.");
            }
            if (tree.All(item => item.Finalized))
                return (state, Result(state, change, reference.TaskId, "unchanged", false));
            var disposition = new CisNativeWorkAbandonment(request.DisposalBasis, actor.Trim(), reason, Time(Tick()), request.Evidence);
            foreach (var item in tree.Where(item => !item.Finalized))
            {
                // Old explicit unit admissions could inherit a configured mutation
                // recipe even though this complete tree never instrumented source.
                item.NativeRestorationRequired = false;
                // A completed child can already own a valid report while its
                // containing invocation waits for another interrupted child.
                // Disposal must keep that child's native provenance reusable.
                if (item.Stopped && item.RecoveryReason is null && item != root)
                {
                    if (item.ObservedStopped is null && !item.ConservativeRecovery) item.ObservedStopped = item.Completed;
                    continue;
                }
                if (item.Stopped && item.ObservedStopped is null && !item.ConservativeRecovery) item.ObservedStopped = item.Completed;
                item.NativeAbandonment = disposition;
                item.RecoveryReason = null;
                item.NativeCancellationRequested = false;
                item.ConservativeRecovery = true;
            }
            root.TerminalStatus = "Interrupted";
            if (root.Stopped) root.Status = "Interrupted";
            foreach (var item in tree.OrderByDescending(Depth))
                if (!item.Stopped)
                    SettleCore(state, change, item, "Interrupted", true,
                        "Disposable unit invocation abandoned as inconclusive: " + reason, request.Evidence, conservativeRecovery: true);
            if (tree.Any(item => !item.Finalized || !item.Stopped || item.RecoveryReason is not null))
                throw new InvalidDataException("The old unit accounting did not finalize; keep its reservations.");
            // This releases accounting growth after controlled retirement. It does
            // not turn retirement into a historical OS stop observation or test pass.
            SettleReconciledNativeStorage(change, tree, verifiedStopped: true);
            if (owner.OpenAttempt == reference.LogicalAttemptId
                && !change.Executions.Values.Any(item => item.Reference.OwnerTaskId == owner.Id && !item.Finalized))
                owner.OpenAttempt = null;
            owner.Checkpoints.Add(new(reference.LogicalAttemptId, actor.Trim(), Time(Tick()),
                new("Interrupted unit invocation disposed as inconclusive", request.Evidence,
                    "Retry only unfinished unit work from the saved checkpoint after cleanup or environment retirement.",
                    "Keep the original charge and attempt; unknown historical facts remain unknown.",
                    "Inspect remaining capacity and run ordinary bounded unit retry.")
                { CloseAttempt = true },
                owner.Consumed, Reserved(change, owner.Id))
            { Origin = "native-abandonment" });
            return (state, Result(state, change, reference.TaskId, "abandoned", true));

            int Depth(Execution item)
            {
                var depth = 0;
                while (item.Reference.ParentExecutionId is { } parent && change.Executions.TryGetValue(parent, out var ancestor))
                { if (++depth > 128) throw new InvalidDataException("Unit ancestry exceeds its supported bound."); item = ancestor; }
                return depth;
            }
        });
}
