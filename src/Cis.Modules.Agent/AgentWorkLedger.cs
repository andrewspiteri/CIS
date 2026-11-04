using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cis.Abstractions;

[assembly: InternalsVisibleTo("Cis.Modules.Agent.Tests")]

namespace Cis.Modules.Agent;

internal sealed record AgentWorkAdmission(CisAgentWorkResult Result, CisAgentWorkReference? Reference, int TimeoutSeconds)
{
    public bool Admitted => Result.Status == "admitted" && Reference is not null;
}

/// <summary>Direct-task accounting. All mutations are short authority-wide transactions.</summary>
internal sealed class AgentWorkLedger(Func<DateTimeOffset>? clock = null)
{
    internal const string RelativePath = CisAgentWorkPaths.Ledger;
    internal const string AdoptionPath = CisAgentWorkPaths.Adoption;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    private sealed class Ledger
    {
        public int SchemaVersion { get; set; } = 1;
        public string AuthorityId { get; set; } = "";
        public string AdoptionId { get; set; } = "";
        public long Revision { get; set; }
        public long BaselineStarted { get; set; }
        public Dictionary<string, Change> Changes { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed record Adoption(int SchemaVersion, string AuthorityId, string AdoptionId, string Actor, string Reason, string CreatedAtUtc);
    private sealed class Change
    {
        public string Id { get; set; } = "";
        public long Milliseconds { get; set; }
        public int Attempts { get; set; }
        public long Restoration { get; set; }
        public long Consumed { get; set; }
        public int UsedAttempts { get; set; }
        public string DecisionReference { get; set; } = "";
        public string StorageReference { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<string> Controllers { get; set; } = [];
        public Dictionary<string, Owner> Owners { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Execution> Executions { get; set; } = new(StringComparer.Ordinal);
        public List<AllocationAudit> AllocationHistory { get; set; } = [];
    }
    private sealed record AllocationAudit(string Actor, string Reason, string RecordedAtUtc,
        IReadOnlyList<CisAgentWorkTaskAllocation> Tasks);
    private sealed class Owner
    {
        public string Id { get; set; } = "";
        public long Milliseconds { get; set; }
        public int Attempts { get; set; }
        public long Restoration { get; set; }
        public bool Fixed { get; set; }
        public bool AllowRedistribution { get; set; }
        public long Consumed { get; set; }
        public int UsedAttempts { get; set; }
        public long? ReflectionWindowMilliseconds { get; set; }
        public string? OpenAttempt { get; set; }
        public string? Hypothesis { get; set; }
        public string? ExpectedResult { get; set; }
        public string? LastBlocker { get; set; }
        public int RepeatedBlockers { get; set; }
        public string? Hold { get; set; }
        public string? HoldKind { get; set; }
        public string NextAction { get; set; } = "Run the next bounded action within the allocation.";
        public List<string> Outcomes { get; set; } = [];
        public List<CisAgentWorkCheckpointRecord> Checkpoints { get; set; } = [];
    }
    private sealed class Execution
    {
        public CisAgentWorkReference Reference { get; set; } = new("", "", "", "", "", "", "", null);
        public string RootId { get; set; } = "";
        public long Started { get; set; }
        public long? Completed { get; set; }
        public long Bound { get; set; }
        public string Status { get; set; } = "reserved";
        public string? TerminalStatus { get; set; }
        public bool LaunchIntent { get; set; }
        public bool Stopped { get; set; }
        public bool Finalized { get; set; }
        public bool ConservativeRecovery { get; set; }
        public CisAgentWorkPreparationOwner? PreparationOwner { get; set; }
        public int? ProcessId { get; set; }
        public string? ProcessStartedAtUtc { get; set; }
        public string? RecoveryReason { get; set; }
        public string? Summary { get; set; }
        public List<string> Evidence { get; set; } = [];
        public List<Interval> Intervals { get; set; } = [];
    }
    private sealed record Interval(long Start, long? End);

    internal CisAgentWorkResult Read(CisRepositoryContext authority, string changeId, string? taskId = null, int? reflectionWindowSeconds = null)
    {
        try
        {
            // The immutable marker lets a missing-state status remain entirely
            // observational. Existing snapshots share the short transaction lock
            // so Windows replacement cannot race path/attribute inspection.
            if (!CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, RelativePath, out var path)
                || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, AdoptionPath, out var marker))
                throw new InvalidDataException("Work accounting paths must remain inside the authority.");
            if (!File.Exists(path) && !File.Exists(marker)) return Missing();
            using var held = AcquireTransactionLock(SafePath(authority, CisAgentWorkPaths.Lock));
            var state = Load(authority);
            if (state is null) return Missing();
            if (reflectionWindowSeconds is { } window)
            {
                if (window < 30) throw new InvalidDataException("The adopted reflection window must be at least 30 seconds.");
                if (state.Changes.TryGetValue(changeId, out var projectedChange))
                    foreach (var owner in projectedChange.Owners.Values) owner.ReflectionWindowMilliseconds = checked((long)window * 1000);
            }
            return state.Changes.TryGetValue(changeId, out var change) ? Result(state, change, taskId, "status", false)
                : Result(state, new() { Id = changeId }, null, "missing-allocation", false) with
                { Errors = ["This change has no owning allocation. Supply its allocation using the current accounting revision."] };
        }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    internal CisAgentWorkResult Allocate(CisRepositoryContext authority, string changeId, CisAgentWorkAllocationRequest request,
        IReadOnlyList<CisAgentWorkIdentity> tasks, string actor, string reason, bool hasHistoricalWorkReferences = false)
        => Mutate(authority, state =>
        {
            Text(actor, "Allocation actor"); Text(reason, "Allocation rationale"); Text(request.DecisionReference, "Allocation decision reference");
            Text(request.StorageCeilingReference, "Storage ceiling reference");
            if (request.Tasks is null || request.Tasks.Count is < 1 or > 1000 || request.Tasks.Select(item => item.TaskId).Distinct(StringComparer.Ordinal).Count() != request.Tasks.Count)
                throw new InvalidDataException("Supply one bounded allocation for each distinct canonical task owner.");
            if (state is null)
            {
                if (hasHistoricalWorkReferences) throw new InvalidDataException("Historical runs reference adopted work accounting. Restore its missing state; a fresh baseline cannot erase consumption.");
                if (!request.AcknowledgeHistoricalUnknown) throw new InvalidDataException("Initial allocation must explicitly acknowledge unmeasured historical effort.");
                if (request.ExpectedRevision is not null) throw new InvalidDataException("The first accounting baseline has no prior revision.");
                state = new() { AuthorityId = authority.RepositoryId, AdoptionId = Guid.NewGuid().ToString("N"), BaselineStarted = Tick() };
            }
            else CheckRevision(state, request.ExpectedRevision);
            var isNew = !state.Changes.TryGetValue(changeId, out var change);
            if (isNew)
            {
                change = new()
                {
                    Id = changeId,
                    Milliseconds = Milliseconds(request.ChangeSeconds),
                    Attempts = Attempts(request.ChangeAttempts),
                    Restoration = Milliseconds(request.RestorationSeconds),
                    DecisionReference = request.DecisionReference,
                    StorageReference = request.StorageCeilingReference,
                    Actor = actor.Trim(),
                    Reason = reason.Trim(),
                    Controllers = (request.Controllers ?? []).Append(actor.Trim()).Distinct(StringComparer.Ordinal).ToList()
                };
                if (change.Restoration > change.Milliseconds) throw new InvalidDataException("Change restoration capacity must fit its fixed allocation.");
                state.Changes[changeId] = change;
            }
            else
            {
                Authorize(change!, actor);
                if (Milliseconds(request.ChangeSeconds) != change!.Milliseconds || request.ChangeAttempts != change.Attempts
                    || Milliseconds(request.RestorationSeconds) != change.Restoration)
                    throw new InvalidDataException("Ordinary allocation cannot increase or reset the fixed containing-change allowance or its restoration reserve.");
                if (request.DecisionReference != change.DecisionReference || request.StorageCeilingReference != change.StorageReference)
                    throw new InvalidDataException("Retain the owning allocation and storage authority references; ordinary redistribution does not replace them.");
            }
            var canonical = tasks.ToDictionary(item => item.TaskId, StringComparer.Ordinal);
            var oldTotal = change!.Owners.Values.Sum(item => item.Milliseconds);
            var oldAttempts = change.Owners.Values.Sum(item => (long)item.Attempts);
            long addedMilliseconds = 0, addedAttempts = 0;
            foreach (var allocation in request.Tasks.OrderBy(item => canonical.GetValueOrDefault(item.TaskId)?.AncestorTaskIds.Count ?? int.MaxValue))
            {
                if (!canonical.TryGetValue(allocation.TaskId, out var identity)) throw new InvalidDataException("Allocation requires a canonical approved task identity: " + allocation.TaskId);
                ValidateIdentity(authority, changeId, identity);
                var inherited = FindOwner(change, identity);
                if (inherited is not null && inherited.Id != allocation.TaskId)
                    throw new InvalidDataException($"Task {allocation.TaskId} inherits accounting owner {inherited.Id}; decomposition cannot create fresh capacity.");
                var milliseconds = Milliseconds(allocation.Seconds); var attempts = Attempts(allocation.Attempts); var restoration = Milliseconds(allocation.RestorationSeconds);
                if (restoration > milliseconds) throw new InvalidDataException("Task restoration capacity must fit its allocation.");
                if (change.Owners.TryGetValue(allocation.TaskId, out var owner))
                {
                    if (restoration < owner.Restoration)
                        throw new InvalidDataException("Ordinary redistribution cannot reduce protected task restoration capacity. Retain the existing RestorationSeconds value, including when updating other task limits.");
                    if (owner.Fixed && (owner.Milliseconds != milliseconds || owner.Attempts != attempts || owner.Restoration != restoration))
                        throw new InvalidDataException("A separately fixed task limit cannot be overridden by redistribution.");
                    if ((owner.Milliseconds != milliseconds || owner.Attempts != attempts || owner.Restoration != restoration)
                        && !owner.AllowRedistribution) throw new InvalidDataException("This task does not delegate redistribution of its remaining allocation.");
                    if (owner.Fixed != allocation.Fixed || owner.AllowRedistribution != allocation.AllowRedistribution)
                        throw new InvalidDataException("Ordinary allocation cannot broaden task allocation authority.");
                    if (milliseconds < owner.Consumed + Reserved(change, owner.Id) + restoration || attempts < owner.UsedAttempts)
                        throw new InvalidDataException("Redistribution must preserve consumption, reservations and restoration capacity.");
                    owner.Milliseconds = milliseconds; owner.Attempts = attempts; owner.Restoration = restoration;
                }
                else
                {
                    if (!isNew)
                    {
                        if (actor.Trim() != change.Actor)
                            throw new InvalidDataException("Only the original allocation authority may assign previously unallocated outer capacity to a new canonical root task.");
                        addedMilliseconds = checked(addedMilliseconds + milliseconds); addedAttempts += attempts;
                        if (addedMilliseconds > Math.Max(0, change.Milliseconds - change.Restoration - oldTotal)
                            || addedAttempts > Math.Max(0, change.Attempts - oldAttempts))
                            throw new InvalidDataException("The new root allocation exceeds previously unallocated containing-change time or attempts; fixed limits and restoration capacity remain unchanged.");
                    }
                    owner = new()
                    {
                        Id = allocation.TaskId,
                        Milliseconds = milliseconds,
                        Attempts = attempts,
                        Restoration = restoration,
                        Fixed = allocation.Fixed,
                        AllowRedistribution = allocation.AllowRedistribution,
                        Outcomes = identity.AcceptedOutcomeReferences.ToList()
                    };
                    change.Owners[owner.Id] = owner;
                }
                change.Bindings[allocation.TaskId] = owner.Id;
            }
            if (!isNew && (change.Owners.Values.Sum(item => item.Milliseconds) > oldTotal + addedMilliseconds
                || change.Owners.Values.Sum(item => (long)item.Attempts) > oldAttempts + addedAttempts))
                throw new InvalidDataException("Delegated redistribution cannot increase the original task allocation pool.");
            change.AllocationHistory.Add(new(actor.Trim(), reason.Trim(), Time(Tick()), request.Tasks));
            return (state, Result(state, change, null, "allocated", true));
        });

    internal AgentWorkAdmission Admit(CisRepositoryContext authority, CisAgentWorkIdentity identity, string executionId,
        int requestedSeconds, int maximumRunSeconds, string? parentExecutionId = null, string? logicalAttemptId = null,
        CisAgentWorkPreparationOwner? preparationOwner = null)
    {
        CisAgentWorkReference? reference = null; var timeout = 0;
        var result = Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("Direct task execution requires an authorized owning work allocation; no unlimited baseline was substituted.");
            ValidateIdentity(authority, identity.ChangeId, identity); Text(executionId, "Execution identity");
            var change = ChangeFor(state, identity.ChangeId); var owner = FindOwner(change, identity) ?? throw new InvalidDataException("No task or allocated canonical ancestor owns this task's work allowance.");
            if (change.Executions.ContainsKey(executionId)) throw new InvalidDataException("This execution identity already has accounting state; inspect or reconcile it instead of reserving again.");
            if (requestedSeconds is < 30 || requestedSeconds > maximumRunSeconds || maximumRunSeconds < 30)
                throw new InvalidDataException($"The requested execution bound must be between 30 and the adopted {maximumRunSeconds}-second ceiling.");
            if (preparationOwner is not null) ValidatePreparationOwner(preparationOwner);
            owner.ReflectionWindowMilliseconds = checked((long)maximumRunSeconds * 1000);
            var hold = Hold(change, owner);
            Execution? parent = null;
            if (parentExecutionId is not null)
            {
                if (!change.Executions.TryGetValue(parentExecutionId, out parent) || parent.Stopped || parent.Reference.OwnerTaskId != owner.Id
                    || parent.Reference.TaskId != identity.TaskId && !identity.AncestorTaskIds.Contains(parent.Reference.TaskId, StringComparer.Ordinal))
                    throw new InvalidDataException("A child execution must inherit a live canonical parent and its unchanged accounting owner.");
                hold = change.Executions.Values.FirstOrDefault(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id && item.RecoveryReason is not null)?.RecoveryReason ?? owner.Hold;
            }
            if (hold is not null) return (state, Result(state, change, identity.TaskId, "held", false, hold));
            var milliseconds = checked((long)requestedSeconds * 1000);
            var available = parent is null ? Math.Min(owner.Milliseconds - owner.Consumed - Reserved(change, owner.Id) - owner.Restoration,
                change.Milliseconds - change.Consumed - Reserved(change, null) - change.Restoration)
                : Math.Max(0, parent.Started + parent.Bound - Tick());
            if (milliseconds > available)
            {
                var shorter = Math.Max(0, available / 1000);
                return (state, Result(state, change, identity.TaskId, "held", false,
                    shorter >= 30 ? $"Remaining capacity permits a shorter execution bound of at most {shorter} seconds."
                    : "Remaining hard allowance cannot support the minimum operation while preserving restoration capacity. Seek the applicable allocation authority."));
            }
            if (owner.OpenAttempt is null)
            {
                if (owner.UsedAttempts >= owner.Attempts || change.UsedAttempts >= change.Attempts)
                    return (state, Result(state, change, identity.TaskId, "held", false, "The logical-attempt allowance is exhausted. Seek the applicable allocation authority."));
                owner.OpenAttempt = owner.Id + ":" + ++owner.UsedAttempts; change.UsedAttempts++;
            }
            if (logicalAttemptId is not null && logicalAttemptId != owner.OpenAttempt) throw new InvalidDataException("Run identifiers cannot open or replace the current logical attempt. Close it through controller progress first.");
            reference = new(state.AdoptionId, authority.RepositoryId, change.Id, identity.TaskId, owner.Id, executionId, owner.OpenAttempt, parentExecutionId);
            var started = Tick();
            change.Executions[executionId] = new()
            {
                Reference = reference,
                RootId = parent?.RootId ?? executionId,
                Started = started,
                Bound = milliseconds,
                PreparationOwner = preparationOwner,
                Intervals = [new(started, null)]
            };
            change.Bindings[identity.TaskId] = owner.Id;
            owner.Outcomes = owner.Outcomes.Concat(identity.AcceptedOutcomeReferences).Distinct(StringComparer.Ordinal).ToList();
            timeout = requestedSeconds;
            return (state, Result(state, change, identity.TaskId, "admitted", true));
        });
        return new(result, result.Status == "admitted" ? reference : null, timeout);
    }

    internal CisAgentWorkResult MarkLaunchIntent(CisRepositoryContext authority, CisAgentWorkReference reference)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            if (execution.Stopped) throw new InvalidDataException("A stopped execution cannot launch again.");
            execution.LaunchIntent = true; execution.Status = "launch-intent";
            return Result(state, change, reference.TaskId, "launch-intent", true);
        });

    internal CisAgentWorkResult AttachProcess(CisRepositoryContext authority, CisAgentWorkReference reference, int processId, string startedAtUtc)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            if (execution.Stopped || !execution.LaunchIntent || processId <= 0 || !DateTimeOffset.TryParse(startedAtUtc, out _))
                throw new InvalidDataException("Attach only a verified owned process to a live launch intent.");
            if (execution.ProcessId is not null && (execution.ProcessId != processId || execution.ProcessStartedAtUtc != startedAtUtc))
                throw new InvalidDataException("An execution's owned process identity cannot be replaced.");
            execution.ProcessId = processId; execution.ProcessStartedAtUtc = startedAtUtc; execution.Status = "running";
            return Result(state, change, reference.TaskId, "running", true);
        });

    internal CisAgentWorkResult MarkRecoveryUncertain(CisRepositoryContext authority, CisAgentWorkReference reference, string reason)
        => ExecutionChange(authority, reference, (state, change, execution) =>
        {
            Text(reason, "Recovery reason");
            if (execution.Finalized) return Result(state, change, reference.TaskId, "unchanged", false);
            execution.RecoveryReason = reason; execution.Status = "uncertain";
            return Result(state, change, reference.TaskId, "held", true, reason);
        });

    internal CisAgentWorkResult Settle(CisRepositoryContext authority, CisAgentWorkReference reference, string status, bool ownedWorkStopped,
        string? summary = null, IReadOnlyList<string>? evidence = null, bool conservativeRecovery = false)
        => ExecutionChange(authority, reference, (state, change, execution) =>
            SettleCore(state, change, execution, status, ownedWorkStopped, summary, evidence, conservativeRecovery));

    private CisAgentWorkResult SettleCore(Ledger state, Change change, Execution execution, string status, bool ownedWorkStopped,
        string? summary, IReadOnlyList<string>? evidence, bool conservativeRecovery)
    {
        var reference = execution.Reference;
        if (execution.Finalized || execution.Stopped) return Result(state, change, reference.TaskId, "unchanged", false);
        if (!ownedWorkStopped)
        {
            execution.RecoveryReason = "Owned execution has not been proven stopped; its reservation remains held.";
            execution.Status = "uncertain";
            return Result(state, change, reference.TaskId, "held", true, execution.RecoveryReason);
        }
        var stopped = Tick(); if (stopped < execution.Started) throw new InvalidDataException("The accounting clock moved backwards; reconcile usage before releasing capacity.");
        execution.Stopped = true; execution.Completed = stopped; execution.Status = status; execution.TerminalStatus = status; execution.Summary = summary;
        execution.ConservativeRecovery = conservativeRecovery;
        execution.Evidence = evidence?.ToList() ?? []; execution.RecoveryReason = null;
        execution.Intervals[^1] = execution.Intervals[^1] with { End = stopped };
        var root = change.Executions[execution.RootId];
        var tree = change.Executions.Values.Where(item => item.RootId == root.RootId).ToArray();
        if (root.Stopped && tree.All(item => item.Stopped))
        {
            var charge = UnionMilliseconds(tree.SelectMany(item => item.Intervals).Select(interval => (interval.Start, interval.End!.Value)));
            if (tree.Any(item => item.ConservativeRecovery)) charge = Math.Max(charge, root.Bound);
            change.Consumed = checked(change.Consumed + charge);
            var owner = change.Owners[root.Reference.OwnerTaskId]; owner.Consumed = checked(owner.Consumed + charge);
            foreach (var item in tree) { item.Finalized = true; item.Status = item.TerminalStatus ?? item.Status; }
            var checkpoint = new CisAgentWorkCheckpoint(root.Status + ": " + (root.Summary ?? "Inspect retained result."), root.Evidence,
                owner.Hypothesis ?? "Execute the current canonical task within its allocation.",
                tree.Any(item => item.ConservativeRecovery) ? "Usage conservatively reconciled after stopped-process verification; the full reserved bound is charged."
                : "Inspect retained result and required evidence before the next bounded action.", owner.NextAction);
            owner.Checkpoints.Add(new(root.Reference.LogicalAttemptId, "CIS measured execution", Time(stopped), checkpoint, owner.Consumed, Reserved(change, owner.Id))
            { Origin = "execution-summary" });
        }
        else
        {
            if (root.Stopped) root.Status = "waiting-for-owned-children";
            var owner = change.Owners[root.Reference.OwnerTaskId];
            owner.Checkpoints.Add(new(reference.LogicalAttemptId, "CIS measured execution", Time(stopped),
                new(status + ": " + (summary ?? "Inspect retained result."), evidence ?? [], owner.Hypothesis ?? "Execute the current bounded task.",
                    "Owned execution time remains included in the parent reservation until every owned execution safely settles.", owner.NextAction),
                owner.Consumed, Reserved(change, owner.Id))
            { Origin = "execution-summary" });
        }
        return Result(state, change, reference.TaskId, "settled", true);
    }

    internal CisAgentWorkResult Progress(CisRepositoryContext authority, CisAgentWorkIdentity identity, CisAgentWorkProgressRequest request, string actor)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("An owning work allocation is required before progress can be recorded.");
            CheckRevision(state, request.ExpectedRevision); ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor);
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("This task has no accounting owner.");
            var checkpoint = request.Checkpoint ?? throw new InvalidDataException("Supply the bounded progress checkpoint.");
            Text(checkpoint.Outcome, "Outcome"); Text(checkpoint.Approach, "Approach"); Text(checkpoint.Decision, "Decision"); Text(checkpoint.NextAction, "Next action");
            if (checkpoint.Evidence is null || checkpoint.Evidence.Count > 40 || checkpoint.MaterialNewInformation && checkpoint.Evidence.Count == 0)
                throw new InvalidDataException("Material new information requires bounded evidence references.");
            if (checkpoint.CloseAttempt && (owner.OpenAttempt is null || change.Executions.Values.Any(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id)))
                throw new InvalidDataException("Close only an existing logical attempt whose owned executions have safely settled.");
            owner.Checkpoints.Add(new(owner.OpenAttempt ?? owner.Id + ":pending", actor.Trim(), Time(Tick()), checkpoint, MeasuredOwnerMilliseconds(change, owner), Reserved(change, owner.Id))
            { Origin = "controller-progress" });
            owner.Hypothesis = checkpoint.Hypothesis ?? owner.Hypothesis; owner.ExpectedResult = checkpoint.ExpectedResult ?? owner.ExpectedResult;
            owner.NextAction = checkpoint.NextAction;
            if (checkpoint.CloseAttempt)
            {
                owner.RepeatedBlockers = !checkpoint.MaterialNewInformation && checkpoint.BlockerKey is not null
                    ? owner.LastBlocker == checkpoint.BlockerKey ? owner.RepeatedBlockers + 1 : 1 : 0;
                owner.LastBlocker = checkpoint.BlockerKey; owner.OpenAttempt = null;
                if (owner.RepeatedBlockers >= 2 && (owner.Hold is null || owner.HoldKind == "repeated-blocker"))
                {
                    owner.Hold = "Two consecutive logical attempts reported the same blocker without material new information. Reconsider this approach before another unchanged expensive repetition.";
                    owner.HoldKind = "repeated-blocker";
                }
            }
            return (state, Result(state, change, identity.TaskId, "recorded", true));
        });

    internal CisAgentWorkResult Disposition(CisRepositoryContext authority, CisAgentWorkIdentity identity, CisAgentWorkDispositionRequest request, string actor, string reason,
        CisAgentWorkPreparationOwner? verifiedPreparationOwner = null, bool preparationStopped = false)
        => Mutate(authority, state =>
        {
            if (state is null) throw new InvalidDataException("An owning work allocation is required before disposition.");
            CheckRevision(state, request.ExpectedRevision); ValidateIdentity(authority, identity.ChangeId, identity);
            var change = ChangeFor(state, identity.ChangeId); Authorize(change, actor); Text(reason, "Disposition rationale");
            var owner = FindOwner(change, identity) ?? throw new InvalidDataException("This task has no accounting owner.");
            switch (request.Kind)
            {
                case "simplify":
                    if (owner.Hold is not null && owner.HoldKind != "repeated-blocker")
                        throw new InvalidDataException("A simpler approach does not clear this separate controller hold. Resolve its specific disposition first.");
                    Text(request.Hypothesis, "Justified replacement hypothesis");
                    if (request.Hypothesis == owner.Hypothesis || request.Evidence is null || request.Evidence.Count == 0)
                        throw new InvalidDataException("A simpler in-scope approach requires a changed hypothesis, supporting evidence and controller rationale; rewording alone is insufficient.");
                    owner.Hypothesis = request.Hypothesis; owner.Hold = null; owner.HoldKind = null; owner.RepeatedBlockers = 0;
                    break;
                case "hold": owner.Hold = reason; owner.HoldKind = "controller"; break;
                case "continue":
                    if (owner.HoldKind == "controller" && request.Evidence.Count > 0) { owner.Hold = null; owner.HoldKind = null; }
                    break;
                case "reconcile":
                    if (request.ExecutionId is null || !change.Executions.TryGetValue(request.ExecutionId, out var execution)
                        || execution.Reference.AuthorityRepositoryId != authority.RepositoryId || execution.Reference.ChangeId != identity.ChangeId
                        || execution.Reference.TaskId != identity.TaskId || execution.Reference.OwnerTaskId != owner.Id)
                        throw new InvalidDataException("Reconciliation requires the exact execution ID belonging to this retained task, owner and authority.");
                    if (execution.Finalized) return (state, Result(state, change, identity.TaskId, "unchanged", false));
                    if (execution.LaunchIntent || execution.ProcessId is not null)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "A launch intent or owned process exists. Use cis agent recover with verified stopped-process evidence; no-PID launched work remains uncertain."));
                    if (Tick() - execution.Started < 30_000)
                        return (state, Result(state, change, identity.TaskId, "held", false, "The never-launched execution is inside its 30-second recovery grace period."));
                    if (execution.PreparationOwner is null)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "Preparation ownership is unknown in this historical reservation. Preserve its capacity until stopped preparation can be established; an absent owner is not proof of abandonment."));
                    if (!preparationStopped || verifiedPreparationOwner != execution.PreparationOwner)
                        return (state, Result(state, change, identity.TaskId, "held", false,
                            "Preparation is live or its recorded owner has not been proven stopped. Retain the reservation and let the owning invocation finish, or reconcile after its exact owner is verified stopped."));
                    var reconciled = SettleCore(state, change, execution, "Interrupted", true, reason, request.Evidence, true);
                    owner.Checkpoints.Add(new(execution.Reference.LogicalAttemptId, actor.Trim(), Time(Tick()),
                        new("Never-launched reservation reconciled", request.Evidence, owner.Hypothesis ?? "Retain current scope.",
                            "reconcile: " + reason, "Inspect charged usage before another bounded action."), owner.Consumed, Reserved(change, owner.Id))
                    { Origin = "reconciliation" });
                    return (state, reconciled);
                default: throw new InvalidDataException("Disposition must be continue, simplify, hold or reconcile.");
            }
            owner.NextAction = request.NextAction ?? owner.NextAction;
            owner.Checkpoints.Add(new(owner.OpenAttempt ?? owner.Id + ":pending", actor.Trim(), Time(Tick()),
                new("Controller disposition", request.Evidence, owner.Hypothesis ?? "Retain current scope.", request.Kind + ": " + reason, owner.NextAction), owner.Consumed, Reserved(change, owner.Id))
            { Origin = "controller-disposition" });
            return (state, Result(state, change, identity.TaskId, "disposed", true));
        });

    private CisAgentWorkResult ExecutionChange(CisRepositoryContext authority, CisAgentWorkReference reference,
        Func<Ledger, Change, Execution, CisAgentWorkResult> action)
        => Mutate(authority, state =>
        {
            if (state is null || state.AdoptionId != reference.AdoptionId || reference.AuthorityRepositoryId != authority.RepositoryId)
                throw new InvalidDataException("The authoritative accounting reference is missing or changed; restore it before continuation.");
            var change = ChangeFor(state, reference.ChangeId);
            if (!change.Executions.TryGetValue(reference.ExecutionId, out var execution) || execution.Reference != reference)
                throw new InvalidDataException("This execution does not match its immutable work reference.");
            return (state, action(state, change, execution));
        });

    private CisAgentWorkResult Mutate(CisRepositoryContext authority, Func<Ledger?, (Ledger State, CisAgentWorkResult Result)> action)
    {
        try
        {
            var path = SafePath(authority, RelativePath); var marker = SafePath(authority, AdoptionPath);
            var lockPath = SafePath(authority, CisAgentWorkPaths.Lock);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var held = AcquireTransactionLock(lockPath);
            var current = Load(authority); var next = action(current);
            if (!next.Result.Applied) return next.Result;
            next.State.Revision++;
            var serialized = JsonSerializer.Serialize(next.State, Json);
            if (serialized.Length > 8 * 1024 * 1024) throw new InvalidDataException("The durable work ledger reached its 8 MiB bound; preserve accounting and resolve storage capacity before more work.");
            Atomic(path, serialized);
            if (!File.Exists(marker)) Atomic(marker, JsonSerializer.Serialize(new Adoption(1, authority.RepositoryId, next.State.AdoptionId,
                next.State.Changes.Values.First().Actor, next.State.Changes.Values.First().Reason, Time(Tick())), Json));
            return next.Result with { Work = next.Result.Work is null ? null : next.Result.Work with { Revision = Revision(next.State) } };
        }
        catch (IOException error) { return new("busy", null, ["Accounting is busy or unavailable; retry without starting a provider. " + error.Message], false); }
        catch (Exception error) when (Recoverable(error)) { return Failure(error); }
    }

    private static Ledger? Load(CisRepositoryContext authority)
    {
        var path = SafePath(authority, RelativePath); var marker = SafePath(authority, AdoptionPath);
        if (!File.Exists(path) && !File.Exists(marker)) return null;
        if (!File.Exists(path) || !File.Exists(marker)) throw new InvalidDataException("Adopted work accounting is incomplete. Restore its ledger and adoption reference; missing state cannot become a fresh allowance.");
        var adoption = JsonSerializer.Deserialize<Adoption>(ReadSnapshot(marker, 65536), Json);
        var state = JsonSerializer.Deserialize<Ledger>(ReadSnapshot(path, 8 * 1024 * 1024), Json);
        if (state is not { SchemaVersion: 1, Changes: not null } || adoption is not { SchemaVersion: 1 }
            || state.AuthorityId != authority.RepositoryId || adoption.AuthorityId != authority.RepositoryId || state.AdoptionId != adoption.AdoptionId
            || string.IsNullOrWhiteSpace(state.AdoptionId) || state.Revision < 1 || state.BaselineStarted <= 0 || state.Changes.Count == 0)
            throw new InvalidDataException("Work accounting identity or adoption evidence is invalid; restore it rather than resetting consumption.");
        foreach (var (id, change) in state.Changes)
        {
            if (change is null || change.Id != id || change.Milliseconds < 0 || change.Attempts < 1 || change.Consumed < 0 || change.UsedAttempts < 0
                || change.Restoration < 0 || change.Restoration > change.Milliseconds || change.Owners is null || change.Bindings is null || change.Executions is null
                || change.Controllers is null || change.Controllers.Count == 0 || change.AllocationHistory is null)
                throw new InvalidDataException("A containing-change accounting record is corrupt; restore it without replenishing capacity.");
            foreach (var (ownerId, owner) in change.Owners)
                if (owner is null || owner.Id != ownerId || owner.Milliseconds < 0 || owner.Attempts < 1 || owner.Consumed < 0 || owner.UsedAttempts < 0
                    || owner.Restoration < 0 || owner.Restoration > owner.Milliseconds || owner.Checkpoints is null || owner.Outcomes is null
                    || owner.ReflectionWindowMilliseconds is < 30_000)
                    throw new InvalidDataException("A task accounting owner is corrupt; restore its recorded consumption and allocation.");
            if (change.Bindings.Values.Any(owner => !change.Owners.ContainsKey(owner)))
                throw new InvalidDataException("A task accounting binding has lost its immutable owner.");
            foreach (var (executionId, execution) in change.Executions)
            {
                if (execution is null || execution.Reference is null || execution.Reference.ExecutionId != executionId || execution.Reference.AdoptionId != state.AdoptionId
                    || execution.Reference.AuthorityRepositoryId != state.AuthorityId || execution.Reference.ChangeId != change.Id
                    || !change.Owners.ContainsKey(execution.Reference.OwnerTaskId) || change.Bindings.GetValueOrDefault(execution.Reference.TaskId) != execution.Reference.OwnerTaskId
                    || execution.Started <= 0 || execution.Bound <= 0 || execution.Intervals is null || execution.Intervals.Count == 0
                    || !change.Executions.ContainsKey(execution.RootId) || execution.Reference.ParentExecutionId is { } parent && !change.Executions.ContainsKey(parent))
                    throw new InvalidDataException("An execution reservation or inherited accounting reference is corrupt; reconcile it before execution.");
                if (execution.PreparationOwner is not null) ValidatePreparationOwner(execution.PreparationOwner);
            }
        }
        return state;
    }

    private CisAgentWorkResult Result(Ledger state, Change change, string? taskId, string status, bool applied, string? explicitHold = null)
    {
        var owner = taskId is not null && change.Bindings.TryGetValue(taskId, out var id) ? change.Owners.GetValueOrDefault(id) : null;
        var hold = explicitHold ?? (owner is null ? null : Hold(change, owner));
        var executions = change.Executions.Values.Where(item => owner is null || item.Reference.OwnerTaskId == owner.Id)
            .Select(item => new CisAgentWorkExecution(item.Reference.ExecutionId, item.Reference.TaskId, item.Reference.OwnerTaskId,
                item.Reference.LogicalAttemptId, item.Reference.ParentExecutionId, item.RootId, item.Status, Time(item.Started),
                item.Completed is { } end ? Time(end) : null, UnionMilliseconds(item.Intervals.Select(interval => (interval.Start, interval.End ?? Tick()))),
                Math.Max(0, (item.Completed ?? Tick()) - item.Started), item.RootId == item.Reference.ExecutionId && !item.Finalized ? item.Bound : 0,
                item.ProcessId, item.ProcessStartedAtUtc, item.RecoveryReason ?? UnownedPreparation(item))
            { Finalized = item.Finalized, LaunchIntent = item.LaunchIntent, PreparationOwner = item.PreparationOwner }).ToArray();
        return new(status, new(state.AuthorityId, change.Id, taskId, owner?.Id, Revision(state), state.AdoptionId,
            change.Executions.Values.Any(item => item.ConservativeRecovery)
                ? "Measured from the authorized baseline with conservative full-bound recovery charges; earlier historical effort is unmeasured."
                : "Measured from the authorized baseline; earlier historical effort is unmeasured.", executions.Any(item => item.RecoveryReason is not null) ? "uncertain" : "known",
            hold, explicitHold ?? (hold == ReflectionAction ? ReflectionAction : owner?.NextAction) ?? "Select a canonical task and inspect its remaining allowance.",
            Math.Max(0, Tick() - state.BaselineStarted),
            new(change.Milliseconds, change.Consumed, Reserved(change, null), change.Restoration, change.Attempts, change.UsedAttempts),
            owner is null ? null : new(owner.Milliseconds, owner.Consumed, Reserved(change, owner.Id), owner.Restoration, owner.Attempts, owner.UsedAttempts),
            owner?.OpenAttempt, owner?.RepeatedBlockers ?? 0, change.DecisionReference, state.AuthorityId, change.StorageReference,
            executions, owner?.Checkpoints ?? change.Owners.Values.SelectMany(item => item.Checkpoints).ToList())
        {
            AcceptedOutcomeReferences = owner?.Outcomes ?? [],
            Hypothesis = owner?.Hypothesis,
            ExpectedResult = owner?.ExpectedResult,
            ReflectionWindowMilliseconds = owner?.ReflectionWindowMilliseconds,
            LastControllerReflectionMilliseconds = owner is null ? 0 : LastControllerReflection(change, owner),
            ReflectionDue = owner is not null && ReflectionDue(change, owner)
        }, [], applied);
    }

    private static Owner? FindOwner(Change change, CisAgentWorkIdentity identity)
    {
        foreach (var task in new[] { identity.TaskId }.Concat(identity.AncestorTaskIds))
            if (change.Bindings.TryGetValue(task, out var owner) && change.Owners.TryGetValue(owner, out var existing)) return existing;
            else if (change.Owners.TryGetValue(task, out existing)) return existing;
        return null;
    }
    private string? Hold(Change change, Owner owner)
        => change.Executions.Values.FirstOrDefault(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id && item.RecoveryReason is not null)?.RecoveryReason
            ?? owner.Hold
            ?? (change.Executions.Values.Any(item => !item.Finalized && item.Reference.OwnerTaskId == owner.Id)
                ? change.Executions.Values.Where(item => item.Reference.OwnerTaskId == owner.Id).Select(UnownedPreparation).FirstOrDefault(reason => reason is not null)
                    ?? "An owned execution or child reservation is still active. Inspect or reconcile it before conflicting work."
                : owner.Milliseconds - owner.Consumed - owner.Restoration < 30_000 || change.Milliseconds - change.Consumed - Reserved(change, null) - change.Restoration < 30_000
                    ? "Remaining hard allowance cannot support the minimum operation while preserving restoration capacity. Use cis agent work allocate through the original allocation authority for unused outer capacity or a valid delegated transfer; fixed limits remain binding."
                    : owner.OpenAttempt is null && (owner.UsedAttempts >= owner.Attempts || change.UsedAttempts >= change.Attempts)
                        ? "The logical-attempt allowance is exhausted. Seek the applicable allocation authority."
                        : ReflectionDue(change, owner) ? ReflectionAction : null);

    private const string ReflectionAction = "Controller reflection is due for cumulative active work. Record Outcome, Evidence, measured Cost context, Approach, Decision and the next bounded action through cis agent work progress; the current logical attempt can remain open.";

    private bool ReflectionDue(Change change, Owner owner)
        => owner.ReflectionWindowMilliseconds is { } window
            && MeasuredOwnerMilliseconds(change, owner) - LastControllerReflection(change, owner) >= window;

    private long LastControllerReflection(Change change, Owner owner)
    {
        var measured = MeasuredOwnerMilliseconds(change, owner);
        return owner.Checkpoints.Where(record => QualifyingReflection(change, record, measured))
            .Select(record => record.ActiveMilliseconds).DefaultIfEmpty(0).Max();
    }

    private static bool QualifyingReflection(Change change, CisAgentWorkCheckpointRecord? record, long measured)
    {
        if (record is null || record.Checkpoint is not { } checkpoint || record.ActiveMilliseconds < 0 || record.ActiveMilliseconds > measured
            || !change.Controllers.Contains(record.Actor, StringComparer.Ordinal)
            || !BoundedText(checkpoint.Outcome) || !BoundedText(checkpoint.Approach) || !BoundedText(checkpoint.Decision) || !BoundedText(checkpoint.NextAction)
            || checkpoint.Evidence is not { Count: > 0 and <= 40 } || checkpoint.Evidence.Any(evidence => !BoundedText(evidence))) return false;
        if (record.Origin is not null) return record.Origin == "controller-progress";
        // These are exact historical CIS producer identities, not a classifier
        // over the controller's engineering judgment or narrative.
        return record.Actor != "CIS measured execution"
            && checkpoint.Outcome is not ("Controller disposition" or "Never-launched reservation reconciled");
    }

    private long MeasuredOwnerMilliseconds(Change change, Owner owner)
    {
        var tick = Tick();
        return checked(owner.Consumed + change.Executions.Values.Where(execution => !execution.Finalized && execution.Reference.OwnerTaskId == owner.Id)
            .GroupBy(execution => execution.RootId, StringComparer.Ordinal)
            .Sum(tree => UnionMilliseconds(tree.SelectMany(execution => execution.Intervals).Select(interval => (interval.Start, interval.End ?? tick)))));
    }

    private static string? UnownedPreparation(Execution execution)
        => !execution.Stopped && !execution.Finalized && !execution.LaunchIntent && execution.ProcessId is null && execution.PreparationOwner is null
            ? "Preparation ownership is unknown for an unresolved historical reservation. Preserve its capacity; elapsed grace and absent owner metadata do not prove stopped preparation." : null;

    private static void ValidatePreparationOwner(CisAgentWorkPreparationOwner owner)
    {
        Text(owner.HostId, "Preparation host identity");
        Text(owner.ProcessStartedAtUtc, "Preparation process start time");
        if (owner.ProcessId <= 0 || !DateTimeOffset.TryParse(owner.ProcessStartedAtUtc, out _))
            throw new InvalidDataException("Preparation ownership requires its verified process ID and start time.");
    }

    private static bool BoundedText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000 && !value.Contains('\0');
    private static long Reserved(Change change, string? owner)
        => change.Executions.Values.Where(item => !item.Finalized && item.Reference.ParentExecutionId is null && (owner is null || item.Reference.OwnerTaskId == owner)).Sum(item => item.Bound);
    internal static long UnionMilliseconds(IEnumerable<(long Start, long End)> intervals)
    {
        long total = 0, start = 0, end = 0; var first = true;
        foreach (var interval in intervals.OrderBy(item => item.Start))
        {
            if (interval.End < interval.Start) throw new InvalidDataException("An accounting interval ends before it starts.");
            if (first) { start = interval.Start; end = interval.End; first = false; }
            else if (interval.Start <= end) end = Math.Max(end, interval.End);
            else { total = checked(total + end - start); start = interval.Start; end = interval.End; }
        }
        return first ? 0 : checked(total + end - start);
    }
    private static Change ChangeFor(Ledger state, string id) => state.Changes.GetValueOrDefault(id) ?? throw new InvalidDataException("The containing change has no authorized allocation. Supply that owning decision once before governed execution.");
    private static void Authorize(Change change, string actor)
    { Text(actor, "Controller actor"); if (!change.Controllers.Contains(actor.Trim(), StringComparer.Ordinal)) throw new InvalidDataException("This actor is not delegated as a controller by the owning allocation."); }
    private static void CheckRevision(Ledger state, string? expected)
    { if (expected != Revision(state)) throw new InvalidDataException("Work accounting changed. Read its current revision before recording this controller decision."); }
    private static void ValidateIdentity(CisRepositoryContext authority, string change, CisAgentWorkIdentity identity)
    { if (identity.AuthorityRepositoryId != authority.RepositoryId || identity.ChangeId != change || identity.AncestorTaskIds is null || identity.AcceptedOutcomeReferences is null) throw new InvalidDataException("Work ownership must resolve through the same CIS authority and containing change."); Text(identity.TaskId, "Canonical task identity"); }
    private static long Milliseconds(long seconds) => seconds is >= 0 and <= 31_536_000 ? checked(seconds * 1000) : throw new InvalidDataException("Allocation seconds must be finite and bounded to one year.");
    private static int Attempts(int value) => value is >= 1 and <= 10000 ? value : throw new InvalidDataException("Supply between one and 10,000 logical attempts.");
    private static void Text(string? value, string label)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > 4000 || value.Contains('\0')) throw new InvalidDataException(label + " is required and must be bounded."); }
    private static string Revision(Ledger state) => state.AdoptionId + ":" + state.Revision;
    private long Tick() => now().ToUniversalTime().ToUnixTimeMilliseconds();
    private static string Time(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value).ToString("O");
    private static string SafePath(CisRepositoryContext authority, string relative)
    {
        if (CisPathSafety.IsReparsePoint(authority.RepositoryPath) || !CisPathSafety.TryResolveUnderRoot(authority.RepositoryPath, relative, out var path)
            || CisPathSafety.ContainsReparsePoint(authority.RepositoryPath, path))
            throw new InvalidDataException("Work accounting requires a regular owned authority path without links.");
        return path;
    }
    private static void Atomic(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    // ReplaceFile preserves existing delete-sharing snapshots on
                    // Windows; MoveFile overwrite can deny the same open handle.
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    break;
                }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string ReadSnapshot(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Work accounting exceeds its bounded state format.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() >= 0) throw new InvalidDataException("Work accounting changed outside atomic replacement; restore a coherent snapshot.");
        return Encoding.UTF8.GetString(bytes);
    }
    private static FileStream AcquireTransactionLock(string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
        }
    }
    private static bool Recoverable(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or OverflowException;
    private static CisAgentWorkResult Failure(Exception error) => new("held", null, ["Work accounting must be restored or reconciled before execution: " + error.Message], false);
    private static CisAgentWorkResult Missing() => new("missing-allocation", null, ["No authorized work allocation exists. Establish the owning change/task allocation and explicit historical-effort baseline before governed execution."], false);
}
