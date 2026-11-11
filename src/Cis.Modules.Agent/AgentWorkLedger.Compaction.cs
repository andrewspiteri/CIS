using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private const int LedgerCompactionBytes = 4 * 1024 * 1024;
    private const int RecentSettledTreesPerOwner = 4;
    private const int RecentClosedCheckpointsPerOwner = 8;

    private void CompactLedgerHistory(Ledger state)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(state, LedgerJson).Length < LedgerCompactionBytes) return;
        foreach (var change in state.Changes.Values)
        {
            CompactStorageState(state, change);
            if (change.Storage is { } retainedStorage)
                PruneMissingStorageClaims(change, retainedStorage.Claims.Values.Where(claim => claim.NativeScratchAdmission is null
                    || change.Executions.GetValueOrDefault(claim.NativeScratchAdmission.Reference.ExecutionId) is { Finalized: true } execution
                        && change.Owners[execution.Reference.OwnerTaskId].OpenAttempt != execution.Reference.LogicalAttemptId).Select(claim => claim.Path).ToArray());
            foreach (var owner in change.Owners.Values) CompactCheckpoints(state, change, owner);
            var roots = new HashSet<string>(StringComparer.Ordinal);
            void Keep(string? id)
            {
                if (id is not null && change.Executions.TryGetValue(id, out var execution)) roots.Add(execution.RootId);
            }
            foreach (var execution in change.Executions.Values.Where(execution => !execution.Finalized
                || execution.NativeRestorationRequired || execution.RecoveryReason is not null || execution.NativeCancellationRequested
                || execution.Stage is { Route: "story-task" } or { CorrectionRoundId: not null }
                || change.Owners[execution.Reference.OwnerTaskId].OpenAttempt == execution.Reference.LogicalAttemptId)) Keep(execution.Reference.ExecutionId);
            foreach (var group in change.Executions.Values.Where(execution => execution.Reference.ExecutionId == execution.RootId)
                .GroupBy(execution => execution.Reference.OwnerTaskId))
                foreach (var execution in group.OrderByDescending(execution => execution.Completed ?? execution.Started)
                    .ThenByDescending(execution => execution.Started).Take(RecentSettledTreesPerOwner)) Keep(execution.Reference.ExecutionId);
            if (change.Storage is { } storage)
            {
                foreach (var claim in storage.Claims.Values)
                {
                    Keep(claim.RequiredByExecutionId); Keep(claim.SupersededByExecutionId);
                    Keep(claim.NativeScratchAdmission?.Reference.ExecutionId);
                }
                foreach (var reservation in storage.Reservations.Values) Keep(reservation.Reference.ExecutionId);
            }
            foreach (var relation in state.Corrections.Values)
            {
                Keep(relation.ActiveRootExecutionId);
                foreach (var round in relation.Rounds)
                    foreach (var id in round.ExecutionIds) Keep(id);
            }
            var retainedEvidence = change.Owners.Values.SelectMany(owner => owner.Outcomes
                .Concat(owner.Checkpoints.SelectMany(checkpoint => checkpoint.Checkpoint.Evidence))).ToArray();
            foreach (var execution in change.Executions.Values)
                if (retainedEvidence.Any(evidence => evidence.Contains(execution.Reference.ExecutionId, StringComparison.Ordinal))) Keep(execution.Reference.ExecutionId);
            var count = -1;
            while (count != roots.Count)
            {
                count = roots.Count;
                foreach (var execution in change.Executions.Values.Where(execution => roots.Contains(execution.RootId)).ToArray())
                {
                    Keep(execution.Reference.ParentExecutionId); Keep(execution.RestorationOfExecutionId);
                }
            }
            foreach (var pair in change.Executions.Where(pair => !roots.Contains(pair.Value.RootId)).ToArray())
            {
                // Open attempts and every unresolved tree were pinned above. Their
                // authoritative counters and closure state are never reconstructed.
                change.ConservativeHistory |= pair.Value.ConservativeRecovery;
                change.CompactedExecutionCount = checked(change.CompactedExecutionCount + 1);
                change.Executions.Remove(pair.Key);
            }
        }
    }

    private void CompactCheckpoints(Ledger state, Change change, Owner owner)
    {
        var originalCount = owner.Checkpoints.Count;
        var checkpoints = owner.Checkpoints.DistinctBy(record => JsonSerializer.Serialize(record, LedgerJson), StringComparer.Ordinal).ToArray();
        var keep = Enumerable.Range(Math.Max(0, checkpoints.Length - RecentClosedCheckpointsPerOwner),
            Math.Min(checkpoints.Length, RecentClosedCheckpointsPerOwner)).ToHashSet();
        var measured = MeasuredOwnerMilliseconds(change, owner);
        var reflection = checkpoints.Select((record, index) => (record, index)).Where(item => QualifyingReflection(change, item.record, measured))
            .OrderByDescending(item => item.record.ActiveMilliseconds).FirstOrDefault();
        if (reflection.record is not null) keep.Add(reflection.index);
        for (var index = 0; index < checkpoints.Length; index++)
        {
            var checkpoint = checkpoints[index];
            if (checkpoint.LogicalAttemptId == owner.OpenAttempt || checkpoint.Origin == "controller-disposition"
                || checkpoint.StoryRelationshipId is { } relationship && state.Corrections.ContainsKey(relationship)) keep.Add(index);
        }
        owner.Checkpoints = checkpoints.Where((_, index) => keep.Contains(index)).ToList();
        owner.CompactedCheckpointCount = checked(owner.CompactedCheckpointCount + originalCount - owner.Checkpoints.Count);
    }
}
