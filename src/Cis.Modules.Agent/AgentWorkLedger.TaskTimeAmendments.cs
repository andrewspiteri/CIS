using System.Globalization;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    private const int MaximumTaskTimeAmendments = 10000;

    internal CisAgentWorkResult AmendTaskTime(CisRepositoryContext authority, CisAgentWorkIdentity identity,
        CisAgentWorkTaskTimeAmendmentRequest request, string actor, string reason)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("A task-time amendment requires its existing owning allocation and history.");
            ValidateIdentity(authority, identity.ChangeId, identity);
            CheckRevision(state, request.ExpectedRevision);
            var change = ChangeFor(state, identity.ChangeId);
            Authorize(change, actor);
            if (!string.Equals(actor.Trim(), change.Actor, StringComparison.Ordinal))
                throw new InvalidDataException("Only the original allocation authority may amend an existing task time ceiling; delegated controller access is insufficient.");
            Text(reason, "Task-time amendment reason");
            Text(request.DecisionReference, "New task-time amendment decision reference");
            Text(change.DecisionReference, "Original allocation decision reference");
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("The canonical task has no existing accounting owner to amend.");
            if (owner.Id != identity.TaskId || change.Bindings.GetValueOrDefault(identity.TaskId) != owner.Id)
                throw new InvalidDataException("Amend the exact existing canonical accounting owner, not an inheriting descendant or another task.");
            var milliseconds = Milliseconds(request.Seconds);
            if (milliseconds <= owner.Milliseconds)
                throw new InvalidDataException("The absolute task-time ceiling must increase its current value; replay, no-op and reductions grant no capacity.");
            if (milliseconds < checked(owner.Consumed + Reserved(change, owner.Id) + ProtectedRestoration(owner)))
                throw new InvalidDataException("The amended task ceiling must retain all consumed work, pending reservations and protected restoration.");
            var decision = request.DecisionReference.Trim();
            if (decision == change.DecisionReference.Trim() || change.TaskTimeAmendments.Any(item => item.OwnerTaskId == owner.Id
                && item.DecisionReference == decision))
                throw new InvalidDataException("Use the actual distinct new task-time decision reference; an original or already-recorded decision cannot authorize another increase.");
            if (change.TaskTimeAmendments.Count >= MaximumTaskTimeAmendments)
                throw new InvalidDataException("The existing bounded task-time amendment history is full; retain it before another amendment.");
            var previous = owner.Milliseconds;
            var previousRevision = Revision(state);
            var nextRevision = state.AdoptionId + ":" + checked(state.Revision + 1).ToString(CultureInfo.InvariantCulture);
            // This explicit decision may raise the former task allocation pool.
            // The containing ceiling and admission's task/change minimum stay unchanged.
            owner.Milliseconds = milliseconds;
            change.TaskTimeAmendments.Add(new(Guid.NewGuid().ToString("N"), state.AuthorityId, change.Id,
                identity.TaskId, owner.Id, previous, milliseconds, actor.Trim(), reason.Trim(), decision, Time(Tick()),
                previousRevision, nextRevision));
            return (state, Result(state, change, identity.TaskId, "task-time-amended", true));
        });

    private static void ValidateTaskTimeAmendments(Ledger state, Change change)
    {
        if (change.TaskTimeAmendments is null || change.TaskTimeAmendments.Count > MaximumTaskTimeAmendments
            || change.TaskTimeAmendments.Select(item => item?.Reference).Distinct(StringComparer.Ordinal).Count() != change.TaskTimeAmendments.Count)
            throw new InvalidDataException("Task-time amendment history is incomplete or exceeds its retained bound.");
        if (change.TaskTimeAmendments.Count > 0 && !BoundedText(change.DecisionReference))
            throw new InvalidDataException("Task-time amendments must retain their original allocation decision.");
        var previous = 0L;
        var decisions = new HashSet<(string Owner, string Decision)>();
        foreach (var item in change.TaskTimeAmendments)
        {
            if (item is null || !Guid.TryParseExact(item.Reference, "N", out _) || item.AuthorityRepositoryId != state.AuthorityId
                || item.ChangeId != change.Id || item.TaskId != item.OwnerTaskId || !change.Owners.ContainsKey(item.OwnerTaskId)
                || change.Bindings.GetValueOrDefault(item.TaskId) != item.OwnerTaskId
                || item.Actor != change.Actor || !BoundedText(item.Reason) || !BoundedText(item.DecisionReference)
                || item.DecisionReference == change.DecisionReference.Trim() || !decisions.Add((item.OwnerTaskId, item.DecisionReference))
                || !DateTimeOffset.TryParse(item.RecordedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || item.PreviousAllocatedMilliseconds < 0 || item.AllocatedMilliseconds <= item.PreviousAllocatedMilliseconds
                || item.PreviousAllocatedMilliseconds % 1000 != 0 || item.AllocatedMilliseconds > 31_536_000_000L
                || item.AllocatedMilliseconds % 1000 != 0)
                throw new InvalidDataException("A retained task-time amendment has lost its original authority, target or finite decision.");
            var before = AmendmentRevision(state, item.PreviousRevision);
            var after = AmendmentRevision(state, item.Revision);
            if (before < previous || after != checked(before + 1) || after > state.Revision)
                throw new InvalidDataException("Task-time amendment revisions are incomplete or out of order; retain the original history.");
            previous = after;
        }
    }

    private static long AmendmentRevision(Ledger state, string value)
    {
        Text(value, "Retained task-time amendment revision");
        var prefix = state.AdoptionId + ":";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || !long.TryParse(value.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw new InvalidDataException("A task-time amendment revision does not belong to its retained accounting adoption.");
        return revision;
    }
}
