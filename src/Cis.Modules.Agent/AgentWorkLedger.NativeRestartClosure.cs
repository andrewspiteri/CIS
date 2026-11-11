using System.Security.Cryptography;
using System.Text.Json;
using Cis.Abstractions;

namespace Cis.Modules.Agent;

internal sealed partial class AgentWorkLedger
{
    internal CisAgentWorkResult QualifyNativeRestartClosure(CisRepositoryContext authority,
        CisAgentWorkIdentity identity, CisAgentWorkDispositionRequest request, string actor, string reason)
    {
        try
        {
            // Qualification reads the existing transaction snapshot. In particular,
            // it cannot create a challenge or retrofit provenance onto a launch.
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            var state = Load(authority) ?? throw new InvalidDataException("Restart closure qualification requires the retained accounting allocation.");
            CheckRevision(state, request.ExpectedRevision);
            ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId);
            Authorize(change, actor);
            Text(reason, "Restart closure qualification rationale");
            ValidateRestartClosureRequest(request);
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("This retained task has no accounting owner.");
            if (!change.Executions.TryGetValue(request.ExecutionId!, out var root)
                || root.Reference.TaskId != identity.TaskId || root.Reference.OwnerTaskId != owner.Id)
                throw new InvalidDataException("Restart closure qualification requires the exact execution belonging to this retained task and owner.");
            if (root.Reference.ParentExecutionId is not null || root.RootId != root.Reference.ExecutionId)
                throw new InvalidDataException("Restart closure qualification requires the full retained native root, not a partial child tree.");
            var tree = NativeSubtree(change, root.Reference.ExecutionId)
                .OrderBy(item => item.Reference.ExecutionId, StringComparer.Ordinal).ToArray();
            if (tree.Length is < 1 or > 4096
                || tree.Any(item => item.Stage?.NativeOperation is null || item.RootId != root.Reference.ExecutionId
                    || item.Reference.OwnerTaskId != owner.Id || item.Reference.TaskId != identity.TaskId
                    || item.Reference.LogicalAttemptId != root.Reference.LogicalAttemptId)
                || change.Executions.Values.Count(item => item.RootId == root.Reference.ExecutionId) != tree.Length)
                throw new InvalidDataException("Restart closure qualification requires a bounded, complete native tree with its original root, task, owner and logical attempt.");

            // This digest identifies only the current retained ledger snapshot. It
            // is neither authenticated original-host custody nor OS closure proof.
            var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                AuthorityPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(authority.RepositoryPath)),
                AuthorityId = state.AuthorityId,
                state.AdoptionId,
                Revision = Revision(state),
                RootExecutionId = root.Reference.ExecutionId,
                Executions = tree
            }, LedgerJson)));
            var gaps = new List<string>
            {
                "original-host-custody-unproven",
                "anti-copy-rollback-unqualified",
                "restart-verifier-unqualified"
            };
            if (request.Kind == "verify-restart-closure") gaps.Add("challenge-not-prepared");
            const string explanation = "Restart closure is not qualified: retained launch records do not establish original-host custody, copy/rollback exclusion or a qualified restart verifier. No challenge was prepared. A new machine binding, matching hostname/PID/session, wall-clock change, caller evidence or restart cannot supply missing historical provenance. Preserve this execution and all reservations.";
            return Result(state, change, identity.TaskId, "held", false, explanation) with
            {
                Errors = [explanation],
                NativeRestartClosure = new(request.Kind, "not-qualified", root.Reference.ExecutionId,
                    root.RootId, Revision(state), state.AdoptionId, digest, tree.Length, gaps)
            };
        }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    private static void ValidateRestartClosureRequest(CisAgentWorkDispositionRequest request)
    {
        if (request.Kind is not ("prepare-restart-closure" or "verify-restart-closure"))
            throw new InvalidDataException("Restart closure qualification requires prepare-restart-closure or verify-restart-closure.");
        Text(request.ExecutionId, "Exact retained native root execution ID");
        if (request.Hypothesis is not null || request.NextAction is not null || request.CorrectionRelationshipId is not null
            || request.CorrectionMaximumSeconds is not null || request.CorrectionFindingReferences is not { Count: 0 }
            || request.HistoricalRunIds is not { Count: 0 }
            || request.Evidence is not { Count: <= 40 } || request.Evidence.Any(item => !BoundedText(item)))
            throw new InvalidDataException("Restart closure qualification accepts only its operation, current revision, exact native root and bounded contextual evidence; it grants no continuation or correction disposition.");
    }
}
